using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class StudioScene : UserControl
{
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _clock = new();

    private TranslateTransform? _wideBeamTransform;
    private TranslateTransform? _coreBeamTransform;
    private TranslateTransform? _hazeTransform;

    public StudioScene()
    {
        InitializeComponent();

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _animationTimer.Tick += OnAnimationTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private bool ReduceMotion =>
        AnimationPreferenceParser.IsReducedMotionValue(
            Environment.GetEnvironmentVariable("CLC_REDUCE_MOTION"));

    private void OnLoaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        _wideBeamTransform = new TranslateTransform();
        _coreBeamTransform = new TranslateTransform();
        _hazeTransform = new TranslateTransform();

        WideBeam.RenderTransform = _wideBeamTransform;
        CoreBeam.RenderTransform = _coreBeamTransform;
        HazeLayer.RenderTransform = _hazeTransform;

        if (ReduceMotion)
        {
            ApplyStaticState();
            return;
        }

        _clock.Restart();
        _animationTimer.Start();

        // Apply the first non-zero frame immediately so the scene does not
        // appear static during startup.
        ApplyFrame(0.75);
    }

    private void OnUnloaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        _animationTimer.Stop();
        _clock.Stop();
    }

    private void OnAnimationTick(
        object? sender,
        EventArgs e)
    {
        ApplyFrame(_clock.Elapsed.TotalSeconds);
    }

    private void ApplyFrame(double seconds)
    {
        var wideWave = Math.Sin(seconds * Math.PI / 4.5);
        var coreWave = Math.Sin(seconds * Math.PI / 3.2 + 0.9);
        var hazeWave = Math.Sin(seconds * Math.PI / 6.5 + 0.4);

        if (_wideBeamTransform is not null)
            _wideBeamTransform.X = wideWave * 34.0;

        if (_coreBeamTransform is not null)
            _coreBeamTransform.X = coreWave * 14.0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = hazeWave * 42.0;
            _hazeTransform.Y = wideWave * 8.0;
        }

        WideBeam.Opacity = 0.49 + (wideWave * 0.06);
        CoreBeam.Opacity = 0.72 + (coreWave * 0.07);
        HazeLayer.Opacity = 0.11 + (hazeWave * 0.035);
        FloorLight.Opacity = 0.56 + (wideWave * 0.07);
    }

    private void ApplyStaticState()
    {
        _animationTimer.Stop();
        _clock.Stop();

        if (_wideBeamTransform is not null)
            _wideBeamTransform.X = 0;

        if (_coreBeamTransform is not null)
            _coreBeamTransform.X = 0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = 0;
            _hazeTransform.Y = 0;
        }

        WideBeam.Opacity = 0.50;
        CoreBeam.Opacity = 0.74;
        HazeLayer.Opacity = 0.10;
        FloorLight.Opacity = 0.58;
    }
}
