using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class StudioEnvironmentControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private TranslateTransform? _ambientTransform;
    private TranslateTransform? _haloTransform;
    private TranslateTransform? _fogLeftTransform;
    private TranslateTransform? _fogRightTransform;
    private TranslateTransform? _dustNearTransform;
    private TranslateTransform? _dustFarTransform;

    public StudioEnvironmentControl()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
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
        _ambientTransform = new TranslateTransform();
        _haloTransform = new TranslateTransform();
        _fogLeftTransform = new TranslateTransform();
        _fogRightTransform = new TranslateTransform();
        _dustNearTransform = new TranslateTransform();
        _dustFarTransform = new TranslateTransform();

        AmbientWash.RenderTransform = _ambientTransform;
        ContentHalo.RenderTransform = _haloTransform;
        FogLeft.RenderTransform = _fogLeftTransform;
        FogRight.RenderTransform = _fogRightTransform;
        DustNear.RenderTransform = _dustNearTransform;
        DustFar.RenderTransform = _dustFarTransform;

        if (ReduceMotion)
        {
            ApplyStaticState();
            return;
        }

        _clock.Restart();
        ApplyFrame(1.1);
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
        ApplyFrame(_clock.Elapsed.TotalSeconds + 1.1);
    }

    private void ApplyFrame(double seconds)
    {
        var ambient = Math.Sin(seconds * 0.18);
        var halo = Math.Sin((seconds * 0.23) + 0.8);
        var fogLeft = Math.Sin((seconds * 0.13) + 0.3);
        var fogRight = Math.Sin((seconds * 0.11) + 1.4);
        var dust = seconds % 30.0;

        if (_ambientTransform is not null)
        {
            _ambientTransform.X = ambient * 5.0;
            _ambientTransform.Y = halo * 2.0;
        }

        if (_haloTransform is not null)
        {
            _haloTransform.X = halo * 3.0;
            _haloTransform.Y = ambient * 2.0;
        }

        if (_fogLeftTransform is not null)
        {
            _fogLeftTransform.X = fogLeft * 26.0;
            _fogLeftTransform.Y = ambient * 5.0;
        }

        if (_fogRightTransform is not null)
        {
            _fogRightTransform.X = fogRight * 31.0;
            _fogRightTransform.Y = halo * 4.0;
        }

        if (_dustNearTransform is not null)
        {
            _dustNearTransform.X = Math.Sin(seconds * 0.16) * 10.0;
            _dustNearTransform.Y = -dust * 1.1;
        }

        if (_dustFarTransform is not null)
        {
            _dustFarTransform.X = Math.Sin((seconds * 0.10) + 1.2) * 7.0;
            _dustFarTransform.Y = -(dust * 0.65);
        }

        AmbientWash.Opacity = 0.21 + (ambient * 0.018);
        ContentHalo.Opacity = 0.17 + (halo * 0.022);
        FogLeft.Opacity = 0.085 + (fogLeft * 0.014);
        FogRight.Opacity = 0.070 + (fogRight * 0.012);
        DustNear.Opacity = 0.20 + (halo * 0.035);
        DustFar.Opacity = 0.10 + (ambient * 0.018);
        FloorBloomOuter.Opacity = 0.24 + (ambient * 0.025);
        FloorBloomInner.Opacity = 0.29 + (halo * 0.032);
    }

    private void ApplyStaticState()
    {
        AmbientWash.Opacity = 0.22;
        ContentHalo.Opacity = 0.18;
        FogLeft.Opacity = 0.09;
        FogRight.Opacity = 0.075;
        DustNear.Opacity = 0.20;
        DustFar.Opacity = 0.10;
        FloorBloomOuter.Opacity = 0.25;
        FloorBloomInner.Opacity = 0.30;
    }
}
