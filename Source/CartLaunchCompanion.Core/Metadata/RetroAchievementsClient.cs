using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Metadata;

public sealed record RetroAchievementsGameMatch(
    int GameId,
    string Title,
    int ConsoleId,
    string ConsoleName,
    string ImageIcon,
    int AchievementCount,
    int Points,
    string Hash);

public sealed record RetroAchievementProgressItem(
    int AchievementId,
    string Title,
    string Description,
    int Points,
    string BadgeName,
    DateTimeOffset? EarnedAt,
    bool EarnedHardcore);

public sealed record RetroAchievementsGameProgress(
    int GameId,
    string Title,
    int TotalAchievements,
    int EarnedAchievements,
    int EarnedHardcoreAchievements,
    int EarnedPoints,
    string Award,
    IReadOnlyList<RetroAchievementProgressItem> RecentAchievements);

public sealed class RetroAchievementsClient(HttpClient httpClient)
{
    private const string ApiRoot = "https://retroachievements.org/API/";

    public async Task<bool> ValidateAccountAsync(
        string userName,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(apiKey)) return false;
        var uri = ApiRoot + "API_GetUserProfile.php?u=" + Uri.EscapeDataString(userName.Trim()) + "&y=" + Uri.EscapeDataString(apiKey.Trim());
        using var response = await SendAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               (document.RootElement.TryGetProperty("User", out _) || document.RootElement.TryGetProperty("user", out _));
    }

    public async Task<RetroAchievementsGameMatch?> FindGameByHashAsync(
        string platformLabel,
        string hash,
        string apiKey,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var consoles = await GetConsolesAsync(apiKey, cacheDirectory, cancellationToken);
        var console = MatchConsole(platformLabel, consoles);
        if (console is null) return null;

        var games = await GetGamesAsync(console.Id, apiKey, cacheDirectory, cancellationToken);
        var normalizedHash = hash.Trim().ToLowerInvariant();
        var game = games.FirstOrDefault(item => item.Hashes.Any(itemHash => itemHash.Equals(normalizedHash, StringComparison.OrdinalIgnoreCase)));
        return game is null ? null : new RetroAchievementsGameMatch(
            game.Id, game.Title, game.ConsoleId, game.ConsoleName, game.ImageIcon,
            game.NumAchievements, game.Points, normalizedHash);
    }

    public async Task<RetroAchievementsGameProgress> GetUserGameProgressAsync(
        string userName,
        int gameId,
        string apiKey,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToLowerInvariant())))[..16].ToLowerInvariant();
        var cachePath = Path.Combine(cacheDirectory, $"retroachievements-progress-{userHash}-{gameId}.json");
        byte[] bytes;
        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromMinutes(2))
        {
            bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }
        else
        {
            var uri = ApiRoot + "API_GetGameInfoAndUserProgress.php?g=" + gameId +
                      "&u=" + Uri.EscapeDataString(userName.Trim()) + "&a=1&y=" + Uri.EscapeDataString(apiKey.Trim());
            using var response = await SendAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
        }

        using var document = JsonDocument.Parse(bytes);
        return ParseProgress(document.RootElement, gameId);
    }

    public static string ResolveBadgeUrl(string badgeName)
    {
        var value = badgeName.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }
        value = value.TrimStart('/');
        if (value.StartsWith("Badge/", StringComparison.OrdinalIgnoreCase)) value = value[6..];
        if (!Path.HasExtension(value)) value += ".png";
        return "https://media.retroachievements.org/Badge/" + Uri.EscapeDataString(value);
    }

    private static RetroAchievementsGameProgress ParseProgress(JsonElement root, int requestedGameId)
    {
        var total = ReadInt(root, "NumAchievements");
        var earned = ReadInt(root, "NumAwardedToUser");
        var hardcore = ReadInt(root, "NumAwardedToUserHardcore");
        var achievements = new List<RetroAchievementProgressItem>();
        if (root.TryGetProperty("Achievements", out var achievementMap) && achievementMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in achievementMap.EnumerateObject())
            {
                var item = property.Value;
                var earnedAt = ReadDate(item, "DateEarnedHardcore") ?? ReadDate(item, "DateEarned");
                if (earnedAt is null) continue;
                achievements.Add(new RetroAchievementProgressItem(
                    ReadInt(item, "ID", int.TryParse(property.Name, out var keyId) ? keyId : 0),
                    ReadString(item, "Title"),
                    ReadString(item, "Description"),
                    ReadInt(item, "Points"),
                    ReadString(item, "BadgeName"),
                    earnedAt,
                    !string.IsNullOrWhiteSpace(ReadString(item, "DateEarnedHardcore"))));
            }
        }

        var award = total > 0 && hardcore >= total
            ? "MASTERED"
            : total > 0 && earned >= total
                ? "COMPLETED"
                : earned > 0 ? "IN PROGRESS" : "NOT STARTED";
        return new RetroAchievementsGameProgress(
            ReadInt(root, "ID", requestedGameId),
            ReadString(root, "Title"),
            total,
            earned,
            hardcore,
            achievements.Sum(item => item.Points),
            award,
            achievements.OrderByDescending(item => item.EarnedAt).Take(4).ToArray());
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int ReadInt(JsonElement element, string name, int fallback = 0)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), out var value) ? value : null;

    private async Task<IReadOnlyList<ConsoleDto>> GetConsolesAsync(string key, string cache, CancellationToken token)
    {
        var path = Path.Combine(cache, "retroachievements-consoles.json");
        return await GetCachedAsync(path, TimeSpan.FromDays(7),
            ApiRoot + "API_GetConsoleIDs.php?a=1&g=1&y=" + Uri.EscapeDataString(key),
            RetroAchievementsJsonContext.Default.ListConsoleDto, token);
    }

    private async Task<IReadOnlyList<GameDto>> GetGamesAsync(int consoleId, string key, string cache, CancellationToken token)
    {
        var path = Path.Combine(cache, $"retroachievements-games-{consoleId}.json");
        return await GetCachedAsync(path, TimeSpan.FromDays(7),
            ApiRoot + $"API_GetGameList.php?i={consoleId}&h=1&f=1&y=" + Uri.EscapeDataString(key),
            RetroAchievementsJsonContext.Default.ListGameDto, token);
    }

    private async Task<IReadOnlyList<T>> GetCachedAsync<T>(
        string cachePath,
        TimeSpan lifetime,
        string uri,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> jsonType,
        CancellationToken token)
    {
        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < lifetime)
        {
            await using var cached = File.OpenRead(cachePath);
            return await JsonSerializer.DeserializeAsync(cached, jsonType, token) ?? [];
        }

        using var response = await SendAsync(uri, token);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        var result = JsonSerializer.Deserialize(bytes, jsonType) ?? [];
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllBytesAsync(cachePath, bytes, token);
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(string uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/2.3");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private static ConsoleDto? MatchConsole(string platformLabel, IReadOnlyList<ConsoleDto> consoles)
    {
        var wanted = Alias(Normalize(platformLabel));
        return consoles.FirstOrDefault(console => Alias(Normalize(console.Name)) == wanted)
            ?? consoles.FirstOrDefault(console =>
                Alias(Normalize(console.Name)).Contains(wanted, StringComparison.Ordinal) ||
                wanted.Contains(Alias(Normalize(console.Name)), StringComparison.Ordinal));
    }

    private static string Alias(string value) => value switch
    {
        "psp" => "playstationportable",
        "ps1" or "sonyplaystation" => "playstation",
        "ps2" or "sonyplaystation2" => "playstation2",
        "genesis" or "segagenesis" => "megadrive",
        "snes" or "superfamicom" => "supernintendoentertainmentsystem",
        "nes" or "famicom" => "nintendoentertainmentsystem",
        "gba" => "gameboyadvance",
        "gbc" => "gameboycolor",
        "gb" => "gameboy",
        "n64" => "nintendo64",
        "nds" => "nintendods",
        _ => value
    };

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public sealed class ConsoleDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }
        [JsonPropertyName("Name")] public string Name { get; set; } = "";
    }

    public sealed class GameDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }
        [JsonPropertyName("Title")] public string Title { get; set; } = "";
        [JsonPropertyName("ConsoleID")] public int ConsoleId { get; set; }
        [JsonPropertyName("ConsoleName")] public string ConsoleName { get; set; } = "";
        [JsonPropertyName("ImageIcon")] public string ImageIcon { get; set; } = "";
        [JsonPropertyName("NumAchievements")] public int NumAchievements { get; set; }
        [JsonPropertyName("Points")] public int Points { get; set; }
        [JsonPropertyName("Hashes")] public List<string> Hashes { get; set; } = [];
    }
}

[JsonSerializable(typeof(List<RetroAchievementsClient.ConsoleDto>))]
[JsonSerializable(typeof(List<RetroAchievementsClient.GameDto>))]
internal partial class RetroAchievementsJsonContext : JsonSerializerContext;
