using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Launching;

public interface ILaunchTargetSelector
{
    LaunchTargetSelection? Select(
        GameConfiguration configuration,
        PlatformKind currentPlatform);
}
