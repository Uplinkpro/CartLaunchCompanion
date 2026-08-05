using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Metadata;

public interface IGameMetadataService
{
    Task<GameMetadataEnrichmentResult> EnrichAsync(
        string gameFolder,
        GameConfiguration configuration,
        PortablePaths portablePaths,
        CancellationToken cancellationToken = default);
}

public sealed class GameMetadataEnrichmentResult
{
    public List<string> Warnings { get; } = [];
}
