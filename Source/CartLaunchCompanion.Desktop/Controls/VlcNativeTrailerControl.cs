using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace CartLaunchCompanion.Desktop.Controls;

public sealed class VlcNativeTrailerControl : NativeControlHost, IDisposable
{
    private static readonly Lazy<Task> RuntimePreparation = new(
        () => Task.Run(() => LibVLCSharp.Shared.Core.Initialize()));

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
    private bool? _softwareDecodingMode;
    private bool _nativePlaybackAvailable;
    private bool _playbackUpdateQueued;
    private bool _hasUsableBounds;

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

    public static Task PrepareRuntimeAsync(CancellationToken cancellationToken = default) =>
        RuntimePreparation.Value.WaitAsync(cancellationToken);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            var nativeControl = base.CreateNativeControlCore(parent);
            _videoWindow = nativeControl.Handle;
            _nativePlaybackAvailable = IsX11Handle(nativeControl);

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
        return new PlatformHandle(_videoWindow, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopPlayback();
        DisposePlayerInstance();
        if (OperatingSystem.IsWindows() && control.Handle != IntPtr.Zero)
            DestroyWindow(control.Handle);
        else
            base.DestroyNativeControlCore(control);
        _videoWindow = IntPtr.Zero;
        _nativePlaybackAvailable = false;
        _hasUsableBounds = false;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (!_hasUsableBounds && finalSize.Width > 16 && finalSize.Height > 16)
        {
            _hasUsableBounds = true;
            QueuePlaybackUpdate();
        }
        return arranged;
    }

    private bool InitializePlayer(bool preferSoftwareDecoding)
    {
        if (_player is not null &&
            (!OperatingSystem.IsWindows() ||
             _softwareDecodingMode == preferSoftwareDecoding))
            return true;

        try
        {
            DisposePlayerInstance();
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC(
                "--no-audio",
                "--quiet",
                "--no-video-title-show");
            _player = new MediaPlayer(_libVlc)
            {
                Mute = true,
                EnableKeyInput = false,
                EnableMouseInput = false
            };
            _softwareDecodingMode = preferSoftwareDecoding;
            _player.Playing += OnPlaying;
            _player.EncounteredError += OnEncounteredError;
            AttachPlayerToCurrentWindow();
            return true;
        }
        catch
        {
            DisposePlayerInstance();
            return false;
        }
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SourceProperty || e.Property == IsActiveProperty)
        {
            if (_hasUsableBounds)
                QueuePlaybackUpdate();
        }
    }

    private void QueuePlaybackUpdate()
    {
        if (_playbackUpdateQueued)
            return;

        _playbackUpdateQueued = true;
        Dispatcher.UIThread.Post(
            async () =>
            {
                _playbackUpdateQueued = false;
                await UpdatePlaybackAsync();
            },
            DispatcherPriority.Loaded);
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
            var source = Source.Trim();
            var preferSoftwareDecoding = RequiresSoftwareDecoding(source);
            if (!InitializePlayer(preferSoftwareDecoding))
            {
                PlaybackFailed = true;
                return;
            }

            AttachPlayerToCurrentWindow();

            _media = Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile
                ? new Media(_libVlc!, uri)
                : new Media(_libVlc!, Path.GetFullPath(source));
            _media.AddOption(":network-caching=3000");
            _media.AddOption(":live-caching=3000");
            _media.AddOption(":file-caching=500");
            _media.AddOption(":http-reconnect");
            _media.AddOption(":http-user-agent=Mozilla/5.0 CartLaunchCompanion/2.3");
            _media.AddOption(":http-referrer=https://store.steampowered.com/");
            _media.AddOption(":adaptive-logic=highest");
            // VLC's automatic Windows decoder selection can report successful
            // playback while presenting a black native child surface. Legacy
            // Steam MP4s behave best with DXVA2, while current Steam encodes are
            // reliable in software. The downloaded filename identifies the
            // encoding family without depending on UI binding order.
            if (OperatingSystem.IsWindows())
                _media.AddOption(preferSoftwareDecoding
                    ? ":avcodec-hw=none"
                    : ":avcodec-hw=dxva2");
            else
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

    private void AttachPlayerToCurrentWindow()
    {
        if (_player is null || _videoWindow == IntPtr.Zero)
            return;

        if (OperatingSystem.IsWindows())
            _player.Hwnd = _videoWindow;
        else
            _player.XWindow = unchecked((uint)_videoWindow.ToInt64());
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

    private static bool RequiresSoftwareDecoding(string source)
    {
        if (source.Contains("SteamTrailer.Software.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.IsFile)
            return false;

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (segments[index].Equals("apps", StringComparison.OrdinalIgnoreCase) &&
                segments[index + 2].StartsWith("movie", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(segments[index + 1], out var movieAssetId))
            {
                return movieAssetId >= 100_000_000;
            }
        }

        return false;
    }

    public void Dispose()
    {
        StopPlayback();
        DisposePlayerInstance();
    }

    private void DisposePlayerInstance()
    {
        if (_player is not null)
        {
            _player.Playing -= OnPlaying;
            _player.EncounteredError -= OnEncounteredError;
            _player.Dispose();
        }

        _libVlc?.Dispose();
        _player = null;
        _libVlc = null;
        _softwareDecodingMode = null;
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
