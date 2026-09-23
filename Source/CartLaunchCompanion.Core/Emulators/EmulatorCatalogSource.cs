namespace CartLaunchCompanion.Core.Emulators;

public interface IEmulatorCatalogSource
{
    Task<EmulatorCatalog> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Loads a local catalog. Missing or invalid catalogs are errors, not empty catalogs.</summary>
public sealed class EmulatorCatalogSource(string path) : IEmulatorCatalogSource
{
    public async Task<EmulatorCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return EmulatorManagementJson.ReadCatalog(json);
    }
}
