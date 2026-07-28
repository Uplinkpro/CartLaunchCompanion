using CartLaunchCompanion.Models;

namespace CartLaunchCompanion.Services;

public sealed class ArtworkService
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task EnsureArtworkAsync(
        GameDefinition game,
        CancellationToken token = default)
    {
        var steamMetadataId = game.EffectiveSteamMetadataId;
        if (string.IsNullOrWhiteSpace(steamMetadataId))
            return;

        await EnsureCoverAsync(game, steamMetadataId, token);
        await EnsureHeaderAsync(game, steamMetadataId, token);
    }

    private async Task EnsureCoverAsync(
        GameDefinition game,
        string steamMetadataId,
        CancellationToken token)
    {
        var target = Path.Combine(game.FolderPath, "Cover.jpg");

        var sources = string.IsNullOrWhiteSpace(game.CoverUrl)
            ? new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900_2x.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900_2x.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900.jpg"
            }
            : new[] { game.CoverUrl };

        if (await DownloadFirstAsync(sources, target, token))
            game.CoverPath = target;
    }

    private async Task EnsureHeaderAsync(
        GameDefinition game,
        string steamMetadataId,
        CancellationToken token)
    {
        var target = Path.Combine(game.FolderPath, "Header.jpg");

        // Steam's library hero is background artwork intended for wide,
        // cinematic library headers. Its usual 1920x620 aspect ratio scales
        // directly to Cart Launch Companion's 3840x1240 header target.
        //
        // A configured HeaderUrl remains the highest-priority source.
        // Steam's smaller storefront header is retained only as a fallback
        // for games that do not provide library hero artwork.
        var sources = string.IsNullOrWhiteSpace(game.HeaderUrl)
            ? new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/library_hero.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{steamMetadataId}/library_hero.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/header.jpg",
                $"https://cdn.akamai.steamstatic.com/steam/apps/{steamMetadataId}/header.jpg"
            }
            : new[] { game.HeaderUrl };

        if (await DownloadFirstAsync(sources, target, token))
            game.HeaderPath = target;
    }

    private async Task<bool> DownloadFirstAsync(
        IEnumerable<string> sources,
        string target,
        CancellationToken token)
    {
        var targetDirectory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        foreach (var source in sources.Where(
                     value => !string.IsNullOrWhiteSpace(value)))
        {
            var temporaryTarget = target + ".download";

            try
            {
                using var response = await _http.GetAsync(
                    source,
                    HttpCompletionOption.ResponseHeadersRead,
                    token);

                if (!response.IsSuccessStatusCode)
                    continue;

                var contentType =
                    response.Content.Headers.ContentType?.MediaType ?? "";

                if (!contentType.StartsWith(
                        "image/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await using (var input =
                    await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(
                    temporaryTarget,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true))
                {
                    await input.CopyToAsync(output, token);
                    await output.FlushAsync(token);
                }

                var fileInfo = new FileInfo(temporaryTarget);
                if (!fileInfo.Exists || fileInfo.Length < 4096)
                {
                    File.Delete(temporaryTarget);
                    continue;
                }

                File.Move(temporaryTarget, target, overwrite: true);
                return true;
            }
            catch when (!token.IsCancellationRequested)
            {
                // Continue to the next Steam CDN/source candidate.
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryTarget))
                        File.Delete(temporaryTarget);
                }
                catch
                {
                    // Cleanup failure must not stop the remaining sources.
                }
            }
        }

        return false;
    }
}
