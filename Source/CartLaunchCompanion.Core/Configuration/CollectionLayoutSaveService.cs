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
        var normalized = placements.Select(item => item with
        {
            ConfigurationPath = Path.GetFullPath(item.ConfigurationPath),
            Shelf = item.Shelf.Trim()
        }).ToArray();

        if (normalized.Any(item => !item.ConfigurationPath.StartsWith(gamesRoot, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Every game configuration must be inside Cart/Games.");
        if (normalized.GroupBy(item => item.ConfigurationPath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException("A game cannot appear more than once in the collection layout.");

        var resolved = new List<(CollectionGamePlacementUpdate Placement, string GameId)>();
        foreach (var item in normalized)
        {
            var game = await GameConfigurationJson.LoadAsync(item.ConfigurationPath, cancellationToken);
            resolved.Add((item, GameIdentity.Resolve(game.Game)));
        }

        collection.Placements = resolved.Select(item => new CollectionGamePlacementConfiguration
        {
            GameId = item.GameId,
            Configuration = Path.GetRelativePath(root, item.Placement.ConfigurationPath).Replace('\\', '/'),
            Shelf = item.Placement.Shelf,
            Order = item.Placement.Order
        }).ToList();
        await CollectionConfigurationJson.SaveAsync(Path.Combine(root, "Config"), collection, cancellationToken);
    }
}
