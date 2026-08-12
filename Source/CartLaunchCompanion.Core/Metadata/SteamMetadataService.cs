using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Metadata;

public sealed class SteamMetadataService(
    HttpClient httpClient,
    IGamePathResolver pathResolver)
    : IGameMetadataService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<GameMetadataEnrichmentResult> EnrichAsync(
        string gameFolder,
        GameConfiguration configuration,
        PortablePaths portablePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(portablePaths);

        var result = new GameMetadataEnrichmentResult();
        var steamId = ResolveSteamMetadataId(configuration);

        if (string.IsNullOrWhiteSpace(steamId) && configuration.Artwork.SteamGridDbGameId is null)
            return result;

        JsonDocument? steamDocument = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(steamId)) steamDocument = await GetSteamMetadataAsync(
                steamId,
                portablePaths,
                cancellationToken);

            if (steamDocument is not null)
            {
                ApplySteamTextMetadata(configuration, steamId, steamDocument);
                if (configuration.Artwork.DownloadMissingArtwork)
                {
                    await DownloadSteamScreenshotsAsync(
                        gameFolder,
                        steamId,
                        steamDocument,
                        result,
                        cancellationToken);
                    await DownloadSteamTrailerAsync(
                        gameFolder,
                        steamId,
                        configuration,
                        steamDocument,
                        result,
                        cancellationToken);
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            result.Warnings.Add(
                $"Steam metadata could not be refreshed: {ex.Message}");
        }
        finally
        {
            steamDocument?.Dispose();
        }

        if (!configuration.Artwork.DownloadMissingArtwork)
            return result;

        if (!string.IsNullOrWhiteSpace(steamId))
            await DownloadSteamArtworkAsync(gameFolder, configuration, steamId, result, cancellationToken);

        var settings = await MetadataProviderSettings.LoadAsync(
            portablePaths,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) &&
            HasMissingArtwork(gameFolder, configuration))
        {
            await DownloadSteamGridDbArtworkAsync(
                gameFolder,
                configuration,
                steamId,
                settings.SteamGridDbApiKey,
                result,
                cancellationToken);
        }

        return result;
    }

    private async Task<JsonDocument?> GetSteamMetadataAsync(
        string steamId,
        PortablePaths paths,
        CancellationToken cancellationToken)
    {
        var cacheFolder = Path.Combine(paths.Cache, "Metadata", "Steam");
        Directory.CreateDirectory(cacheFolder);

        var cachePath = Path.Combine(cacheFolder, steamId + ".json");
        var cached = File.Exists(cachePath);
        var fresh = cached &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) <
                    CacheLifetime;

        if (fresh)
        {
            return JsonDocument.Parse(
                await File.ReadAllTextAsync(cachePath, cancellationToken));
        }

        try
        {
            var url =
                "https://store.steampowered.com/api/appdetails?appids=" +
                Uri.EscapeDataString(steamId) +
                "&l=english&cc=US";

            using var response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);

            await File.WriteAllTextAsync(
                cachePath,
                json,
                cancellationToken);

            return JsonDocument.Parse(json);
        }
        catch when (cached)
        {
            return JsonDocument.Parse(
                await File.ReadAllTextAsync(cachePath, cancellationToken));
        }
    }

    private static void ApplySteamTextMetadata(
        GameConfiguration configuration,
        string steamId,
        JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty(steamId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data))
        {
            return;
        }

        configuration.Game.Description = Fill(
            configuration.Game.Description,
            ReadString(data, "short_description"));

        configuration.Game.Developer = Fill(
            configuration.Game.Developer,
            ReadFirstString(data, "developers"));

        configuration.Game.Publisher = Fill(
            configuration.Game.Publisher,
            ReadFirstString(data, "publishers"));

        configuration.Game.Genre = Fill(
            configuration.Game.Genre,
            ReadDescriptionList(data, "genres"));

        if (data.TryGetProperty("release_date", out var releaseDate))
        {
            configuration.Game.ReleaseDate = Fill(
                configuration.Game.ReleaseDate,
                ReadString(releaseDate, "date"));
        }

        if (configuration.Game.GamepadSupport is GamepadSupport.Unknown)
            configuration.Game.GamepadSupport = ReadGamepadSupport(data);
    }

    private static GamepadSupport ReadGamepadSupport(JsonElement data)
    {
        if (!data.TryGetProperty("categories", out var categories) ||
            categories.ValueKind is not JsonValueKind.Array)
        {
            return GamepadSupport.Unknown;
        }

        var descriptions = categories.EnumerateArray()
            .Select(category => ReadString(category, "description"))
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        if (descriptions.Any(description =>
                description!.Contains("full controller support", StringComparison.OrdinalIgnoreCase)))
        {
            return GamepadSupport.Full;
        }

        if (descriptions.Any(description =>
                description!.Contains("partial controller support", StringComparison.OrdinalIgnoreCase)))
        {
            return GamepadSupport.Partial;
        }

        return GamepadSupport.Unknown;
    }

    private async Task DownloadSteamArtworkAsync(
        string gameFolder,
        GameConfiguration configuration,
        string steamId,
        GameMetadataEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        var baseUrl =
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamId}/";

        await DownloadFirstAvailableAsync(
            gameFolder,
            configuration.Artwork.Cover,
            [baseUrl + "library_600x900_2x.jpg", baseUrl + "library_600x900.jpg"],
            "Steam cover",
            result,
            cancellationToken);

        await DownloadFirstAvailableAsync(
            gameFolder,
            configuration.Artwork.Hero,
            [baseUrl + "library_hero.jpg"],
            "Steam hero",
            result,
            cancellationToken);

        await DownloadFirstAvailableAsync(
            gameFolder,
            configuration.Artwork.Logo,
            [baseUrl + "logo.png"],
            "Steam logo",
            result,
            cancellationToken);
    }

    private async Task DownloadSteamScreenshotsAsync(
        string gameFolder,
        string steamId,
        JsonDocument document,
        GameMetadataEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        if (!document.RootElement.TryGetProperty(steamId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("screenshots", out var screenshots) ||
            screenshots.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var folder = Path.Combine(gameFolder, "Artwork", "Screenshots");
        var urls = screenshots.EnumerateArray()
            .Select(item => ReadString(item, "path_full"))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Take(8)
            .ToArray();

        for (var index = 0; index < urls.Length; index++)
        {
            var destination = Path.Combine(folder, $"{index + 1:00}.jpg");
            if (File.Exists(destination))
                continue;

            try
            {
                await DownloadImageAsync(urls[index]!, destination, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                result.Warnings.Add($"Steam screenshot {index + 1} could not be downloaded: {ex.Message}");
            }
        }
    }

    private async Task DownloadSteamTrailerAsync(
        string gameFolder,
        string steamId,
        GameConfiguration configuration,
        JsonDocument document,
        GameMetadataEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        var mediaFolder = Path.Combine(gameFolder, "Media");
        if (File.Exists(Path.Combine(mediaFolder, "Snap.mp4")) ||
            File.Exists(Path.Combine(mediaFolder, "Trailer.mp4")) ||
            File.Exists(Path.Combine(mediaFolder, "SteamTrailer.mp4")) ||
            File.Exists(Path.Combine(mediaFolder, "SteamTrailer.webm")))
        {
            return;
        }

        if (!document.RootElement.TryGetProperty(steamId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("movies", out var movies) ||
            movies.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var candidates = new List<string>();
        CollectMovieUrls(movies, candidates);

        if (string.IsNullOrWhiteSpace(configuration.Artwork.TrailerUrl))
        {
            configuration.Artwork.TrailerUrl = candidates.FirstOrDefault(value =>
                value.Contains("hls_264", StringComparison.OrdinalIgnoreCase)) ??
                candidates.FirstOrDefault(value =>
                    value.Contains("dash_h264", StringComparison.OrdinalIgnoreCase)) ??
                candidates.FirstOrDefault() ?? "";
        }

        foreach (var url in candidates
                     .Where(value => !value.Contains(".mpd", StringComparison.OrdinalIgnoreCase) &&
                                     !value.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(value => value.Contains("max", StringComparison.OrdinalIgnoreCase))
                     .ThenBy(value => value.Contains("microtrailer", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var extension = new Uri(url).AbsolutePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                    ? ".webm"
                    : ".mp4";
                var destination = Path.Combine(mediaFolder, "SteamTrailer" + extension);
                if (await DownloadMediaAsync(url, destination, steamId, cancellationToken))
                    return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                result.Warnings.Add($"Steam trailer candidate failed: {ex.Message}");
            }
        }
    }

    private static void CollectMovieUrls(JsonElement element, List<string> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectMovieUrls(property.Value, results);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectMovieUrls(item, results);
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                    (uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                     uri.AbsolutePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                     uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
                     uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(value!);
                }
                break;
        }
    }

    private async Task<bool> DownloadMediaAsync(
        string url,
        string destination,
        string steamId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri($"https://store.steampowered.com/app/{steamId}/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 CartLaunchCompanion/2.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/mp4"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/webm"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".download";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                             temporaryPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            if (new FileInfo(temporaryPath).Length < 65536) return false;
            File.Move(temporaryPath, destination, true);
            return true;
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private async Task DownloadSteamGridDbArtworkAsync(
        string gameFolder,
        GameConfiguration configuration,
        string steamId,
        string apiKey,
        GameMetadataEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var gameId = configuration.Artwork.SteamGridDbGameId ?? await ResolveSteamGridDbGameIdAsync(
                steamId, apiKey, cancellationToken);

            if (gameId is null)
                return;
            configuration.Artwork.SteamGridDbGameId = gameId;

            await DownloadSteamGridDbAssetAsync(
                gameFolder,
                configuration.Artwork.Cover,
                $"grids/game/{gameId}?dimensions=600x900&types=static",
                apiKey,
                cancellationToken);

            await DownloadSteamGridDbAssetAsync(
                gameFolder,
                configuration.Artwork.Hero,
                $"heroes/game/{gameId}?dimensions=3840x1240,1920x620&types=static",
                apiKey,
                cancellationToken);

            await DownloadSteamGridDbAssetAsync(
                gameFolder,
                configuration.Artwork.Logo,
                $"logos/game/{gameId}?types=static",
                apiKey,
                cancellationToken);

            await DownloadSteamGridDbAssetAsync(
                gameFolder,
                configuration.Artwork.Icon,
                $"icons/game/{gameId}",
                apiKey,
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && !cancellationToken.IsCancellationRequested)
        {
            result.Warnings.Add("The saved SteamGridDB API key was rejected. Update or remove it in Configurator Settings.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            result.Warnings.Add(
                $"SteamGridDB artwork could not be refreshed: {ex.Message}");
        }
    }

    private async Task<long?> ResolveSteamGridDbGameIdAsync(
        string steamId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var document = await GetSteamGridDbJsonAsync(
            $"games/steam/{steamId}",
            apiKey,
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data))
            return null;

        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("id", out var id))
        {
            return id.GetInt64();
        }

        if (data.ValueKind == JsonValueKind.Array)
        {
            var first = data.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("id", out id))
            {
                return id.GetInt64();
            }
        }

        return null;
    }

    private async Task DownloadSteamGridDbAssetAsync(
        string gameFolder,
        string relativePath,
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var destination = ResolveMissingDestination(gameFolder, relativePath);
        if (destination is null)
            return;

        using var document = await GetSteamGridDbJsonAsync(
            endpoint,
            apiKey,
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var url = data
            .EnumerateArray()
            .Select(item => ReadString(item, "url"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(url))
            await DownloadImageAsync(url, destination, cancellationToken);
    }

    private async Task<JsonDocument> GetSteamGridDbJsonAsync(
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.steamgriddb.com/api/v2/" + endpoint);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    private async Task DownloadFirstAvailableAsync(
        string gameFolder,
        string relativePath,
        IReadOnlyList<string> urls,
        string label,
        GameMetadataEnrichmentResult result,
        CancellationToken cancellationToken)
    {
        var destination = ResolveMissingDestination(gameFolder, relativePath);
        if (destination is null)
            return;

        foreach (var url in urls)
        {
            try
            {
                if (await DownloadImageAsync(url, destination, cancellationToken))
                    return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                result.Warnings.Add($"{label} could not be downloaded: {ex.Message}");
                return;
            }
        }
    }

    private async Task<bool> DownloadImageAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".download";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                             temporaryPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            File.Move(temporaryPath, destination, true);
            return true;
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private string? ResolveMissingDestination(
        string gameFolder,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var destination = pathResolver.Resolve(gameFolder, relativePath);
        return File.Exists(destination) ? null : destination;
    }

    private bool HasMissingArtwork(
        string gameFolder,
        GameConfiguration configuration) =>
        ResolveMissingDestination(gameFolder, configuration.Artwork.Cover) is not null ||
        ResolveMissingDestination(gameFolder, configuration.Artwork.Hero) is not null ||
        ResolveMissingDestination(gameFolder, configuration.Artwork.Logo) is not null ||
        ResolveMissingDestination(gameFolder, configuration.Artwork.Icon) is not null;

    private static string ResolveSteamMetadataId(
        GameConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.Artwork.SteamMetadataId))
            return configuration.Artwork.SteamMetadataId.Trim();

        if (!string.IsNullOrWhiteSpace(configuration.Launch.Windows.SteamId))
            return configuration.Launch.Windows.SteamId.Trim();

        return configuration.Launch.Linux.SteamId.Trim();
    }

    private static string Fill(string destination, string? value) =>
        string.IsNullOrWhiteSpace(destination) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : destination;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadFirstString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static string? ReadDescriptionList(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var descriptions = values
            .EnumerateArray()
            .Select(value => ReadString(value, "description"))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(", ", descriptions);
    }
}
