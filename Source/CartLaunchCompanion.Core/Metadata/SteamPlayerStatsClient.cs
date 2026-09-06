using System.Collections.Concurrent;
using System.Text.Json;

namespace CartLaunchCompanion.Core.Metadata;

public sealed record SteamPlayerStats(
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayed,
    int? EarnedAchievements,
    int? TotalAchievements);

public sealed class SteamPlayerStatsClient(HttpClient httpClient)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, SteamPlayerStats? Value)> _cache = new();

    public async Task<SteamPlayerStats?> GetAsync(
        string apiKey,
        string steamAccountId,
        uint appId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamAccountId))
            return null;

        var cacheKey = $"{steamAccountId}:{appId}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            return cached.Value;

        var playtimeTask = LoadPlaytimeAsync(apiKey, steamAccountId, appId, cancellationToken);
        var achievementsTask = LoadAchievementsAsync(apiKey, steamAccountId, appId, cancellationToken);
        await Task.WhenAll(playtimeTask, achievementsTask);

        var playtime = await playtimeTask;
        var achievements = await achievementsTask;
        if (playtime is null && achievements is null)
        {
            _cache[cacheKey] = (DateTimeOffset.UtcNow.AddMinutes(5), null);
            return null;
        }

        var result = new SteamPlayerStats(
            playtime?.Minutes ?? 0,
            playtime?.LastPlayed,
            achievements?.Earned,
            achievements?.Total);
        _cache[cacheKey] = (DateTimeOffset.UtcNow.AddMinutes(5), result);
        return result;
    }

    private async Task<(int Minutes, DateTimeOffset? LastPlayed)?> LoadPlaytimeAsync(
        string apiKey,
        string steamAccountId,
        uint appId,
        CancellationToken cancellationToken)
    {
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/" +
                  $"?key={Uri.EscapeDataString(apiKey)}&steamid={steamAccountId}" +
                  $"&include_appinfo=false&include_played_free_games=true&appids_filter[0]={appId}&format=json";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("response", out var responseNode) ||
            !responseNode.TryGetProperty("games", out var games) ||
            games.ValueKind != JsonValueKind.Array || games.GetArrayLength() == 0)
            return null;

        var game = games[0];
        var minutes = game.TryGetProperty("playtime_forever", out var playtimeNode)
            ? playtimeNode.GetInt32()
            : 0;
        DateTimeOffset? lastPlayed = null;
        if (game.TryGetProperty("rtime_last_played", out var lastPlayedNode) &&
            lastPlayedNode.TryGetInt64(out var unixTime) && unixTime > 0)
            lastPlayed = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime();

        return (minutes, lastPlayed);
    }

    private async Task<(int Earned, int Total)?> LoadAchievementsAsync(
        string apiKey,
        string steamAccountId,
        uint appId,
        CancellationToken cancellationToken)
    {
        var url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/" +
                  $"?key={Uri.EscapeDataString(apiKey)}&steamid={steamAccountId}&appid={appId}&l=english";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("playerstats", out var playerStats) ||
            !playerStats.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !playerStats.TryGetProperty("achievements", out var achievements) ||
            achievements.ValueKind != JsonValueKind.Array || achievements.GetArrayLength() == 0)
            return null;

        var total = 0;
        var earned = 0;
        foreach (var achievement in achievements.EnumerateArray())
        {
            total++;
            if (achievement.TryGetProperty("achieved", out var achieved) && achieved.GetInt32() > 0)
                earned++;
        }

        return total > 0 ? (earned, total) : null;
    }
}
