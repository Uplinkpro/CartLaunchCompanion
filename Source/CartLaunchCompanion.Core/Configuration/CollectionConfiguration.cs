using System.Text.Json;

namespace CartLaunchCompanion.Core.Configuration;

public sealed class CollectionConfiguration
{
    public int FormatVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Logo { get; set; } = "";
    public string AccentColor { get; set; } = "#C08AFF";
    public string DefaultShelf { get; set; } = "";
    public string ArtworkStyle { get; set; } = "official";
    public bool AllowHumorArtwork { get; set; }
    public List<CollectionShelfConfiguration> Shelves { get; set; } = [];
    public List<CollectionGamePlacementConfiguration> Placements { get; set; } = [];
}

public sealed class CollectionGamePlacementConfiguration
{
    public string GameId { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string Shelf { get; set; } = "";
    public int Order { get; set; }
}

public sealed class CollectionShelfConfiguration
{
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

public static class CollectionConfigurationJson
{
    public static async Task<CollectionConfiguration> LoadAsync(
        string configFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFolder);

        var path = Path.Combine(configFolder, "collection.json");
        if (!File.Exists(path))
            return new CollectionConfiguration();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CollectionConfiguration>(
                   stream,
                   GameConfigurationJson.Options,
                   cancellationToken)
               ?? throw new InvalidDataException(
                   $"The collection configuration is empty: {path}");
    }

    public static async Task SaveAsync(
        string configFolder,
        CollectionConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFolder);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(configFolder);
        var path = Path.Combine(configFolder, "collection.json");
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, GameConfigurationJson.Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
