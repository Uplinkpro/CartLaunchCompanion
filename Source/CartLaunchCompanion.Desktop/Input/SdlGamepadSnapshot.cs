namespace CartLaunchCompanion.Desktop.Input;

internal readonly record struct SdlGamepadSnapshot(
    bool Confirm,
    bool Back,
    bool Trailer,
    bool Up,
    bool Down,
    bool Left,
    bool Right);
