using System.Text.Json;

namespace CartLaunchCompanion.Core.Configuration;

public sealed class CollectionConfiguration
{
    public int FormatVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AccentColor { get; set; } = "#C08AFF";
    public string DefaultShelf { get; set; } = "Library";
    public List<CollectionShelfConfiguration> Shelves { get; set; } = [];
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
}
