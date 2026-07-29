using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Controls;

/// <summary>
/// OLED-first CRT studio renderer.
///
/// The base canvas is literal black. Atmosphere, phosphor glow, scanlines,
/// mask texture, dust, grain, and floor reflection are drawn only where
/// needed so large OLED regions remain fully unlit.
/// </summary>
public sealed class StudioRenderer : Control
{
    private const int TargetFrameMilliseconds = 40;
    private const int ParticleCount = 34;
    private const int GrainPointCount = 150;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly DustParticle[] _particles;
    private readonly GrainPoint[] _grainPoints;

    private readonly SolidColorBrush _blackBrush =
        new(Color.Parse("#000000"));

    private readonly SolidColorBrush _horizonBrush =
        new(Color.Parse("#20FFFFFF"));

    private readonly SolidColorBrush _dustBrush =
        new(Color.Parse("#D0FFFFFF"));

    private readonly SolidColorBrush _scanlineBrush =
        new(Color.Parse("#12000000"));

    private readonly SolidColorBrush _grainBrush =
        new(Color.Parse("#78FFFFFF"));

    private readonly SolidColorBrush _redMaskBrush =
        new(Color.Parse("#08FF4050"));

    private readonly SolidColorBrush _greenMaskBrush =
        new(Color.Parse("#0840FF70"));

    private readonly SolidColorBrush _blueMaskBrush =
        new(Color.Parse("#084080FF"));

    public StudioRenderer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _particles = CreateParticles();
        _grainPoints = CreateGrainPoints();

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

            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
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
        var accentBright = Lighten(accent, 0.32);

        DrawTrueBlackBase(context, width, height);

        DrawLocalizedPhosphorBloom(
            context,
            width,
            height,
            time,
            accent,
            accentBright);

        DrawAtmosphere(
            context,
            width,
            height,
            time,
            accent);

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

