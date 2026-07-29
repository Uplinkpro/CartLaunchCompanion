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
        ApplyFrame(1.0);
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
        ApplyFrame(_clock.Elapsed.TotalSeconds + 1.0);
    }

    private void ApplyFrame(double seconds)
    {
        var wide = Math.Sin(seconds * 0.72);
        var core = Math.Sin((seconds * 1.05) + 0.8);
        var haze = Math.Sin((seconds * 0.48) + 0.35);

        if (_wideTransform is not null)
            _wideTransform.X = wide * 54.0;

        if (_coreTransform is not null)
            _coreTransform.X = core * 24.0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = haze * 70.0;
            _hazeTransform.Y = wide * 10.0;
        }

        WideBeam.Opacity = 0.34 + (wide * 0.08);
        CoreBeam.Opacity = 0.56 + (core * 0.12);
        HazeLayer.Opacity = 0.15 + (haze * 0.05);
        FloorLight.Opacity = 0.61 + (wide * 0.12);
    }

    private void ApplyStaticState()
    {
        WideBeam.Opacity = 0.34;
        CoreBeam.Opacity = 0.56;
        HazeLayer.Opacity = 0.13;
        FloorLight.Opacity = 0.62;
    }
}
