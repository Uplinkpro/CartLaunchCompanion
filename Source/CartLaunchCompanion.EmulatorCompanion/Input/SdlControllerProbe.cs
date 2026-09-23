using System.Runtime.InteropServices;
using Avalonia.Threading;
using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.EmulatorCompanion.Input;

public sealed class SdlControllerProbe : IAsyncDisposable
{
    private DispatcherTimer? _timer;
    private IntPtr _gamepad;
    private readonly HashSet<CanonicalControllerInput> _active = [];
    private readonly HashSet<uint> _fallbackMappings = [];
    private DateTimeOffset _nextScan;
    private bool _initialized;
    private string? _lastDiagnostic;
    private int _preferredIndex;

    public event EventHandler<DetectedGameController?>? ControllerChanged;
    public event EventHandler<CanonicalControllerInput>? InputActivated;
    public event EventHandler<string>? DiagnosticChanged;
    public DetectedGameController? Controller { get; private set; }

    public void SelectControllerIndex(int index)
    {
        if (index is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(index));
        if (_preferredIndex == index) return;
        _preferredIndex = index;
        Close();
        _nextScan = DateTimeOffset.MinValue;
        if (_initialized) Scan();
    }

    public void Start()
    {
        if (_timer is not null) return;
        try
        {
            if (!Native.SDL_InitSubSystem(Native.InitGamepad)) throw new IOException("SDL could not start: " + Native.Error());
            _initialized = true;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += Poll; _timer.Start(); Poll(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or IOException)
        { Report(error.Message); }
    }

    private void Poll(object? sender, EventArgs args)
    {
        try
        {
            Native.SDL_PumpEvents(); Native.SDL_UpdateGamepads();
            if (_gamepad != IntPtr.Zero && !Native.SDL_GamepadConnected(_gamepad)) Close();
            if (_gamepad == IntPtr.Zero && DateTimeOffset.UtcNow >= _nextScan)
            { Scan(); _nextScan = DateTimeOffset.UtcNow.AddMilliseconds(500); }
            if (_gamepad != IntPtr.Zero) PollInputs();
        }
        catch (Exception error)
        {
            Close();
            Report("Controller polling failed: " + error.Message);
        }
    }

    private void Scan()
    {
        var devices = Native.SDL_GetGamepads(out var count);
        try
        {
            if (devices == IntPtr.Zero || count == 0)
            {
                if (TryRegisterXboxStyleFallback()) return;
                ReportJoystickStatus();
                return;
            }
            var availableIndex = 0;
            var nameOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, i * sizeof(uint)));
                var opened = Native.SDL_OpenGamepad(id); if (opened == IntPtr.Zero) continue;
                var name = FriendlyName(opened);
                var ordinal = nameOrdinals.TryGetValue(name, out var seen) ? seen + 1 : 1;
                nameOrdinals[name] = ordinal;
                if (availableIndex++ != _preferredIndex) { Native.SDL_CloseGamepad(opened); continue; }
                _gamepad = opened;
                var vendor = Native.SDL_GetGamepadVendor(opened);
                var product = Native.SDL_GetGamepadProduct(opened);
                var type = Native.SDL_GetGamepadType(opened);
                var mappingPointer = Native.SDL_GetGamepadMapping(opened);
                var mapping = Native.String(mappingPointer, "");
                if (mappingPointer != IntPtr.Zero) Native.SDL_free(mappingPointer);
                var stableId = mapping.Split(',', 2)[0];
                if (stableId.Length < 8) stableId = $"sdl-{id:x8}-{name}";
                var properties = Native.SDL_GetGamepadProperties(opened);
                var hasRumble = properties != 0 && Native.SDL_GetBooleanProperty(properties,
                    Native.RumbleProperty, false);
                var protocol = Protocol(type);
                Controller = new(stableId, name, Layout(type), mapping.Length > 0, hasRumble,
                    ControllerCompatibilityCatalog.Classify(vendor, product, name, protocol), vendor, product,
                    _fallbackMappings.Contains(id) ? ControllerMappingKind.BuiltInXboxFallback : ControllerMappingKind.SdlStandard,
                    ordinal);
                _lastDiagnostic = null;
                ControllerChanged?.Invoke(this, Controller); return;
            }
            Report(_preferredIndex == 1
                ? "Player 2 needs a second connected controller. Connect it by USB or Bluetooth."
                : "No compatible controller is available for Player 1.");
        }
        finally { if (devices != IntPtr.Zero) Native.SDL_free(devices); }
    }

    private bool TryRegisterXboxStyleFallback()
    {
        var devices = Native.SDL_GetJoysticks(out var count);
        try
        {
            var availableIndex = 0;
            for (var i = 0; i < count; i++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, i * sizeof(uint)));
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
                if (availableIndex++ != _preferredIndex) continue;

                var buffer = new System.Text.StringBuilder(33);
                Native.SDL_GUIDToString(Native.SDL_GetJoystickGUIDForID(id), buffer, buffer.Capacity);
                var platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Mac OS X";
                var mapping = $"{buffer},{name},a:b0,b:b1,x:b2,y:b3,back:b6,start:b7,leftstick:b8,rightstick:b9," +
                    "leftshoulder:b4,rightshoulder:b5,dpup:h0.1,dpright:h0.2,dpdown:h0.4,dpleft:h0.8," +
                    $"leftx:a0,lefty:a1,rightx:a2,righty:a3,lefttrigger:+a4,righttrigger:+a5,platform:{platform},";
                if (Native.SDL_AddGamepadMapping(mapping) < 0) continue;
                _fallbackMappings.Add(id);
                Report($"{name} is using the built-in Xbox-style fallback. Complete every input test to verify it.");
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

    private string FriendlyName(IntPtr gamepad)
    {
        var fallback = Native.String(Native.SDL_GetGamepadName(gamepad), "Gamepad");
        var vendor = Native.SDL_GetGamepadVendor(gamepad);
        var product = Native.SDL_GetGamepadProduct(gamepad);
        if (vendor == 0 && product == 0) return fallback;
        var devices = Native.SDL_GetJoysticks(out var count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, i * sizeof(uint)));
                if (Native.SDL_GetJoystickVendorForID(id) != vendor || Native.SDL_GetJoystickProductForID(id) != product) continue;
                var candidate = Native.String(Native.SDL_GetJoystickNameForID(id), "").Trim('(', ')');
                if (candidate.Length > 0 && !candidate.StartsWith("XInput Controller", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return fallback;
        }
        finally { if (devices != IntPtr.Zero) Native.SDL_free(devices); }
    }

    private void ReportJoystickStatus()
    {
        var devices = Native.SDL_GetJoysticks(out var count);
        try
        {
            if (devices == IntPtr.Zero || count == 0)
            {
                Report("No controller detected. On Windows, connect an 8BitDo controller in XInput mode.");
                return;
            }
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(devices, i * sizeof(uint)));
                var name = Native.String(Native.SDL_GetJoystickNameForID(id), "Generic controller").Trim('(', ')');
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
            Report($"Detected {string.Join(", ", names)}, but SDL has no standard gamepad mapping. Try XInput mode and reconnect it.");
        }
        finally { if (devices != IntPtr.Zero) Native.SDL_free(devices); }
    }

    private void Report(string message)
    {
        if (message == _lastDiagnostic) return;
        _lastDiagnostic = message;
        DiagnosticChanged?.Invoke(this, message);
    }

    private void PollInputs()
    {
        var current = new HashSet<CanonicalControllerInput>();
        AddButton(current, Button.South, CanonicalControllerInput.South); AddButton(current, Button.East, CanonicalControllerInput.East);
        AddButton(current, Button.West, CanonicalControllerInput.West); AddButton(current, Button.North, CanonicalControllerInput.North);
        AddButton(current, Button.Back, CanonicalControllerInput.Back); AddButton(current, Button.Start, CanonicalControllerInput.Start);
        AddButton(current, Button.LeftStick, CanonicalControllerInput.LeftStickClick); AddButton(current, Button.RightStick, CanonicalControllerInput.RightStickClick);
        AddButton(current, Button.LeftShoulder, CanonicalControllerInput.LeftShoulder); AddButton(current, Button.RightShoulder, CanonicalControllerInput.RightShoulder);
        AddButton(current, Button.DpadUp, CanonicalControllerInput.DpadUp); AddButton(current, Button.DpadDown, CanonicalControllerInput.DpadDown);
        AddButton(current, Button.DpadLeft, CanonicalControllerInput.DpadLeft); AddButton(current, Button.DpadRight, CanonicalControllerInput.DpadRight);
        AddAxis(current, Axis.LeftX, CanonicalControllerInput.LeftStickX); AddAxis(current, Axis.LeftY, CanonicalControllerInput.LeftStickY);
        AddAxis(current, Axis.RightX, CanonicalControllerInput.RightStickX); AddAxis(current, Axis.RightY, CanonicalControllerInput.RightStickY);
        AddAxis(current, Axis.LeftTrigger, CanonicalControllerInput.LeftTrigger, 8000); AddAxis(current, Axis.RightTrigger, CanonicalControllerInput.RightTrigger, 8000);
        foreach (var input in current.Where(input => !_active.Contains(input))) InputActivated?.Invoke(this, input);
        _active.Clear(); _active.UnionWith(current);
    }

    private void AddButton(HashSet<CanonicalControllerInput> set, Button button, CanonicalControllerInput input)
    { if (Native.SDL_GetGamepadButton(_gamepad, button)) set.Add(input); }
    private void AddAxis(HashSet<CanonicalControllerInput> set, Axis axis, CanonicalControllerInput input, int threshold = 16000)
    { if (Math.Abs((int)Native.SDL_GetGamepadAxis(_gamepad, axis)) >= threshold) set.Add(input); }
    private static ControllerLayoutKind Layout(GamepadType type) => type switch
    {
        GamepadType.PS3 or GamepadType.PS4 or GamepadType.PS5 => ControllerLayoutKind.PlayStation,
        GamepadType.NintendoSwitchPro or GamepadType.NintendoSwitchJoyconLeft or GamepadType.NintendoSwitchJoyconRight or GamepadType.NintendoSwitchJoyconPair => ControllerLayoutKind.Nintendo,
        GamepadType.Xbox360 or GamepadType.XboxOne or GamepadType.Standard or GamepadType.Steam => ControllerLayoutKind.Xbox,
        _ => ControllerLayoutKind.Generic
    };
    private static ControllerProtocolKind Protocol(GamepadType type) => type switch
    {
        GamepadType.Xbox360 => ControllerProtocolKind.Xbox360,
        GamepadType.XboxOne => ControllerProtocolKind.XboxOne,
        GamepadType.PS3 => ControllerProtocolKind.PlayStation3,
        GamepadType.PS4 => ControllerProtocolKind.PlayStation4,
        GamepadType.PS5 => ControllerProtocolKind.PlayStation5,
        GamepadType.NintendoSwitchPro or GamepadType.NintendoSwitchJoyconLeft or GamepadType.NintendoSwitchJoyconRight or GamepadType.NintendoSwitchJoyconPair => ControllerProtocolKind.NintendoSwitch,
        GamepadType.Steam => ControllerProtocolKind.Steam,
        _ => ControllerProtocolKind.Standard
    };
    private void Close()
    {
        if (_gamepad != IntPtr.Zero) Native.SDL_CloseGamepad(_gamepad);
        _gamepad = IntPtr.Zero; _active.Clear(); Controller = null; ControllerChanged?.Invoke(this, null);
    }
    public ValueTask DisposeAsync()
    {
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= Poll; _timer = null; }
        Close(); if (_initialized) Native.SDL_QuitSubSystem(Native.InitGamepad); _initialized = false; return ValueTask.CompletedTask;
    }

    private static class Native
    {
        internal const uint InitGamepad = 0x00002000;
        internal const string RumbleProperty = "SDL.joystick.cap.rumble";
        private const string Library = "SDL3";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_InitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_QuitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetError();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetGamepads(out int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_OpenGamepad(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseGamepad(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GamepadConnected(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_PumpEvents();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_UpdateGamepads();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GetGamepadButton(IntPtr gamepad, Button button);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern short SDL_GetGamepadAxis(IntPtr gamepad, Axis axis);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetGamepadMapping(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern GamepadType SDL_GetGamepadType(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetGamepadVendor(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetGamepadProduct(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_GetGamepadProperties(IntPtr gamepad);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_GetBooleanProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.I1)] bool defaultValue);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetJoysticks(out int count);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_IsGamepad(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SDL_GetJoystickNameForID(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetJoystickVendorForID(uint id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetJoystickProductForID(uint id);
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
        internal static string Error() => String(SDL_GetError(), "Unknown SDL error");
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct SdlGuid { private readonly ulong _first; private readonly ulong _second; }
    private enum Button { Invalid = -1, South, East, West, North, Back, Guide, Start, LeftStick, RightStick, LeftShoulder, RightShoulder, DpadUp, DpadDown, DpadLeft, DpadRight }
    private enum Axis { Invalid = -1, LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger }
    private enum GamepadType { Unknown, Standard, Xbox360, XboxOne, PS3, PS4, PS5, NintendoSwitchPro, NintendoSwitchJoyconLeft, NintendoSwitchJoyconRight, NintendoSwitchJoyconPair, GameCube, Steam }
}
