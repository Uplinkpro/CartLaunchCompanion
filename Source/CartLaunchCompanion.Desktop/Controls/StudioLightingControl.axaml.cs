using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class StudioLightingControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private TranslateTransform? _beamTransform;
    private TranslateTransform? _coreTransform;
    private TranslateTransform? _hazeTransform;
    private TranslateTransform? _dustTransform;

    public StudioLightingControl()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
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
        _beamTransform = new TranslateTransform();
        _coreTransform = new TranslateTransform();
        _hazeTransform = new TranslateTransform();
        _dustTransform = new TranslateTransform();

        BeamGroup.RenderTransform = _beamTransform;
        BeamCore.RenderTransform = _coreTransform;
        HazeGroup.RenderTransform = _hazeTransform;
        DustGroup.RenderTransform = _dustTransform;

        if (ReduceMotion)
        {
            ApplyStaticState();
            return;
        }

        _clock.Restart();
        ApplyFrame(0.8);
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
        ApplyFrame(_clock.Elapsed.TotalSeconds + 0.8);
    }

    private void ApplyFrame(double seconds)
    {
        var beamWave = Math.Sin(seconds * 0.34);
        var coreWave = Math.Sin((seconds * 0.55) + 0.9);
        var hazeWave = Math.Sin((seconds * 0.22) + 0.4);
        var dustWave = Math.Sin((seconds * 0.16) + 1.3);

        if (_beamTransform is not null)
            _beamTransform.X = beamWave * 14.0;

        if (_coreTransform is not null)
            _coreTransform.X = coreWave * 7.0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = hazeWave * 22.0;
            _hazeTransform.Y = beamWave * 4.0;
        }

        if (_dustTransform is not null)
        {
            _dustTransform.X = dustWave * 12.0;
            _dustTransform.Y = -(seconds % 22.0) * 1.25;
        }

        BeamGroup.Opacity = 0.92 + (beamWave * 0.035);
        BeamCore.Opacity = 0.11 + (coreWave * 0.025);
        HazeGroup.Opacity = 0.075 + (hazeWave * 0.018);
        DustGroup.Opacity = 0.23 + (dustWave * 0.055);
        FloorBloomOuter.Opacity = 0.27 + (beamWave * 0.035);
        FloorBloomInner.Opacity = 0.37 + (coreWave * 0.045);
    }

    private void ApplyStaticState()
    {
        BeamGroup.Opacity = 0.92;
        BeamCore.Opacity = 0.12;
        HazeGroup.Opacity = 0.075;
        DustGroup.Opacity = 0.23;
        FloorBloomOuter.Opacity = 0.28;
        FloorBloomInner.Opacity = 0.38;
    }
}
