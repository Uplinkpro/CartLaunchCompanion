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
    private TranslateTransform? _shaftTransform;

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
        _shaftTransform = new TranslateTransform();

        WideBeam.RenderTransform = _wideTransform;
        CoreBeam.RenderTransform = _coreTransform;
        BeamShaft.RenderTransform = _shaftTransform;

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
        var wide = Math.Sin(seconds * 0.68);
        var core = Math.Sin((seconds * 0.96) + 0.85);
        var shaft = Math.Sin((seconds * 1.18) + 1.35);

        if (_wideTransform is not null)
            _wideTransform.X = wide * 42.0;

        if (_coreTransform is not null)
            _coreTransform.X = core * 30.0;

        if (_shaftTransform is not null)
            _shaftTransform.X = shaft * 18.0;

        WideBeam.Opacity = 0.23 + (wide * 0.045);
        CoreBeam.Opacity = 0.76 + (core * 0.13);
        BeamShaft.Opacity = 0.27 + (shaft * 0.06);
        FloorLight.Opacity = 0.60 + (wide * 0.11);
    }

    private void ApplyStaticState()
    {
        WideBeam.Opacity = 0.24;
        CoreBeam.Opacity = 0.78;
        BeamShaft.Opacity = 0.28;
        FloorLight.Opacity = 0.62;
    }
}

