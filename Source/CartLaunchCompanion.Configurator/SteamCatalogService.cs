using System.Text.Json;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Configurator;

public sealed record SteamCatalogMatch(
    uint AppId,
    string Name,
    int Score,
    Bitmap? Artwork = null,
    long? SteamGridDbGameId = null)
{
    public bool HasSteamAppId => AppId > 0;
    public string SourceText => HasSteamAppId
        ? SteamGridDbGameId is null ? $"Steam App ID: {AppId}" : $"Steam App ID: {AppId} · SteamGridDB game {SteamGridDbGameId}"
        : $"SteamGridDB game {SteamGridDbGameId} · Artwork available without Steam";
}

public sealed class SteamCatalogService(HttpClient httpClient)
{
    private const string Endpoint = "https://api.steampowered.com/IStoreService/GetAppList/v1/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<SteamCatalogMatch>> SearchAsync(
        string query,
        string apiKey,
        string steamGridDbApiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (uint.TryParse(query.Trim(), out var appId))
            return await LookupByAppIdAsync(appId, cancellationToken);

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var apps = await LoadCatalogAsync(apiKey.Trim(), cancellationToken);
        var normalizedQuery = Normalize(query);

        var steamMatches = apps
            .Select(app => new SteamCatalogMatch(
                app.AppId,
                app.Name,
                Score(normalizedQuery, Normalize(app.Name))))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Name.Length)
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var steamGridDbMatches = string.IsNullOrWhiteSpace(steamGridDbApiKey)
            ? []
            : await SearchSteamGridDbAsync(query, steamGridDbApiKey, cancellationToken);

        var matches = steamMatches
            .Concat(steamGridDbMatches)
            .GroupBy(match => Normalize(match.Name))
            .Select(group => group.OrderByDescending(match => match.HasSteamAppId).First())
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Name.Length)
            .Take(12)
            .ToArray();

        return await Task.WhenAll(matches.Select(match => match.HasSteamAppId
            ? LoadArtworkAsync(match, cancellationToken)
            : LoadSteamGridDbArtworkAsync(match, steamGridDbApiKey, cancellationToken)));
    }

    private async Task<SteamCatalogMatch[]> SearchSteamGridDbAsync(
        string query,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(query));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var normalizedQuery = Normalize(query);
        return data.EnumerateArray()
            .Where(item => item.TryGetProperty("id", out _) && item.TryGetProperty("name", out _))
            .Select(item => new SteamCatalogMatch(
                0,
                item.GetProperty("name").GetString() ?? "Unknown game",
                Score(normalizedQuery, Normalize(item.GetProperty("name").GetString() ?? "")),
                SteamGridDbGameId: item.GetProperty("id").GetInt64()))
            .Where(match => match.Score > 0)
            .Take(12)
            .ToArray();
    }

    private async Task<SteamCatalogMatch> LoadSteamGridDbArtworkAsync(
        SteamCatalogMatch match,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (match.SteamGridDbGameId is null || string.IsNullOrWhiteSpace(apiKey)) return match;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://www.steamgriddb.com/api/v2/heroes/game/{match.SteamGridDbGameId}?types=static");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return match;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return match;
            var first = data.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return match;
            var url = first.TryGetProperty("thumb", out var thumb) ? thumb.GetString() :
                first.TryGetProperty("url", out var full) ? full.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) return match;
            return await DownloadSteamGridDbArtworkAsync(match, url, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested) { return match; }
    }

    private async Task<SteamCatalogMatch> DownloadSteamGridDbArtworkAsync(
        SteamCatalogMatch match,
        string url,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartLaunchCompanion", "Configurator", "SteamGridDbArtwork");
        var path = Path.Combine(folder, match.SteamGridDbGameId + ".jpg");
        if (!File.Exists(path))
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return match;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length < 1024) return match;
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        return match with { Artwork = new Bitmap(path) };
    }

    private async Task<IReadOnlyList<SteamCatalogMatch>> LookupByAppIdAsync(
        uint appId,
        CancellationToken cancellationToken)
    {
        var url = "https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english&cc=US";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        if (!document.RootElement.TryGetProperty(appId.ToString(), out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("name", out var nameElement) ||
            string.IsNullOrWhiteSpace(nameElement.GetString()))
            return [];

        var match = new SteamCatalogMatch(appId, nameElement.GetString()!, 1000);
        return [await LoadArtworkAsync(match, cancellationToken)];
    }

    private async Task<SteamCatalogMatch> LoadArtworkAsync(
        SteamCatalogMatch match,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartLaunchCompanion",
            "Configurator",
            "SteamArtwork");
        var path = Path.Combine(folder, match.AppId + ".jpg");

        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
            {
                var url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{match.AppId}/header.jpg";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/2.0");
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                    return match;

                Directory.CreateDirectory(folder);
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 1024) return match;
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }

            return match with { Artwork = new Bitmap(path) };
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return match;
        }
    }

    private async Task<IReadOnlyList<CatalogApp>> LoadCatalogAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CartLaunchCompanion",
            "Configurator");
        var cachePath = Path.Combine(cacheFolder, "steam-catalog.json");

        if (File.Exists(cachePath) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime)
        {
            return JsonSerializer.Deserialize<List<CatalogApp>>(
                       await File.ReadAllTextAsync(cachePath, cancellationToken)) ?? [];
        }

        var results = new List<CatalogApp>();
        uint lastAppId = 0;
        while (true)
        {
            var input = JsonSerializer.Serialize(new
            {
                include_games = true,
                include_dlc = false,
                include_software = false,
                include_videos = false,
                include_hardware = false,
                last_appid = lastAppId,
                max_results = 50000
            });
            var url = Endpoint + "?key=" + Uri.EscapeDataString(apiKey) +
                      "&input_json=" + Uri.EscapeDataString(input);
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or
                System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(
                    "Steam rejected this API key. Confirm it was copied from steamcommunity.com/dev/apikey, then try again.");
            }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            var page = ReadApps(document.RootElement).ToArray();
            results.AddRange(page);
            if (page.Length == 0)
                break;

            var next = page.Max(app => app.AppId);
            if (next <= lastAppId || page.Length < 50000)
                break;
            lastAppId = next;
        }

        Directory.CreateDirectory(cacheFolder);
        await File.WriteAllTextAsync(
            cachePath,
            JsonSerializer.Serialize(results),
            cancellationToken);
        return results;
    }

    private static IEnumerable<CatalogApp> ReadApps(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("apps", out var apps) ||
            apps.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var app in apps.EnumerateArray())
        {
            if (!app.TryGetProperty("appid", out var id) ||
                !app.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()))
                continue;
            yield return new CatalogApp(id.GetUInt32(), name.GetString()!);
        }
    }

    private static int Score(string query, string candidate)
    {
        if (candidate == query) return 1000;
        if (candidate.StartsWith(query, StringComparison.Ordinal)) return 800 - Math.Min(200, candidate.Length - query.Length);
        if (candidate.Contains(query, StringComparison.Ordinal)) return 600 - Math.Min(200, candidate.Length - query.Length);

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchingWords = words.Count(candidate.Contains);
        return matchingWords == words.Length ? 400 + matchingWords * 10 : matchingWords * 40;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant()
            .Split([' ', ':', '-', '–', '—', '™', '®'], StringSplitOptions.RemoveEmptyEntries));

    private sealed record CatalogApp(uint AppId, string Name);
}
