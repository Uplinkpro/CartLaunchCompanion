using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Library;

public interface IGameLibraryService
{
    Task<GameLibraryLoadResult> LoadAsync(
        PortablePaths paths,
        PlatformKind platform,
        CancellationToken cancellationToken = default);
}
