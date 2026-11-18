using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WBImageViewer.Models;

internal enum AnnotationTool
{
    Pointer,
    Line,
    Wave,
    DoubleLine,
    Freehand,
    Circle,
    Move,
    EraserFree,
    EraserClick
}

/// <summary>
/// One annotation stroke. Points are stored in normalized image space (0..1) so the
/// ink follows the picture when the viewport is zoomed, panned or resized.
/// Geometry is rendered as a variable-width filled ribbon with deterministic,
/// t-parameterised wobble, producing a genuine hand-drawn marker look that stays
/// stable across redraws.
/// </summary>
internal sealed class AnnotationStroke
{
    private readonly int _seed;

    public AnnotationTool Tool { get; }
    public List<Point> Points { get; }
    public Color Color { get; }
    public double Thickness { get; }

    public AnnotationStroke(AnnotationTool tool, IEnumerable<Point> points, Color color, double thickness)
    {
        Tool = tool;
        Points = points.ToList();
        Color = color;
        Thickness = thickness;
        _seed = ComputeSeed(Points);
    }

    public UIElement CreateVisual(Func<Point, Point> toViewport)
    {
        var host = new Canvas { IsHitTestVisible = false };
        foreach (var line in BuildPolylines(toViewport))
            host.Children.Add(CreateSketchStroke(line));
        return host;
    }

