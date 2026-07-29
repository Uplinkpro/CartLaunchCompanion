using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class StudioScene : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private TranslateTransform? _wideBeamTransform;
    private TranslateTransform? _coreBeamTransform;
    private TranslateTransform? _hazeTransform;

    public StudioScene()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _timer.Tick += OnAnimationTick;
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
            ApplyReducedMotionState();
            return;
        }

        _clock.Restart();
        _timer.Start();
    }

    private void OnUnloaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void ApplyReducedMotionState()
    {
        _timer.Stop();
        _clock.Stop();

        WideBeam.Opacity = 0.48;
        CoreBeam.Opacity = 0.72;
        HazeLayer.Opacity = 0.08;
        FloorLight.Opacity = 0.55;
    }

    private void OnAnimationTick(
        object? sender,
        EventArgs e)
    {
        var seconds = _clock.Elapsed.TotalSeconds;

        var slowWave = Math.Sin(seconds * Math.PI / 5.5);
        var mediumWave = Math.Sin(seconds * Math.PI / 3.9 + 1.1);
        var hazeWave = Math.Sin(seconds * Math.PI / 8.0 + 0.6);

        if (_wideBeamTransform is not null)
            _wideBeamTransform.X = slowWave * 17.0;

        if (_coreBeamTransform is not null)
            _coreBeamTransform.X = mediumWave * 8.0;

        if (_hazeTransform is not null)
        {
            _hazeTransform.X = hazeWave * 24.0;
            _hazeTransform.Y = slowWave * 5.0;
        }

        WideBeam.Opacity = 0.47 + (slowWave * 0.035);
        CoreBeam.Opacity = 0.70 + (mediumWave * 0.045);
        HazeLayer.Opacity = 0.09 + (hazeWave * 0.025);
        FloorLight.Opacity = 0.53 + (slowWave * 0.045);
    }
}
