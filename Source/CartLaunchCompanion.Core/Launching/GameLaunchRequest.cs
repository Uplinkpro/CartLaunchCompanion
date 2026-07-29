using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Launching;

public sealed record GameLaunchRequest(
    string GameName,
    string GameFolder,
    LaunchTargetSelection Target,
    BehaviorConfiguration Behavior);
