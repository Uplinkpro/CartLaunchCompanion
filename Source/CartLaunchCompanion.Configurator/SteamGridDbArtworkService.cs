using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Configurator;

public enum SteamGridDbAssetKind { Cover, Hero, Logo, Icon }

public sealed class SteamGridDbAsset : IDisposable
{
    public required long Id { get; init; }
    public required string Url { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Score { get; init; }
    public required string Style { get; init; }
    public required string Author { get; init; }
    public Bitmap? Preview { get; set; }
    public string Details => $"{Width} × {Height} · {Style} · score {Score}";
    public string Credit => string.IsNullOrWhiteSpace(Author) ? "SteamGridDB community" : $"by {Author}";
    public void Dispose() => Preview?.Dispose();
}

public sealed class SteamGridDbArtworkService(HttpClient httpClient)
{
    public async Task<IReadOnlyList<SteamGridDbAsset>> GetAssetsAsync(
        long gameId, SteamGridDbAssetKind kind, string apiKey, bool allowHumor = false,
        CancellationToken cancellationToken = default)
    {
        var endpoint = kind switch
        {
            SteamGridDbAssetKind.Cover => $"grids/game/{gameId}?dimensions=600x900&types=static",
            SteamGridDbAssetKind.Hero => $"heroes/game/{gameId}?dimensions=3840x1240,1920x620&types=static",
            SteamGridDbAssetKind.Logo => $"logos/game/{gameId}?types=static",
            _ => $"icons/game/{gameId}?types=static"
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

        var assets = data.EnumerateArray()
            .Where(item => !ReadBool(item, "nsfw") && !ReadBool(item, "epilepsy") && (allowHumor || !ReadBool(item, "humor")))
            .Select(item => new SteamGridDbAsset
            {
                Id = ReadLong(item, "id"), Url = Read(item, "url"),
                ThumbnailUrl = Read(item, "thumb", Read(item, "url")),
                Width = ReadInt(item, "width"), Height = ReadInt(item, "height"),
                Score = ReadInt(item, "score") + ReadInt(item, "upvotes") - ReadInt(item, "downvotes"),
                Style = Read(item, "style", "alternate"), Author = ReadAuthor(item)
            })
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
            .OrderByDescending(asset => Rank(asset, kind)).Take(30).ToArray();
        foreach (var asset in assets)
        {
            try
            {
                var bytes = await httpClient.GetByteArrayAsync(asset.ThumbnailUrl, cancellationToken);
                asset.Preview = new Bitmap(new MemoryStream(bytes));
            }
            catch { }
        }
        return assets;
    }

    public async Task DownloadAsync(SteamGridDbAsset asset, string destination, CancellationToken cancellationToken = default)
    {
        var bytes = await httpClient.GetByteArrayAsync(asset.Url, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".sgdb.tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        using (var bitmap = new Bitmap(temporary))
            if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0) throw new InvalidDataException("Artwork dimensions are invalid.");
        File.Move(temporary, destination, true);
    }

    private static int Rank(SteamGridDbAsset asset, SteamGridDbAssetKind kind) =>
        asset.Score * 100 + (asset.Style.Equals("official", StringComparison.OrdinalIgnoreCase) ? 10000 : 0) +
        Math.Min(5000, asset.Width * asset.Height / 1000);
    private static string Read(JsonElement item, string name, string fallback = "") => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static int ReadInt(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static long ReadLong(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static bool ReadBool(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static string ReadAuthor(JsonElement item) => item.TryGetProperty("author", out var author) ? Read(author, "name") : "";
}
