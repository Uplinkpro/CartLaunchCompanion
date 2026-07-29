using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Core.Platform;

public sealed class RuntimePlatformService : IPlatformService
{
    public PlatformKind Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? PlatformKind.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? PlatformKind.Linux
                : PlatformKind.Unsupported;
}
