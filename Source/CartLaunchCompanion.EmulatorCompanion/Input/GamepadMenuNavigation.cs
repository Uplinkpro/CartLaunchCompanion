using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CartLaunchCompanion.EmulatorCompanion.Input;

/// <summary>Provides one shared SDL gamepad navigator for every Companion window.</summary>
internal static class GamepadMenuNavigation
{
    private const int StickDeadZone = 18000;
    private static readonly List<Registration> Windows = [];
    private static readonly StateMapper Mapper = new();
    private static DispatcherTimer? _timer;
    private static IntPtr _gamepad;
    private static bool _initialized;
    private static DateTimeOffset _nextScan;
    private static string _status = "Controller navigation is starting…";

    public static event EventHandler<string>? StatusChanged;
    public static string Status => _status;

    public static void Attach(Window window, Func<bool>? suppress = null, Control? initialFocus = null,
        Action? backAction = null)
    {
        var registration = new Registration(window, suppress ?? (() => false), initialFocus, backAction);
        window.Opened += (_, _) => Open(registration);
        window.Closed += (_, _) => Close(registration);
    }

    private static void Open(Registration registration)
    {
        Windows.RemoveAll(item => ReferenceEquals(item.Window, registration.Window));
        Windows.Add(registration);
        EnsureStarted();
        Dispatcher.UIThread.Post(() => FocusFirst(registration), DispatcherPriority.Input);
    }

    private static void Close(Registration registration)
    {
        Windows.RemoveAll(item => ReferenceEquals(item.Window, registration.Window));
        if (Windows.Count != 0) return;
        _timer?.Stop();
        _timer = null;
        CloseGamepad();
        if (_initialized) Native.SDL_QuitSubSystem(Native.InitGamepad);
        _initialized = false;
    }