    public bool HitTest(Point viewportPoint, Func<Point, Point> toViewport, double radius)
    {
        foreach (var line in BuildPolylines(toViewport))
        {
            for (var i = 0; i < line.Count - 1; i++)
            {
                if (DistanceToSegment(viewportPoint, line[i], line[i + 1]) <= radius + Thickness / 2)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Render a centreline as a filled ribbon whose width breathes like a pen stroke.</summary>
    private Path CreateSketchStroke(IReadOnlyList<Point> points)
    {
        var left = new List<Point>(points.Count);
        var right = new List<Point>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var prev = points[Math.Max(0, i - 1)];
            var next = points[Math.Min(points.Count - 1, i + 1)];
            var dx = next.X - prev.X;
            var dy = next.Y - prev.Y;
            var len = Hypot(dx, dy);
            var nx = len > 0.0001 ? -dy / len : 0;
            var ny = len > 0.0001 ? dx / len : 0;

            var t = points.Count > 1 ? i / (double)(points.Count - 1) : 0;
            var taper = 0.42 + 0.58 * Math.Pow(Math.Sin(Math.PI * t), 0.38);
            var pressure = 0.84 + 0.32 * Noise(i * 3 + 7);
            var half = Math.Max(0.35, Thickness * 0.5 * taper * pressure);

            left.Add(new Point(p.X + nx * half, p.Y + ny * half));
            right.Add(new Point(p.X - nx * half, p.Y - ny * half));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(left[0], true, true);
            for (var i = 1; i < left.Count; i++) ctx.LineTo(left[i], true, true);
            for (var i = right.Count - 1; i >= 0; i--) ctx.LineTo(right[i], true, true);
        }

        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(Color),
            IsHitTestVisible = false,
            SnapsToDevicePixels = false
        };
    }

    private List<List<Point>> BuildPolylines(Func<Point, Point> toViewport)
    {
        var raw = Points.Select(toViewport).ToList();
        if (raw.Count < 2) return new List<List<Point>> { raw };

        return Tool switch
        {
            AnnotationTool.Line => new List<List<Point>> { SketchLine(raw) },
            AnnotationTool.Wave => SketchWave(raw),
            AnnotationTool.DoubleLine => SketchDouble(raw),
            AnnotationTool.Circle => SketchCircle(raw),
            _ => new List<List<Point>> { raw }
        };
    }

    /// <summary>A straight line with a faint bow, micro jitter and slightly overshot ends.</summary>
    private List<Point> SketchLine(IReadOnlyList<Point> raw)
    {
        var a = raw[0];
        var b = raw[^1];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / len;
        var ny = dx / len;
        var bow = len * 0.018 * (Noise(0) > 0.5 ? 1 : -1);
        var micro = Math.Max(0.8, len * 0.006);
        var phase = Noise(2) * Math.PI * 2;

        var result = new List<Point>();
        for (var i = 0; i <= 48; i++)
        {
            var t = i / 48.0;
            var along = -0.012 + t * 1.024;
            var wobble = Math.Sin(t * Math.PI) * bow
                       + Math.Sin(t * Math.PI * 3 + phase) * micro * 0.45
                       + (Noise(i + 1) - 0.5) * micro;
            result.Add(new Point(a.X + dx * along + nx * wobble,
                a.Y + dy * along + ny * wobble));
        }
        return result;
    }

    /// <summary>Two overlapping wavy traces, each with drifting amplitude and jitter.</summary>
    private List<List<Point>> SketchWave(IReadOnlyList<Point> raw)
    {
        var a = raw[0];
        var b = raw[^1];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / length;
        var ny = dx / length;
        var waves = Math.Max(2, Math.Min(10, length / 55));
        var amplitude = Math.Max(3, Thickness * 1.6);
        var phase = Noise(3) * Math.PI * 2;
        var gap = Math.Max(4, Thickness * 2.6);

        var passes = new List<List<Point>>();
        for (var p = 0; p < 2; p++)
        {
            var result = new List<Point>();
            var amp = amplitude * (p == 0 ? 1 : 0.78);
            var shift = p == 0 ? 0 : gap;
            for (var i = 0; i <= 64; i++)
            {
                var t = i / 64.0;
                var drift = 0.82 + 0.22 * Math.Sin(t * 9 + p * 2 + phase);
                var offset = Math.Sin(t * waves * Math.PI * 2 + phase + p * 0.4) * amp * drift
                           + (Noise(i + 10 + p * 100) - 0.5) * amp * 0.28
                           + shift;
                result.Add(new Point(a.X + dx * t + nx * offset, a.Y + dy * t + ny * offset));
            }
            passes.Add(result);
        }
        return passes;
    }

    /// <summary>Two imperfect parallel strokes instead of geometric ruler lines.</summary>
    private List<List<Point>> SketchDouble(IReadOnlyList<Point> raw)
    {
        var a = raw[0];
        var b = raw[^1];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / length;
        var ny = dx / length;
        var gap = Math.Max(4, Thickness * 2.4);
        var phase = Noise(12) * Math.PI * 2;

        var passes = new List<List<Point>>();
        for (var p = 0; p < 2; p++)
        {
            var sign = p == 0 ? 1 : -1;
            var result = new List<Point>();
            for (var i = 0; i <= 40; i++)
            {
                var t = i / 40.0;
                var wobble = sign * gap / 2
                           + Math.Sin(t * Math.PI) * sign * length * 0.008
                           + Math.Sin(t * Math.PI * 4 + phase + p) * gap * 0.12
                           + (Noise(i + 20 + p * 80) - 0.5) * Math.Max(1, Thickness * 0.7);
                result.Add(new Point(a.X + dx * t + nx * wobble, a.Y + dy * t + ny * wobble));
            }
            passes.Add(result);
        }
        return passes;
    }

    /// <summary>Two rough overlapping loops with drifting centre, radius wobble and overshoot.</summary>
    private List<List<Point>> SketchCircle(IReadOnlyList<Point> raw)
    {
        var left = raw.Min(p => p.X);
        var right = raw.Max(p => p.X);
        var top = raw.Min(p => p.Y);
        var bottom = raw.Max(p => p.Y);
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var rx = Math.Max(2, (right - left) / 2);
        var ry = Math.Max(2, (bottom - top) / 2);
        var phase = Noise(5) * Math.PI * 2;
        var passes = new List<List<Point>>();

        for (var p = 0; p < 2; p++)
        {
            var loop = new List<Point>();
            var start = -0.32 + p * 0.58 + phase * 0.06;
            var sweep = Math.PI * 2 + (p == 0 ? 0.4 : -0.3);
            var driftX = (Noise(30 + p) - 0.5) * rx * 0.07;
            var driftY = (Noise(40 + p) - 0.5) * ry * 0.07;
            for (var i = 0; i <= 88; i++)
            {
                var t = i / 88.0;
                var angle = start + sweep * t;
                var wobble = 1
                           + 0.055 * Math.Sin(angle * 3 + phase)
                           + 0.028 * Math.Sin(angle * 7 + p * 1.7)
                           + (Noise(i + 50 + p * 120) - 0.5) * 0.02;
                loop.Add(new Point(
                    cx + driftX + Math.Cos(angle) * rx * wobble,
                    cy + driftY + Math.Sin(angle) * ry * wobble));
            }
            passes.Add(loop);
        }
        return passes;
    }

    private static int ComputeSeed(IReadOnlyList<Point> points)
    {
        unchecked
        {
            var hash = 17;
            foreach (var p in points)
            {
                hash = hash * 31 + p.X.GetHashCode();
                hash = hash * 31 + p.Y.GetHashCode();
            }
            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>Deterministic pseudo-random value in [0,1); based on index only, never coordinates.</summary>
    private double Noise(int i)
    {
        unchecked
        {
            uint h = (uint)(_seed ^ (i * 0x9E3779B9));
            h ^= h >> 16;
            h *= 0x7FEB352D;
            h ^= h >> 15;
            h *= 0x846CA68B;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535.0;
        }
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var lengthSq = vx * vx + vy * vy;
        if (lengthSq < 0.0001) return Hypot(p.X - a.X, p.Y - a.Y);
        var t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lengthSq, 0, 1);
        return Hypot(p.X - (a.X + t * vx), p.Y - (a.Y + t * vy));
    }
}
