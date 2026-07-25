using System.Text.Json;
using CartLaunchCompanion.Models;

namespace CartLaunchCompanion.Services;

public sealed class GameLibraryService
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<GameDefinition>> LoadAsync(string root)
    {
        var gamesRoot = Path.Combine(root, "Games");
        if (!Directory.Exists(gamesRoot)) return [];

        var games = new List<GameDefinition>();
        foreach (var file in Directory.EnumerateFiles(gamesRoot, "Game.json", SearchOption.AllDirectories))
        {
            try
            {
                var jsonText = await File.ReadAllTextAsync(file);
                var game = JsonSerializer.Deserialize<GameDefinition>(jsonText, _json);
                if (game is null) continue;

                // Read YouTube fields explicitly as well as through normal model binding.
                // This accepts common spellings used by hand-edited cartridge files and
                // prevents a storefront trailer from masking an explicit YouTube link.
                using (var document = JsonDocument.Parse(jsonText))
                {
                    var rootElement = document.RootElement;
                    game.YouTubeUrl = FirstJsonString(rootElement,
                        "YouTubeUrl", "YoutubeUrl", "YouTubeURL", "youtubeUrl",
                        "youtube_url", "YouTube", "Youtube", "TrailerYouTubeUrl");

                    if (string.IsNullOrWhiteSpace(game.YouTubeUrl))
                    {
                        var legacyTrailer = FirstJsonString(rootElement, "TrailerUrl", "VideoUrl");
                        if (LooksLikeYouTube(legacyTrailer)) game.YouTubeUrl = legacyTrailer;
                    }
                }
                game.FolderPath = Path.GetDirectoryName(file)!;
                game.CoverPath = FindArtwork(game.FolderPath, "Cover");
                game.HeaderPath = FindArtwork(game.FolderPath, "Header");
                games.Add(game);
            }
            catch { /* Invalid entries are skipped so one bad cartridge does not stop the library. */ }
        }
        return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string FirstJsonString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString()?.Trim() ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool LooksLikeYouTube(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindArtwork(string folder, string baseName)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var path = Path.Combine(folder, baseName + extension);
            if (File.Exists(path)) return path;
        }
        return "";
    }
}
