using CartLaunchCompanion.Models;

namespace CartLaunchCompanion.Services;

public sealed class ArtworkService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task EnsureArtworkAsync(GameDefinition game, CancellationToken token = default)
    {
        var steamMetadataId = game.EffectiveSteamMetadataId;
        if (string.IsNullOrWhiteSpace(steamMetadataId)) return;

        await EnsureCoverAsync(game, steamMetadataId, token);
        await EnsureHeaderAsync(game, steamMetadataId, token);
    }

    private async Task EnsureCoverAsync(GameDefinition game, string steamMetadataId, CancellationToken token)
    {
        var target = Path.Combine(game.FolderPath, "Cover.jpg");
        var sources = string.IsNullOrWhiteSpace(game.CoverUrl)
            ? new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900_2x.jpg",
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamMetadataId}/library_600x900.jpg"
            }
            : new[] { game.CoverUrl };

        if (await DownloadFirstAsync(sources, target, token)) game.CoverPath = target;
    }

    private async Task EnsureHeaderAsync(GameDefinition game, string steamMetadataId, CancellationToken token)
    {
        var target = Path.Combine(game.FolderPath, "Header.jpg");
        var sources = string.IsNullOrWhiteSpace(game.HeaderUrl)
            ? new[] { $"https://cdn.akamai.steamstatic.com/steam/apps/{steamMetadataId}/header.jpg" }
            : new[] { game.HeaderUrl };

        if (await DownloadFirstAsync(sources, target, token)) game.HeaderPath = target;
    }

    private async Task<bool> DownloadFirstAsync(IEnumerable<string> sources, string target, CancellationToken token)
    {
        foreach (var source in sources.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                using var response = await _http.GetAsync(source, token);
                if (!response.IsSuccessStatusCode) continue;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(target);
                await input.CopyToAsync(output, token);
                return true;
            }
            catch when (!token.IsCancellationRequested) { }
        }
        return false;
    }
}
