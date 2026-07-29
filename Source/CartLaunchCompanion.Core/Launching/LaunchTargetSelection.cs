using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Launching;

public sealed record LaunchTargetSelection(
    PlatformKind Platform,
    LauncherKind Launcher,
    bool Enabled,
    string SteamId,
    string ApplicationId,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string Uri,
    string CompatibilityTool,
    string WinePrefix);