    private static void EnsureStarted()
    {
        if (_timer is not null) return;
        try
        {
            if (!Native.SDL_InitSubSystem(Native.InitGamepad)) return;
            _initialized = true;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += Poll;
            _timer.Start();
            Poll(null, EventArgs.Empty);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"Controller menu navigation unavailable: {error.Message}");
        }
    }

    private static void Poll(object? sender, EventArgs args)
    {
        try
        {
            Native.SDL_PumpEvents();
            Native.SDL_UpdateGamepads();
            var now = DateTimeOffset.UtcNow;
            if (_gamepad != IntPtr.Zero && !Native.SDL_GamepadConnected(_gamepad)) CloseGamepad();
            if (_gamepad == IntPtr.Zero && now >= _nextScan)
            {
                OpenFirstGamepad();
                _nextScan = now.AddMilliseconds(500);
            }
            if (_gamepad == IntPtr.Zero) return;
            var action = Mapper.Map(
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.South),
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.East),
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.DpadUp),
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.DpadDown),
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.DpadLeft),
                Native.SDL_GetGamepadButton(_gamepad, SdlButton.DpadRight),
                Native.SDL_GetGamepadAxis(_gamepad, SdlAxis.LeftX),
                Native.SDL_GetGamepadAxis(_gamepad, SdlAxis.LeftY), now);
            if (action is not MenuAction.None)
            {
                Report("Controller input: " + action);
                Dispatch(action);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Controller menu polling failed: {error.Message}");
            CloseGamepad();
        }
    }

    private static void Dispatch(MenuAction action)
    {
        // The topmost registered visible window is authoritative. Avalonia's
        // IsActive can briefly remain false for extended-chrome windows and
        // owned dialogs, which previously caused valid controller input to be
        // discarded before it reached focus navigation.
        var current = Windows.LastOrDefault(item => item.Window.IsVisible);
        if (current is null || current.Suppress()) return;

        // ComboBox choices are presented in a popup outside the owning
        // window's visual tree. Keep controller input in the open dropdown
        // instead of allowing focus to move to the next dialog button.
        var openCombo = current.Window.GetVisualDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(combo => combo.IsDropDownOpen && combo.IsEnabled);
        if (openCombo is not null)
        {
            if (action is MenuAction.Confirm or MenuAction.Back)
            {
                openCombo.IsDropDownOpen = false;
                openCombo.Focus(NavigationMethod.Directional);
                return;
            }
            if (action is MenuAction.Up or MenuAction.Down or MenuAction.Left or MenuAction.Right)
            {
                var delta = action is MenuAction.Up or MenuAction.Left ? -1 : 1;
                openCombo.SelectedIndex = Math.Clamp(
                    openCombo.SelectedIndex + delta,
                    0,
                    Math.Max(0, openCombo.ItemCount - 1));
                return;
            }
        }
        if (action is MenuAction.Back)
        {
            if (current.Window.Owner is not null) current.Window.Close();
            else
            {
                current.NavigationTarget = current.InitialFocus;
                if (current.InitialFocus is not null)
                    current.Window.FocusManager?.Focus(current.InitialFocus, NavigationMethod.Directional);
                current.BackAction?.Invoke();
            }
            return;
        }
        var manager = current.Window.FocusManager;
        var focused = current.NavigationTarget ?? manager?.GetFocusedElement() ?? current.InitialFocus;
        if (focused is null) focused = FindFocusableControls(current.Window).FirstOrDefault();
        if (focused is null) return;
        if (action is MenuAction.Confirm)
        {
            Activate(focused);
            return;
        }

        if (FindAncestor<ListBox>(focused) is { } list && action is MenuAction.Up or MenuAction.Down)
        {
            var delta = action is MenuAction.Up ? -1 : 1;
            var next = Math.Clamp(list.SelectedIndex + delta, 0, Math.Max(0, list.ItemCount - 1));
            list.SelectedIndex = next;
            if (list.SelectedItem is { } selected) list.ScrollIntoView(selected);
            list.Focus(NavigationMethod.Directional);
            return;
        }

        if (FindAncestor<ComboBox>(focused) is { IsDropDownOpen: false } combo &&
            action is MenuAction.Left or MenuAction.Right)
        {
            var delta = action is MenuAction.Left ? -1 : 1;
            combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, Math.Max(0, combo.ItemCount - 1));
            return;
        }

        current.NavigationTarget = MoveFocusDirectly(current.Window, focused, action);
    }

    private static Control? MoveFocusDirectly(Window window, IInputElement focused, MenuAction action)
    {
        var controls = FindFocusableControls(window);
        if (controls.Count == 0) return null;
        var current = controls.FindIndex(control =>
            ReferenceEquals(control, focused) ||
            (focused is Visual visual && visual.GetVisualAncestors().Contains(control)));
        if (current < 0) current = 0;
        var step = action is MenuAction.Up or MenuAction.Left ? -1 : 1;
        var next = (current + step + controls.Count) % controls.Count;
        window.FocusManager?.Focus(controls[next], NavigationMethod.Directional);
        controls[next].BringIntoView();
        return controls[next];
    }

    private static List<Control> FindFocusableControls(Window window) =>
        window.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.IsVisible && control.IsEffectivelyEnabled &&
                control is Button or ComboBox or RadioButton or CheckBox or ListBox)
            .ToList();

    private static void Activate(IInputElement focused)
    {
        // RadioButton and CheckBox derive from Button. Handle them first so
        // controller confirmation changes their checked state instead of only
        // raising a generic click event.
        if (FindAncestor<RadioButton>(focused) is { IsEnabled: true } radio)
            radio.IsChecked = true;
        else if (FindAncestor<CheckBox>(focused) is { IsEnabled: true } check)
            check.IsChecked = check.IsChecked != true;
        else if (FindAncestor<ComboBox>(focused) is { IsEnabled: true } combo)
            combo.IsDropDownOpen = !combo.IsDropDownOpen;
        else if (FindAncestor<Button>(focused) is { IsEnabled: true } button)
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static T? FindAncestor<T>(IInputElement element) where T : Visual
    {
        if (element is T match) return match;
        return (element as Visual)?.GetVisualAncestors().OfType<T>().FirstOrDefault();
    }

    private static void FocusFirst(Registration registration)
    {
        if (!registration.Window.IsVisible) return;
        if (registration.InitialFocus is { IsVisible: true, IsEnabled: true } preferred)
        {
            registration.NavigationTarget = preferred;
            preferred.Focus(NavigationMethod.Directional);
            return;
        }
        var first = FindFocusableControls(registration.Window).FirstOrDefault();
        if (first is not null)
        {
            registration.NavigationTarget = first;
            registration.Window.FocusManager?.Focus(first, NavigationMethod.Directional);
        }
    }

    private static void OpenFirstGamepad()
    {
        var devices = Native.SDL_GetGamepads(out var count);
        try
        {
            if (devices == IntPtr.Zero || count == 0)
            {
                if (TryRegisterXboxStyleFallback())
                {
                    if (devices != IntPtr.Zero) Native.SDL_free(devices);
                    devices = Native.SDL_GetGamepads(out count);
                }
                else
                {
                    Report("Controller navigation: no compatible controller detected");
                }
            }
            for (var index = 0; index < count; index++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, index * sizeof(uint)));
                if ((_gamepad = Native.SDL_OpenGamepad(id)) == IntPtr.Zero) continue;
                Report("Controller navigation: " + Native.String(Native.SDL_GetGamepadName(_gamepad), "Gamepad") + " connected");
                break;
            }
            Mapper.Reset();
        }
        finally { if (devices != IntPtr.Zero) Native.SDL_free(devices); }
    }

    private static bool TryRegisterXboxStyleFallback()
    {
        var devices = Native.SDL_GetJoysticks(out var count);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, index * sizeof(uint)));
                if (Native.SDL_IsGamepad(id)) continue;
                var name = Native.String(Native.SDL_GetJoystickNameForID(id), "Generic controller").Trim('(', ')');
                if (!LooksLikeGamepad(name)) continue;
                var joystick = Native.SDL_OpenJoystick(id);
                if (joystick == IntPtr.Zero) continue;
                var axes = Native.SDL_GetNumJoystickAxes(joystick);
                var buttons = Native.SDL_GetNumJoystickButtons(joystick);
                var hats = Native.SDL_GetNumJoystickHats(joystick);
                Native.SDL_CloseJoystick(joystick);
                if (axes < 4 || buttons < 10 || hats < 1) continue;

                var guid = new System.Text.StringBuilder(33);
                Native.SDL_GUIDToString(Native.SDL_GetJoystickGUIDForID(id), guid, guid.Capacity);
                var platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Mac OS X";
                var mapping = $"{guid},{name},a:b0,b:b1,x:b2,y:b3,back:b6,start:b7,leftstick:b8,rightstick:b9," +
                    "leftshoulder:b4,rightshoulder:b5,dpup:h0.1,dpright:h0.2,dpdown:h0.4,dpleft:h0.8," +
                    $"leftx:a0,lefty:a1,rightx:a2,righty:a3,lefttrigger:+a4,righttrigger:+a5,platform:{platform},";
                if (Native.SDL_AddGamepadMapping(mapping) < 0) continue;
                Report("Controller navigation: " + name + " connected with Xbox-style controls");
                return true;
            }
            return false;
        }
        finally { if (devices != IntPtr.Zero) Native.SDL_free(devices); }
    }

    private static bool LooksLikeGamepad(string name) =>
        (name.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("gamepad", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("8BitDo", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("wireless pad", StringComparison.OrdinalIgnoreCase)) &&
        !name.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("flight", StringComparison.OrdinalIgnoreCase);

    private static void Report(string status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(null, status);
    }

    private static void CloseGamepad()
    {
        if (_gamepad != IntPtr.Zero) Native.SDL_CloseGamepad(_gamepad);
        _gamepad = IntPtr.Zero;
        Mapper.Reset();
    }

    private sealed class Registration(Window window, Func<bool> suppress, Control? initialFocus, Action? backAction)
    {
        public Window Window { get; } = window;
        public Func<bool> Suppress { get; } = suppress;
        public Control? InitialFocus { get; } = initialFocus;
        public Action? BackAction { get; } = backAction;
        public IInputElement? NavigationTarget { get; set; }
    }

    private enum MenuAction { None, Confirm, Back, Up, Down, Left, Right }

    private sealed class StateMapper
    {
        private bool _confirm, _back;
        private MenuAction _held;
        private DateTimeOffset _repeat;

        public MenuAction Map(bool confirm, bool back, bool upButton, bool downButton, bool leftButton,
            bool rightButton, short x, short y, DateTimeOffset now)
        {
            if (confirm && !_confirm) { Save(confirm, back); return MenuAction.Confirm; }
            if (back && !_back) { Save(confirm, back); return MenuAction.Back; }
            Save(confirm, back);
            var direction = leftButton || x <= -StickDeadZone ? MenuAction.Left :
                rightButton || x >= StickDeadZone ? MenuAction.Right :
                upButton || y <= -StickDeadZone ? MenuAction.Up :
                downButton || y >= StickDeadZone ? MenuAction.Down : MenuAction.None;
            if (direction is MenuAction.None) { _held = MenuAction.None; return MenuAction.None; }
            if (direction != _held) { _held = direction; _repeat = now.AddMilliseconds(360); return direction; }
            if (now < _repeat) return MenuAction.None;
            _repeat = now.AddMilliseconds(125); return direction;
        }

        private void Save(bool confirm, bool back) { _confirm = confirm; _back = back; }
        public void Reset() { _confirm = _back = false; _held = MenuAction.None; _repeat = default; }
    }

    private static class Native
    {
        internal const uint InitGamepad = 0x00002000;
        private const string Library = "SDL3";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_InitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_QuitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetGamepads(out int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_OpenGamepad(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseGamepad(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GamepadConnected(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_PumpEvents();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_UpdateGamepads();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GetGamepadButton(IntPtr gamepad, SdlButton button);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern short SDL_GetGamepadAxis(IntPtr gamepad, SdlAxis axis);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetJoysticks(out int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_IsGamepad(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetJoystickNameForID(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_OpenJoystick(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseJoystick(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetNumJoystickAxes(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetNumJoystickButtons(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetNumJoystickHats(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern SdlGuid SDL_GetJoystickGUIDForID(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_GUIDToString(SdlGuid guid,
            [MarshalAs(UnmanagedType.LPUTF8Str)] System.Text.StringBuilder buffer, int bufferLength);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_AddGamepadMapping(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string mapping);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_free(IntPtr pointer);
        internal static string String(IntPtr pointer, string fallback) => pointer == IntPtr.Zero ? fallback : Marshal.PtrToStringUTF8(pointer) ?? fallback;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct SdlGuid { private readonly ulong _first; private readonly ulong _second; }

    private enum SdlButton { Invalid = -1, South, East, West, North, Back, Guide, Start, LeftStick, RightStick, LeftShoulder, RightShoulder, DpadUp, DpadDown, DpadLeft, DpadRight }
    private enum SdlAxis { Invalid = -1, LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger }
}
