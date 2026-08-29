using Avalonia.Input;
using CartLaunchCompanion.Core.Input;

namespace CartLaunchCompanion.Desktop.Input;

public static class AvaloniaInputMapper
{
    public static LauncherInputEvent Map(KeyEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var device = args.KeyDeviceType == KeyDeviceType.Remote
            ? InputDeviceKind.Remote
            : InputDeviceKind.Keyboard;

        var action = args.Key switch
        {
            Key.Left => LauncherAction.NavigateLeft,
            Key.Right => LauncherAction.NavigateRight,
            Key.Up => LauncherAction.NavigateUp,
            Key.Down => LauncherAction.NavigateDown,

            Key.Enter => LauncherAction.Confirm,
            Key.Space when device is not InputDeviceKind.Keyboard =>
                LauncherAction.Confirm,

            Key.Escape => LauncherAction.Back,
            Key.Back => LauncherAction.Back,
            Key.BrowserBack => LauncherAction.Back,

            Key.X => LauncherAction.Trailer,
            Key.Space => LauncherAction.Trailer,
            Key.E => LauncherAction.Options,

            _ => LauncherAction.None
        };

        return new LauncherInputEvent(
            action,
            device,
            DateTimeOffset.UtcNow);
    }
}
