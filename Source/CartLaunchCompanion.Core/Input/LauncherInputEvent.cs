namespace CartLaunchCompanion.Core.Input;

public sealed record LauncherInputEvent(
    LauncherAction Action,
    InputDeviceKind Device,
    DateTimeOffset Timestamp);
