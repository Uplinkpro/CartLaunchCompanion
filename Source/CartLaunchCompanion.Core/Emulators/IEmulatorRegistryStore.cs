using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public interface IEmulatorRegistryStore
{
    Task<EmulatorRegistry> LoadAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(EmulatorInstallation installation, CancellationToken cancellationToken = default);
    Task RemoveAsync(string emulatorId, PlatformKind platform, CancellationToken cancellationToken = default);
}
