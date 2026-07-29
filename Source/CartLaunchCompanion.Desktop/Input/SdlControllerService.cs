using System.Runtime.InteropServices;
using Avalonia.Threading;
using CartLaunchCompanion.Core.Input;

namespace CartLaunchCompanion.Desktop.Input;

public sealed class SdlControllerService : IAsyncDisposable
{
    private readonly SdlGamepadStateMapper _mapper = new();

    private DispatcherTimer? _pollTimer;
    private IntPtr _gamepad;
    private string _gamepadName = "";
    private DateTimeOffset _nextScan = DateTimeOffset.MinValue;
    private bool _initialized;
    private bool _reportedNoController;

    public event EventHandler<LauncherInputEvent>? InputReceived;
    public event EventHandler<ControllerConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<string>? DiagnosticChanged;

    public bool IsConnected => _gamepad != IntPtr.Zero;

    public void Start()
    {
        if (_pollTimer is not null)
            return;

        Dispatcher.UIThread.VerifyAccess();

        try
        {
            if (!Sdl3Native.SDL_InitSubSystem(
                    Sdl3Native.InitGamepad))
            {
                DiagnosticChanged?.Invoke(
                    this,
                    $"SDL3 gamepad initialization failed: {Sdl3Native.GetError()}");
                return;
            }

            _initialized = true;

            DiagnosticChanged?.Invoke(
                this,
                "SDL3 controller service started.");

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            _pollTimer.Tick += PollOnUiThread;
            _pollTimer.Start();

            // Perform the first scan immediately rather than waiting for
            // the first timer tick.
            PollOnUiThread(this, EventArgs.Empty);
        }
        catch (DllNotFoundException ex)
        {
            DiagnosticChanged?.Invoke(
                this,
                $"SDL3 native library was not found: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            DiagnosticChanged?.Invoke(
                this,
                $"The installed SDL3 library is incompatible: {ex.Message}");
        }
        catch (Exception ex)
        {
            DiagnosticChanged?.Invoke(
                this,
                $"SDL3 controller service failed: {ex.Message}");
        }
    }

    private void PollOnUiThread(
        object? sender,
        EventArgs eventArgs)
    {
        Dispatcher.UIThread.VerifyAccess();

        try
        {
            // SDL requires event pumping on the main thread for hot-plugged
            // devices to become visible to the application.
            Sdl3Native.SDL_PumpEvents();
            Sdl3Native.SDL_UpdateGamepads();

            var now = DateTimeOffset.UtcNow;

            if (_gamepad != IntPtr.Zero &&
                !Sdl3Native.SDL_GamepadConnected(_gamepad))
            {
                CloseCurrentGamepad();
                _nextScan = DateTimeOffset.MinValue;
            }

            if (_gamepad == IntPtr.Zero &&
                now >= _nextScan)
            {
                TryOpenFirstGamepad();
                _nextScan = now.AddMilliseconds(500);
            }

            if (_gamepad != IntPtr.Zero)
                PollCurrentGamepad(now);
        }
        catch (Exception ex)
        {
            DiagnosticChanged?.Invoke(
                this,
                $"SDL3 polling failed: {ex.Message}");
        }
    }

    private void TryOpenFirstGamepad()
    {
        var pointer = Sdl3Native.SDL_GetGamepads(out var count);

        try
        {
            if (pointer == IntPtr.Zero || count <= 0)
            {
                if (!_reportedNoController)
                {
                    _reportedNoController = true;

                    ConnectionChanged?.Invoke(
                        this,
                        new ControllerConnectionEventArgs(
                            false,
                            "No controller detected."));
                }

                return;
            }

            for (var index = 0; index < count; index++)
            {
                var instanceId = unchecked(
                    (uint)Marshal.ReadInt32(
                        pointer,
                        index * sizeof(uint)));

                var opened =
                    Sdl3Native.SDL_OpenGamepad(instanceId);

                if (opened == IntPtr.Zero)
                    continue;

                _gamepad = opened;
                _gamepadName =
                    Sdl3Native.GetGamepadName(_gamepad);
                _mapper.Reset();
                _reportedNoController = false;

                ConnectionChanged?.Invoke(
                    this,
                    new ControllerConnectionEventArgs(
                        true,
                        _gamepadName));

                DiagnosticChanged?.Invoke(
                    this,
                    $"Controller connected: {_gamepadName}");

                return;
            }

            DiagnosticChanged?.Invoke(
                this,
                $"SDL3 found {count} controller device(s), but none could be opened: {Sdl3Native.GetError()}");
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                Sdl3Native.SDL_free(pointer);
        }
    }

    private void PollCurrentGamepad(
        DateTimeOffset timestamp)
    {
        var actions = _mapper.Map(
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.South),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.East),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.West),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.DpadUp),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.DpadDown),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.DpadLeft),
            Sdl3Native.SDL_GetGamepadButton(
                _gamepad,
                SdlGamepadButton.DpadRight),
            Sdl3Native.SDL_GetGamepadAxis(
                _gamepad,
                SdlGamepadAxis.LeftX),
            Sdl3Native.SDL_GetGamepadAxis(
                _gamepad,
                SdlGamepadAxis.LeftY),
            timestamp);

        foreach (var action in actions)
        {
            DiagnosticChanged?.Invoke(
                this,
                $"Controller action: {action}");

            InputReceived?.Invoke(
                this,
                new LauncherInputEvent(
                    action,
                    InputDeviceKind.Controller,
                    timestamp));
        }
    }

    private void CloseCurrentGamepad()
    {
        if (_gamepad == IntPtr.Zero)
            return;

        try
        {
            Sdl3Native.SDL_CloseGamepad(_gamepad);
        }
        finally
        {
            _gamepad = IntPtr.Zero;
            _mapper.Reset();

            ConnectionChanged?.Invoke(
                this,
                new ControllerConnectionEventArgs(
                    false,
                    string.IsNullOrWhiteSpace(_gamepadName)
                        ? "Controller disconnected."
                        : $"{_gamepadName} disconnected."));

            DiagnosticChanged?.Invoke(
                this,
                "Controller disconnected. Waiting for a controller…");

            _gamepadName = "";
            _reportedNoController = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= PollOnUiThread;
            _pollTimer = null;
        }

        CloseCurrentGamepad();

        if (_initialized)
        {
            Sdl3Native.SDL_QuitSubSystem(
                Sdl3Native.InitGamepad);

            _initialized = false;
        }

        return ValueTask.CompletedTask;
    }
}

public sealed record ControllerConnectionEventArgs(
    bool Connected,
    string Description);
