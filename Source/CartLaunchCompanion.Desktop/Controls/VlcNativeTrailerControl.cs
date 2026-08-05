using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace CartLaunchCompanion.Desktop.Controls;

public sealed class VlcNativeTrailerControl : NativeControlHost, IDisposable
{
    private const uint WsExNoActivate = 0x08000000;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 8;

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VlcNativeTrailerControl, string?>(nameof(Source));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<VlcNativeTrailerControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> PlaybackFailedProperty =
        AvaloniaProperty.Register<VlcNativeTrailerControl, bool>(nameof(PlaybackFailed));

    private IntPtr _videoWindow;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private bool _nativePlaybackAvailable;

    public VlcNativeTrailerControl()
    {
        Focusable = false;
        PropertyChanged += OnControlPropertyChanged;
    }

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool PlaybackFailed
    {
        get => GetValue(PlaybackFailedProperty);
        private set => SetValue(PlaybackFailedProperty, value);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            var nativeControl = base.CreateNativeControlCore(parent);
            _videoWindow = nativeControl.Handle;
            _nativePlaybackAvailable = IsX11Handle(nativeControl);
            if (_nativePlaybackAvailable)
            {
                try
                {
                    InitializePlayer();
                    Dispatcher.UIThread.Post(() => _ = UpdatePlaybackAsync());
                }
                catch
                {
                    _nativePlaybackAvailable = false;
                    PlaybackFailed = IsActive && !string.IsNullOrWhiteSpace(Source);
                }
            }

            return nativeControl;
        }

        _videoWindow = CreateWindowEx(
            WsExNoActivate,
            "STATIC",
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_videoWindow == IntPtr.Zero)
            throw new InvalidOperationException(
                $"The VLC trailer host could not be created ({Marshal.GetLastWin32Error()}).");

        ShowWindow(_videoWindow, SwHide);
        _nativePlaybackAvailable = true;
        InitializePlayer();
        Dispatcher.UIThread.Post(() => _ = UpdatePlaybackAsync());
        return new PlatformHandle(_videoWindow, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopPlayback();
        if (OperatingSystem.IsWindows() && control.Handle != IntPtr.Zero)
            DestroyWindow(control.Handle);
        else
            base.DestroyNativeControlCore(control);
        _videoWindow = IntPtr.Zero;
        _nativePlaybackAvailable = false;
    }

    private void InitializePlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc ??= new LibVLC("--no-audio", "--quiet", "--no-video-title-show");
        if (_player is not null)
            return;

        _player = new MediaPlayer(_libVlc) { Mute = true };
        if (OperatingSystem.IsWindows())
            _player.Hwnd = _videoWindow;
        else
            _player.XWindow = unchecked((uint)_videoWindow.ToInt64());
        _player.EnableKeyInput = false;
        _player.EnableMouseInput = false;
        _player.Playing += OnPlaying;
        _player.EncounteredError += OnEncounteredError;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SourceProperty || e.Property == IsActiveProperty)
            Dispatcher.UIThread.Post(() => _ = UpdatePlaybackAsync());
    }

    private async Task UpdatePlaybackAsync()
    {
        StopPlayback();
        PlaybackFailed = false;
        if (!_nativePlaybackAvailable ||
            _videoWindow == IntPtr.Zero ||
            !IsActive ||
            string.IsNullOrWhiteSpace(Source))
        {
            PlaybackFailed = IsActive && !string.IsNullOrWhiteSpace(Source);
            return;
        }

        try
        {
            InitializePlayer();
            var source = Source.Trim();
            _media = Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile
                ? new Media(_libVlc!, uri)
                : new Media(_libVlc!, Path.GetFullPath(source));
            _media.AddOption(":network-caching=3000");
            _media.AddOption(":live-caching=3000");
            _media.AddOption(":file-caching=500");
            _media.AddOption(":http-reconnect");
            _media.AddOption(":http-user-agent=Mozilla/5.0 CartLaunchCompanion/2.0");
            _media.AddOption(":http-referrer=https://store.steampowered.com/");
            _media.AddOption(":adaptive-logic=highest");
            _media.AddOption(":avcodec-hw=any");
            _media.AddOption(":input-repeat=65535");
            _media.AddOption(":no-audio");

            if (_player!.Play(_media))
                await ConfirmPlaybackAsync();
            else
                PlaybackFailed = true;
        }
        catch
        {
            StopPlayback();
            PlaybackFailed = true;
        }
    }

    private async Task ConfirmPlaybackAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && IsActive)
        {
            if (_player?.IsPlaying == true)
                return;
            if (_player?.State is VLCState.Error or VLCState.Ended or VLCState.Stopped)
                break;
            await Task.Delay(250);
        }

        if (_player?.IsPlaying != true)
        {
            StopPlayback();
            PlaybackFailed = true;
        }
    }

    private void OnPlaying(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            PlaybackFailed = false;
            if (OperatingSystem.IsWindows() &&
                _videoWindow != IntPtr.Zero &&
                IsActive)
                ShowWindow(_videoWindow, SwShowNoActivate);
        });

    private void OnEncounteredError(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            StopPlayback();
            PlaybackFailed = true;
        });

    private void StopPlayback()
    {
        try { _player?.Stop(); } catch { }
        _media?.Dispose();
        _media = null;
        if (OperatingSystem.IsWindows() && _videoWindow != IntPtr.Zero)
            ShowWindow(_videoWindow, SwHide);
    }

    private static bool IsX11Handle(IPlatformHandle handle) =>
        OperatingSystem.IsLinux() &&
        (handle.HandleDescriptor?.Contains("X11", StringComparison.OrdinalIgnoreCase) == true ||
         handle.HandleDescriptor?.Contains("XID", StringComparison.OrdinalIgnoreCase) == true);

    public void Dispose()
    {
        StopPlayback();
        if (_player is not null)
        {
            _player.Playing -= OnPlaying;
            _player.EncounteredError -= OnEncounteredError;
        }
        _player?.Dispose();
        _libVlc?.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
