using System.Text.Json;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record EmulatorLibraryEntry(
    string EmulatorId,
    EmulatorDefinition? Definition,
    IReadOnlyList<EmulatorInstallation> Installations);

public sealed record EmulatorLibrarySnapshot(
    IReadOnlyList<EmulatorLibraryEntry> Entries,
    bool CatalogUnavailable,
    bool RegistryUnavailable);

public interface IEmulatorLibraryService
{
    Task<EmulatorLibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Read-only catalog/registry join. Recorded installations are not a launch-readiness check.</summary>
public sealed class EmulatorLibraryService(
    IEmulatorCatalogSource catalogSource,
    IEmulatorRegistryStore registryStore) : IEmulatorLibraryService
{
    public async Task<EmulatorLibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        EmulatorCatalog? catalog = null;
        EmulatorRegistry? registry = null;
        try
        {
            catalog = await catalogSource.LoadAsync(cancellationToken);
            EmulatorManagementJson.Validate(catalog);
        }
        catch (Exception error) when (IsDataError(error)) { catalog = null; }

        try
        {
            registry = await registryStore.LoadAsync(cancellationToken);
            EmulatorManagementJson.Validate(registry);
        }
        catch (Exception error) when (IsDataError(error)) { registry = null; }

        cancellationToken.ThrowIfCancellationRequested();
        var definitions = catalog?.Emulators.ToDictionary(item => item.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, EmulatorDefinition>(StringComparer.Ordinal);
        var installations = (registry?.Installations ?? [])
            .GroupBy(item => item.EmulatorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<EmulatorInstallation>)group.OrderBy(item => item.Platform).ToArray(),
                StringComparer.Ordinal);
        var ids = definitions.Keys.Concat(installations.Keys).Distinct(StringComparer.Ordinal);
        var entries = ids.Select(id => new EmulatorLibraryEntry(
                id, definitions.GetValueOrDefault(id), installations.GetValueOrDefault(id) ?? []))
            .OrderBy(item => item.Definition?.DisplayName ?? item.EmulatorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EmulatorId, StringComparer.Ordinal)
            .ToArray();
        return new(entries, catalog is null, registry is null);
    }

    private static bool IsDataError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
}