        DrawCrtMask(context, width, height);
        DrawScanlines(context, width, height);
        DrawFilmGrain(context, width, height, time);
        DrawVignette(context, width, height);
    }

    private void DrawTrueBlackBase(
        DrawingContext context,
        double width,
        double height)
    {
        context.FillRectangle(
            _blackBrush,
            new Rect(0, 0, width, height));
    }

    private static void DrawLocalizedPhosphorBloom(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color accent,
        Color accentBright)
    {
        var drift = Math.Sin(time * 0.15);
        var pulse = Math.Sin((time * 0.21) + 0.9);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (drift * 4.0),
                height * 0.235),
            width * 0.19,
            height * 0.17,
            WithAlpha(accent, 0.20),
            14,
            0.010);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (pulse * 2.0),
                height * 0.235),
            width * 0.105,
            height * 0.085,
            WithAlpha(accentBright, 0.16),
            10,
            0.010);
    }

    private static void DrawAtmosphere(
        DrawingContext context,
        double width,
        double height,
        double time,
        Color accent)
    {
        var left = Math.Sin((time * 0.10) + 0.4);
        var right = Math.Sin((time * 0.09) + 1.5);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.36) + (left * 18.0),
                height * 0.45),
            width * 0.21,
            height * 0.105,
            WithAlpha(accent, 0.055),
            9,
            0.006);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.66) + (right * 21.0),
                height * 0.55),
            width * 0.24,
            height * 0.115,
            WithAlpha(accent, 0.045),
            9,
            0.005);
    }

    private void DrawDust(
        DrawingContext context,
        double width,
        double height,
        double time)
    {
        foreach (var particle in _particles)
        {
            var normalizedY = PositiveModulo(
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

            var y = normalizedY * height * 0.74;

            var fade = Math.Sin(normalizedY * Math.PI);

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

        // The floor remains true black; only the horizon and localized
        // reflections illuminate pixels.
        context.FillRectangle(
            _horizonBrush,
            new Rect(0, floorTop, width, 1));

        var drift = Math.Sin(time * 0.15);
        var pulse = Math.Sin((time * 0.21) + 0.9);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (drift * 5.0),
                floorTop + 3),
            width * 0.29,
            height * 0.075,
            WithAlpha(accent, 0.16),
            13,
            0.010);

        DrawSoftEllipse(
            context,
            new Point(
                (width * 0.50) + (pulse * 2.0),
                floorTop + 2),
            width * 0.14,
            height * 0.040,
            WithAlpha(accentBright, 0.15),
            10,
            0.010);
    }

    private void DrawScanlines(
        DrawingContext context,
        double width,
        double height)
    {
        const double spacing = 4.0;

        for (var y = 1.0; y < height; y += spacing)
        {
            context.FillRectangle(
                _scanlineBrush,
                new Rect(0, y, width, 1));
        }
    }

    private void DrawCrtMask(
        DrawingContext context,
        double width,
        double height)
    {
        // Extremely faint RGB phosphor triads. They should add character
        // only at close viewing distance, not tint the image.
        const double triadWidth = 9.0;

        using (context.PushOpacity(0.16))
        {
            for (var x = 0.0; x < width; x += triadWidth)
            {
                context.FillRectangle(
                    _redMaskBrush,
                    new Rect(x, 0, 1, height));

                context.FillRectangle(
                    _greenMaskBrush,
                    new Rect(x + 3, 0, 1, height));

                context.FillRectangle(
                    _blueMaskBrush,
                    new Rect(x + 6, 0, 1, height));
            }
        }
    }

    private void DrawFilmGrain(
        DrawingContext context,
        double width,
        double height,
        double time)
    {
        var frameOffset =
            ReduceMotion
                ? 0
                : (int)(time * 12.0);

        foreach (var point in _grainPoints)
        {
            var x = PositiveModulo(
                point.X + (frameOffset * point.StepX),
                1.0) * width;

            var y = PositiveModulo(
                point.Y + (frameOffset * point.StepY),
                1.0) * height;

            using (context.PushOpacity(point.Opacity))
            {
                context.FillRectangle(
                    _grainBrush,
                    new Rect(
                        x,
                        y,
                        point.Size,
                        point.Size));
            }
        }
    }

    private static void DrawVignette(
        DrawingContext context,
        double width,
        double height)
    {
        var brush =
            new SolidColorBrush(
                Color.Parse("#4A000000"));

        var side = Math.Max(48.0, width * 0.085);
        var top = Math.Max(26.0, height * 0.050);
        var bottom = Math.Max(42.0, height * 0.085);

        context.FillRectangle(
            brush,
            new Rect(0, 0, side, height));

        context.FillRectangle(
            brush,
            new Rect(width - side, 0, side, height));

        context.FillRectangle(
            brush,
            new Rect(0, 0, width, top));

        context.FillRectangle(
            brush,
            new Rect(0, height - bottom, width, bottom));
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

            var centerWeight =
                1.0 - progress;

            var opacity =
                opacityPerLayer *
                (0.45 + (centerWeight * 1.55));

            var brush =
                new SolidColorBrush(
                    WithAlpha(color, opacity));

            context.DrawEllipse(
                brush,
                null,
                center,
                radiusX * progress,
                radiusY * progress);
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
        var result = new DustParticle[ParticleCount];

        for (var index = 0;
             index < result.Length;
             index++)
        {
            result[index] = new DustParticle(
                StartX:
                    0.30 +
                    (random.NextDouble() * 0.40),
                StartY:
                    random.NextDouble(),
                Size:
                    0.55 +
                    (random.NextDouble() * 0.90),
                Speed:
                    0.003 +
                    (random.NextDouble() * 0.008),
                DriftAmount:
                    0.002 +
                    (random.NextDouble() * 0.006),
                DriftSpeed:
                    0.10 +
                    (random.NextDouble() * 0.17),
                Phase:
                    random.NextDouble() *
                    Math.PI *
                    2.0,
                Opacity:
                    0.035 +
                    (random.NextDouble() * 0.105));
        }

        return result;
    }

    private static GrainPoint[] CreateGrainPoints()
    {
        var random = new Random(902104);
        var result = new GrainPoint[GrainPointCount];

        for (var index = 0;
             index < result.Length;
             index++)
        {
            result[index] = new GrainPoint(
                X: random.NextDouble(),
                Y: random.NextDouble(),
                Size:
                    0.35 +
                    (random.NextDouble() * 0.80),
                Opacity:
                    0.006 +
                    (random.NextDouble() * 0.018),
                StepX:
                    0.0007 +
                    (random.NextDouble() * 0.0020),
                StepY:
                    0.0005 +
                    (random.NextDouble() * 0.0016));
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
        return Color.FromArgb(
            (byte)Math.Clamp(
                Math.Round(alpha * 255.0),
                0,
                255),
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

    private sealed record GrainPoint(
        double X,
        double Y,
        double Size,
        double Opacity,
        double StepX,
        double StepY);
}
