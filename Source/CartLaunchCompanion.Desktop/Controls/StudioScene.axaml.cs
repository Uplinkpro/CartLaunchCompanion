using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class StudioScene : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private TranslateTransform? _wideTransform;
    private TranslateTransform? _coreTransform;
    private TranslateTransform? _hazeTransform;

    public StudioScene()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        _timer.Tick += OnTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static bool ReduceMotion
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(
                "CLC_REDUCE_MOTION");

            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnLoaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        _wideTransform = new TranslateTransform();
        _coreTransform = new TranslateTransform();
        _hazeTransform = new TranslateTransform();

        WideBeam.RenderTransform = _wideTransform;
        CoreBeam.RenderTransform = _coreTransform;
        HazeLayer.RenderTransform = _hazeTransform;

        if (ReduceMotion)
        {
            ApplyStaticState();
            return;
        }

        _clock.Restart();

        // Start on a visible non-zero frame.
        ApplyFrame(1.25);

        _timer.Start();
    }

    private void OnUnloaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        ApplyFrame(_clock.Elapsed.TotalSeconds + 1.25);
    }

    private void ApplyFrame(double seconds)
    {
        var wide = Math.Sin(seconds * 0.95);
        var core = Math.Sin((seconds * 1.30) + 0.85);
        var haze = Math.Sin((seconds * 0.62) + 0.35);

        if (_wideTransform is not null)
            _wideTransform.X = wide * 58.0;

        if (_coreTransform is not null)
            _coreTransform.X = core * 26.0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = haze * 72.0;
            _hazeTransform.Y = wide * 12.0;
        }

        WideBeam.Opacity = 0.54 + (wide * 0.11);
        CoreBeam.Opacity = 0.76 + (core * 0.12);
        HazeLayer.Opacity = 0.14 + (haze * 0.06);
        FloorLight.Opacity = 0.61 + (wide * 0.12);
    }

    private void ApplyStaticState()
    {
        _timer.Stop();
        _clock.Stop();

        WideBeam.Opacity = 0.54;
        CoreBeam.Opacity = 0.78;
        HazeLayer.Opacity = 0.12;
        FloorLight.Opacity = 0.62;
    }
}
