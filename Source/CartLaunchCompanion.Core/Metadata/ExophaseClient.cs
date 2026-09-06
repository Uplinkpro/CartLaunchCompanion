using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CartLaunchCompanion.Core.Metadata;

public sealed record ExophaseGameProgress(
    string Title,
    IReadOnlyList<string> Platforms,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayed,
    int EarnedAchievements,
    int TotalAchievements,
    double CompletionPercent,
    long GameId = 0,
    IReadOnlyList<ExophaseAchievement>? RecentAchievements = null);

public sealed record ExophaseAchievement(
    string Title,
    string Description,
    string IconUrl,
    DateTimeOffset? EarnedAt);

/// <summary>
/// Reads the public Exophase profile feed. Exophase does not document this feed,
/// so all failures are intentionally recoverable and stale cached data is kept.
/// </summary>
public sealed partial class ExophaseClient(HttpClient httpClient)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    public async Task<string?> ResolvePlayerIdAsync(
        string profileUrlOrPlayerId,
        CancellationToken cancellationToken = default)
    {
        var value = profileUrlOrPlayerId.Trim();
        if (PlayerIdRegex().IsMatch(value))
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var profileUri) ||
            !profileUri.Host.EndsWith("exophase.com", StringComparison.OrdinalIgnoreCase))
            return null;

        using var request = CreateRequest(profileUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = ProfileIdRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<ExophaseGameProgress?> FindGameAsync(
        string playerId,
        string title,
        string platform,
        string cacheDirectory,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false,
        bool cacheOnly = false)
    {
        var games = await GetGamesAsync(
            playerId,
            cacheDirectory,
            cancellationToken,
            forceRefresh,
            cacheOnly);
        var normalizedTitle = Normalize(title);
        if (normalizedTitle.Length == 0)
            return null;

        var candidates = games
            .Where(game => Normalize(game.Title) == normalizedTitle)
            .ToList();
        if (candidates.Count == 0)
            return null;

        var normalizedPlatform = NormalizePlatform(platform);
        return candidates
            .OrderByDescending(game => game.Platforms.Any(item =>
                NormalizePlatform(item) == normalizedPlatform))
            .ThenByDescending(game => game.TotalAchievements)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<ExophaseGameProgress>> GetGamesAsync(
        string playerId,
        string cacheDirectory,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false,
        bool cacheOnly = false)
    {
        if (!PlayerIdRegex().IsMatch(playerId.Trim()))
            return [];

        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, $"player-{playerId.Trim()}.json");
        var cached = await ReadCacheAsync(cachePath, cancellationToken);
        if (cacheOnly)
            return cached?.Games ?? [];
        if (!forceRefresh && cached is not null &&
            DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime)
            return cached.Games;

        try
        {
            var games = new List<ExophaseGameProgress>();
            for (var page = 1; page <= 200; page++)
            {
                var uri = new Uri(
                    $"https://api.exophase.com/public/player/{Uri.EscapeDataString(playerId.Trim())}/games" +
                    $"?page={page}&environment=&sort=1&showHidden=0");
                using var response = await SendPublicApiRequestAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (!ReadBoolean(root, "success") ||
                    !root.TryGetProperty("games", out var pageGames) ||
                    pageGames.ValueKind != JsonValueKind.Array ||
                    pageGames.GetArrayLength() == 0)
                    break;

                foreach (var game in pageGames.EnumerateArray())
                    if (ParseGame(game) is { } parsed)
                        games.Add(parsed);

                // The public feed is undocumented and rate limited. Spacing
                // its pages keeps large profiles from being rejected midway.
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            if (games.Count == 0)
                return cached?.Games ?? [];

            var fresh = new CacheDocument(DateTimeOffset.UtcNow, games);
            await File.WriteAllTextAsync(
                cachePath,
                JsonSerializer.Serialize(fresh),
                cancellationToken);
            return games;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && cached is not null)
        {
            Trace.WriteLine($"Exophase refresh kept cached data ({ex.GetType().Name}: {ex.Message}).");
            return cached.Games;
        }
    }

    public static async Task<int> ImportBrowserGamesAsync(
        string playerId,
        string json,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!PlayerIdRegex().IsMatch(playerId.Trim()))
            return 0;

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return 0;

        var games = new List<ExophaseGameProgress>();
        foreach (var game in document.RootElement.EnumerateArray())
            if (ParseGame(game) is { } parsed)
                games.Add(parsed);

        if (games.Count == 0)
            return 0;

        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, $"player-{playerId.Trim()}.json");
        var cache = new CacheDocument(DateTimeOffset.UtcNow, games);
        await File.WriteAllTextAsync(
            cachePath,
            JsonSerializer.Serialize(cache),
            cancellationToken);
        return games.Count;
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("application/json, text/html;q=0.9, */*;q=0.5");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.Referrer = new Uri("https://www.exophase.com/");
        return request;
    }

    private async Task<HttpResponseMessage> SendPublicApiRequestAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var response = await SendApiAttemptAsync(uri, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return response;

        response.Dispose();

        // Exophase currently rejects cold API requests from some networks. A
        // visit to its public site establishes the same first-party session a
        // normal browser receives, after which the public JSON feed may be read.
        using (var warmupRequest = CreateRequest(new Uri("https://www.exophase.com/")))
        using (var warmupResponse = await httpClient.SendAsync(warmupRequest, cancellationToken))
        {
            Trace.WriteLine($"Exophase API requested a site warm-up ({(int)warmupResponse.StatusCode}).");
        }

        return await SendApiAttemptAsync(uri, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendApiAttemptAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        request.Headers.TryAddWithoutValidation("Origin", "https://www.exophase.com");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static ExophaseGameProgress? ParseGame(JsonElement game)
    {
        if (game.ValueKind != JsonValueKind.Object ||
            !game.TryGetProperty("meta", out var metadata) ||
            metadata.ValueKind != JsonValueKind.Object)
            return null;

        var title = ReadString(metadata, "title");
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var platforms = new List<string>();
        if (metadata.TryGetProperty("platforms", out var platformNodes) &&
            platformNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in platformNodes.EnumerateArray())
            {
                var name = node.ValueKind == JsonValueKind.Object
                    ? ReadString(node, "name")
                    : node.ValueKind == JsonValueKind.String ? node.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(name))
                    platforms.Add(name);
            }
        }

        var playtimeUnits = game.TryGetProperty("playtimeUnits", out var units) &&
                            units.ValueKind == JsonValueKind.Object
            ? units
            : default;
        var playtimeMinutes = playtimeUnits.ValueKind == JsonValueKind.Object
            ? Math.Max(0, ReadInt(playtimeUnits, "hours") * 60 + ReadInt(playtimeUnits, "minutes"))
            : 0;
        var lastPlayedUnix = ReadLong(game, "lastplayed_utc");
        var lastPlayed = lastPlayedUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(lastPlayedUnix)
            : (DateTimeOffset?)null;

        var completionPercent = Math.Clamp(ReadDouble(game, "percent"), 0d, 100d);
        var earnedAchievements = Math.Max(0, ReadInt(game, "earned_awards"));
        if (earnedAchievements == 0)
        {
            earnedAchievements = Math.Max(0,
                ReadInt(game, "earned_bronze") +
                ReadInt(game, "earned_silver") +
                ReadInt(game, "earned_gold") +
                ReadInt(game, "earned_platinum"));
        }

        var totalAchievements = Math.Max(0, ReadInt(game, "total_awards"));
        if (totalAchievements == 0 && earnedAchievements > 0 && completionPercent > 0)
            totalAchievements = Math.Max(earnedAchievements,
                (int)Math.Round(earnedAchievements * 100d / completionPercent));

        var gameId = ReadLong(game, "_clcGameId");
        if (gameId <= 0) gameId = ReadLong(game, "master_playerid");
        if (gameId <= 0) gameId = ReadLong(game, "master_id");
        if (gameId <= 0) gameId = ReadLong(metadata, "id");
        if (gameId <= 0) gameId = ReadLong(game, "game_id");
        if (gameId <= 0) gameId = ReadLong(game, "id");

        return new ExophaseGameProgress(
            title,
            platforms,
            playtimeMinutes,
            lastPlayed,
            earnedAchievements,
            totalAchievements,
            completionPercent,
            gameId,
            ParseAchievements(game));
    }

    private static IReadOnlyList<ExophaseAchievement> ParseAchievements(JsonElement game)
    {
        if (!game.TryGetProperty("_earned", out var earned))
            return [];

        var results = new List<ExophaseAchievement>();
        CollectAchievementObjects(earned, results);
        return results
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .DistinctBy(item => $"{item.Title}\u001f{item.EarnedAt:O}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.EarnedAt)
            .Take(12)
            .ToList();
    }

    private static void CollectAchievementObjects(
        JsonElement node,
        List<ExophaseAchievement> results)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                CollectAchievementObjects(item, results);
            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
            return;

        var metadata = node.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
            ? meta
            : node;
        var title = ReadFirstString(metadata, "title", "name", "display_name", "achievement_name");
        if (!string.IsNullOrWhiteSpace(title))
        {
            var description = ReadFirstString(metadata, "description", "desc", "detail");
            var icon = ReadFirstString(metadata, "icon", "image", "image_url", "icon_url", "badge", "badge_url");
            if (string.IsNullOrWhiteSpace(icon))
                icon = ReadFirstString(node, "icon", "image", "image_url", "icon_url", "badge", "badge_url");
            var earnedAt = ReadFirstTimestamp(node,
                "earned_at", "earned_utc", "unlock_time", "unlocked_at", "timestamp", "date");
            results.Add(new ExophaseAchievement(title, description, icon, earnedAt));
        }

        foreach (var property in node.EnumerateObject())
        {
            if (property.NameEquals("meta")) continue;
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                CollectAchievementObjects(property.Value, results);
        }
    }

    private static string ReadFirstString(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadString(node, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static DateTimeOffset? ReadFirstTimestamp(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            if (!node.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix) && unix > 0)
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            if (value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed;
        }
        return null;
    }

    private static async Task<CacheDocument?> ReadCacheAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<CacheDocument>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Normalize(string value) =>
        NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), "");

    private static string NormalizePlatform(string value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "pc" or "windows" or "win32" or "win64" => "windows",
            "linux" or "steamos" => "linux",
            "gba" or "gameboyadvance" => "gameboyadvance",
            "psp" or "playstationportable" => "playstationportable",
            "ps1" or "psx" or "playstation" => "playstation",
            _ => normalized
        };
    }

    private static string ReadString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool ReadBoolean(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0);

    private static int ReadInt(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) ? number : 0;

    private static long ReadLong(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) ? number : 0L;

    private static double ReadDouble(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) ? number : 0d;

    private sealed record CacheDocument(DateTimeOffset FetchedAt, List<ExophaseGameProgress> Games);

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerIdRegex();

    [GeneratedRegex("playerProfileId\\s*(?:=|:)\\s*[\\\"']?([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();
}
