using System.Text.Json;

namespace CartLaunchCompanion.Core.Configuration;

public sealed record CollectionGamePlacementUpdate(
    string ConfigurationPath,
    string Shelf,
    int Order);

public sealed class CollectionLayoutSaveService
{
    public async Task SaveAsync(
        string cartRoot,
        CollectionConfiguration collection,
        IReadOnlyCollection<CollectionGamePlacementUpdate> placements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartRoot);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(placements);

        var root = Path.GetFullPath(cartRoot);
        var gamesRoot = Path.GetFullPath(Path.Combine(root, "Games")) + Path.DirectorySeparatorChar;
        var collectionPath = Path.GetFullPath(Path.Combine(root, "Config", "collection.json"));
        var normalized = placements.Select(item => item with
        {
            ConfigurationPath = Path.GetFullPath(item.ConfigurationPath),
            Shelf = item.Shelf.Trim()
        }).ToArray();

        if (normalized.Any(item => !item.ConfigurationPath.StartsWith(gamesRoot, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Every game configuration must be inside Cart/Games.");
        if (normalized.GroupBy(item => item.ConfigurationPath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException("A game cannot appear more than once in the collection layout.");

        var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [collectionPath] = JsonSerializer.SerializeToUtf8Bytes(collection, GameConfigurationJson.Options)
        };
        foreach (var placement in normalized)
        {
            var game = await GameConfigurationJson.LoadAsync(placement.ConfigurationPath, cancellationToken);
            game.Collection.Shelf = placement.Shelf;
            game.Collection.Order = placement.Order;
            payloads[placement.ConfigurationPath] = JsonSerializer.SerializeToUtf8Bytes(game, GameConfigurationJson.Options);
        }

        var originals = payloads.Keys.ToDictionary(
            path => path,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null,
            StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        try
        {
            foreach (var (path, bytes) in payloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporaryPath = path + ".layout.tmp";
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                File.Move(temporaryPath, path, overwrite: true);
                written.Add(path);
            }
        }
        catch
        {
            foreach (var path in written.AsEnumerable().Reverse())
            {
                if (originals[path] is { } original)
                    await File.WriteAllBytesAsync(path, original, CancellationToken.None);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            throw;
        }
        finally
        {
            foreach (var path in payloads.Keys)
            {
                var temporaryPath = path + ".layout.tmp";
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
