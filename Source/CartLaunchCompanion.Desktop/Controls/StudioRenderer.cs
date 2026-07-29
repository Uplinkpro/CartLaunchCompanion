using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Controls;

/// <summary>
/// Custom-drawn atmospheric background for Cart Launch Companion.
///
/// The renderer intentionally draws light indirectly:
/// room exposure, fog volumes, floor bloom, a content halo, and dust.
/// It does not draw visible beam polygons.
/// </summary>
public sealed class StudioRenderer : Control
{
    private const int TargetFrameMilliseconds = 33;
    private const int ParticleCount = 52;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly DustParticle[] _particles;

    private readonly SolidColorBrush _roomTopBrush =
        new(Color.Parse("#06070A"));

    private readonly SolidColorBrush _roomBottomBrush =
        new(Color.Parse("#020305"));

    private readonly SolidColorBrush _floorBrush =
        new(Color.Parse("#080A0E"));

    private readonly SolidColorBrush _horizonBrush =
        new(Color.Parse("#28FFFFFF"));

    private readonly SolidColorBrush _dustBrush =
        new(Color.Parse("#D8FFFFFF"));

    public StudioRenderer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _particles = CreateParticles();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                TargetFrameMilliseconds)
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

            return string.Equals(
                       value,
                       "1",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       value,
                       "true",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       value,
                       "yes",
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 1 || height <= 1)
            return;

        var time = ReduceMotion
            ? 0.0
            : _clock.Elapsed.TotalSeconds;

        var accent = ResolveAccentColor();
        var accentBright = Lighten(accent, 0.35);
        var accentMuted = WithAlpha(accent, 0.16);

        DrawRoom(context, width, height);
        DrawAmbientExposure(
            context,
            width,
            height,
            time,
            accentMuted);

        DrawFog(
            context,
            width,
            height,
            time,
            accent);

        DrawContentHalo(
            context,
            width,
            height,
            time,
            accentBright);

        DrawDust(
            context,
            width,
            height,
            time);

        DrawFloor(
            context,
            width,
            height,
            time,
            accent,
            accentBright);

        DrawVignette(context, width, height);
    }

    private void DrawRoom(
        DrawingContext context,
        double width,
        double height)
    {
        context.FillRectangle(
            _roomBottomBrush,
            new Rect(0, 0, width, height));

        using (context.PushOpacity(0.70))
        {
            context.FillRectangle(
                _roomTopBrush,
                new Rect(0, 0, width, height * 0.58));
        }
    }

    private static void DrawAmbientExposure(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color color)
    {
        var wave = Math.Sin(time * 0.18);
        var center = new Point(
            (width * 0.50) + (wave * 5.0),
            height * 0.30);

        DrawSoftEllipse(
            context,
            center,
            width * 0.34,
            height * 0.42,
            color,
            12,
            0.012);
    }

    private static void DrawFog(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color accent)
    {
        var leftWave = Math.Sin((time * 0.13) + 0.3);
        var rightWave = Math.Sin((time * 0.11) + 1.4);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.35) + (leftWave * 28.0),
                height * 0.42),
            width * 0.29,
            height * 0.20,
            WithAlpha(accent, 0.10),
            10,
            0.008);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.68) + (rightWave * 32.0),
                height * 0.57),
            width * 0.33,
            height * 0.22,
            WithAlpha(accent, 0.08),
            10,
            0.007);
    }

    private static void DrawContentHalo(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color color)
    {
        var pulse = Math.Sin((time * 0.23) + 0.8);
        var radiusScale = 1.0 + (pulse * 0.025);

        DrawSoftEllipse(
            context,
            new Point(width * 0.50, height * 0.245),
            width * 0.19 * radiusScale,
            height * 0.18 * radiusScale,
            WithAlpha(color, 0.12),
            10,
            0.010);
    }

    private void DrawDust(
        DrawingContext context,
        double width,
        double height,
        double time)
    {
        foreach (var particle in _particles)
        {
            var normalizedY =
                PositiveModulo(
                    particle.StartY -
                    (time * particle.Speed),
                    1.0);

            var drift =
                Math.Sin(
                    (time * particle.DriftSpeed) +
                    particle.Phase) *
                particle.DriftAmount;

            var x =
                (particle.StartX * width) +
                (drift * width);

            var y = normalizedY * height * 0.78;

            var fade =
                Math.Sin(normalizedY * Math.PI);

            var opacity =
                particle.Opacity *
                Math.Max(0.0, fade);

            using (context.PushOpacity(opacity))
            {
                context.DrawEllipse(
                    _dustBrush,
                    null,
                    new Point(x, y),
                    particle.Size,
                    particle.Size);
            }
        }
    }

    private void DrawFloor(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color accent,
        Color accentBright)
    {
        var floorTop = height * 0.755;
        var floorHeight = height - floorTop;

        context.FillRectangle(
            _floorBrush,
            new Rect(
                0,
                floorTop,
                width,
                floorHeight));

        context.FillRectangle(
            _horizonBrush,
            new Rect(
                0,
                floorTop,
                width,
                1));

        var wave = Math.Sin(time * 0.18);
        var pulse = Math.Sin((time * 0.23) + 0.8);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (wave * 6.0),
                floorTop + 6),
            width * 0.30,
            height * 0.105,
            WithAlpha(accent, 0.20),
            12,
            0.014);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (pulse * 3.0),
                floorTop + 4),
            width * 0.17,
            height * 0.065,
            WithAlpha(accentBright, 0.18),
            10,
            0.014);
    }

    private static void DrawVignette(
        DrawingContext context,
        double width,
        double height)
    {
        var vignetteBrush =
            new SolidColorBrush(
                Color.Parse("#22000000"));

        var side = Math.Max(42.0, width * 0.075);
        var top = Math.Max(22.0, height * 0.045);
        var bottom = Math.Max(36.0, height * 0.08);

        context.FillRectangle(
            vignetteBrush,
            new Rect(0, 0, side, height));

        context.FillRectangle(
            vignetteBrush,
            new Rect(width - side, 0, side, height));

        context.FillRectangle(
            vignetteBrush,
            new Rect(0, 0, width, top));

        context.FillRectangle(
            vignetteBrush,
            new Rect(
                0,
                height - bottom,
                width,
                bottom));
    }

    private static void DrawSoftEllipse(
        DrawingContext context,
        Point center,
        double radiusX,
        double radiusY,
        Color color,
        int layers,
        double opacityPerLayer)
    {
        for (var layer = layers; layer >= 1; layer--)
        {
            var progress =
                layer / (double)layers;

            var layerRadiusX =
                radiusX * progress;

            var layerRadiusY =
                radiusY * progress;

            var centerWeight =
                1.0 - progress;

            var opacity =
                opacityPerLayer *
                (0.55 + (centerWeight * 1.45));

            var brush =
                new SolidColorBrush(
                    WithAlpha(
                        color,
                        opacity));

            context.DrawEllipse(
                brush,
                null,
                center,
                layerRadiusX,
                layerRadiusY);
        }
    }

    private Color ResolveAccentColor()
    {
        if (DataContext is MainViewModel
            {
                SelectedGame: not null
            } viewModel)
        {
            var value =
                viewModel.SelectedGame.AccentColor;

            if (Color.TryParse(value, out var parsed))
                return parsed;
        }

        return Color.Parse("#9D56E8");
    }

    private static DustParticle[] CreateParticles()
    {
        var random = new Random(4172026);
        var result =
            new DustParticle[ParticleCount];

        for (var index = 0;
             index < result.Length;
             index++)
        {
            result[index] = new DustParticle(
                StartX:
                    0.28 +
                    (random.NextDouble() * 0.44),
                StartY:
                    random.NextDouble(),
                Size:
                    0.7 +
                    (random.NextDouble() * 1.25),
                Speed:
                    0.004 +
                    (random.NextDouble() * 0.010),
                DriftAmount:
                    0.003 +
                    (random.NextDouble() * 0.008),
                DriftSpeed:
                    0.12 +
                    (random.NextDouble() * 0.20),
                Phase:
                    random.NextDouble() *
                    Math.PI *
                    2.0,
                Opacity:
                    0.05 +
                    (random.NextDouble() * 0.16));
        }

        return result;
    }

    private static Color Lighten(
        Color color,
        double amount)
    {
        byte Mix(byte component) =>
            (byte)Math.Clamp(
                component +
                ((255 - component) * amount),
                0,
                255);

        return Color.FromArgb(
            color.A,
            Mix(color.R),
            Mix(color.G),
            Mix(color.B));
    }

    private static Color WithAlpha(
        Color color,
        double alpha)
    {
        var byteAlpha =
            (byte)Math.Clamp(
                Math.Round(alpha * 255.0),
                0,
                255);

        return Color.FromArgb(
            byteAlpha,
            color.R,
            color.G,
            color.B);
    }

    private static double PositiveModulo(
        double value,
        double modulus)
    {
        var result = value % modulus;

        return result < 0
            ? result + modulus
            : result;
    }

    private void OnLoaded(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ReduceMotion)
        {
            InvalidateVisual();
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

    private void OnTick(
        object? sender,
        EventArgs e)
    {
        InvalidateVisual();
    }

    private sealed record DustParticle(
        double StartX,
        double StartY,
        double Size,
        double Speed,
        double DriftAmount,
        double DriftSpeed,
        double Phase,
        double Opacity);
}
