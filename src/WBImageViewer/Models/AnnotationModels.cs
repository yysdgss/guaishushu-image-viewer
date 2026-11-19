using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WBImageViewer.Models;

internal enum AnnotationTool
{
    Pointer,
    Line,
    Wave,
    DoubleLine,
    Freehand,
    Circle,
    Fill,
    Move,
    EraserFree,
    EraserClick
}

/// <summary>Standard = smooth geometric line; HandDrawn = stable marker-like ribbon.</summary>
internal enum InkStrokeStyle
{
    Standard,
    HandDrawn
}

/// <summary>
/// One annotation stroke. Geometry is stored in normalized image space (0..1) and the
/// brush width in image pixels, so zooming, panning or resizing the window keeps every
/// trace pinned to the exact same place and relative size on the picture.
/// The deterministic wobble is generated in image-pixel space with a fixed seed, so
/// it never "jumps" while a stroke is being drawn or redrawn at another zoom level.
/// </summary>
internal sealed class AnnotationStroke
{
    private int _seed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public AnnotationTool Tool { get; set; }
    public List<Point> Points { get; }
    public Color Color { get; set; }
    /// <summary>Brush width in source-image pixels (not screen pixels).</summary>
    public double Thickness { get; set; }
    public InkStrokeStyle Style { get; set; } = InkStrokeStyle.HandDrawn;

    public double Opacity { get; set; } = 1.0;
    public bool Highlighter { get; set; }

    public int Seed
    {
        get => _seed;
        set => _seed = value == 0 ? 1 : value;
    }

    public AnnotationStroke(AnnotationTool tool, IEnumerable<Point> points, Color color, double thickness,
        InkStrokeStyle style = InkStrokeStyle.HandDrawn, int? seed = null)
    {
        Tool = tool;
        Points = points.ToList();
        Color = color;
        Thickness = Math.Max(0.25, thickness);
        Style = style;
        _seed = seed ?? ComputeSeed(Points);
    }

    public void Reseed() => _seed = ComputeSeed(Points);

    public UIElement CreateVisual(Func<Point, Point> toViewport, bool selected = false,
        double viewScale = 1, double imageWidth = 1, double imageHeight = 1)
    {
        var host = new Canvas { IsHitTestVisible = false };
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * Math.Clamp(Opacity, 0.05, 1)), 12, (byte)255);
        var ink = Color.FromArgb(alpha, Color.R, Color.G, Color.B);
        var width = Math.Max(0.6, Thickness * viewScale * (Highlighter ? 2.6 : 1));
        var lines = BuildPolylines(imageWidth, imageHeight)
            .Select(line => line.Select(toViewport).ToList()).ToList();

        if (selected)
        {
            // A restrained under-glow is enough to identify the active stroke - no
            // bounding rectangle, which used to obscure the drawing underneath.
            foreach (var line in lines)
                host.Children.Add(CreateStandardStroke(line,
                    Color.FromArgb(62, 108, 182, 255), width + 3));
        }

        foreach (var line in lines)
        {
            host.Children.Add(Style == InkStrokeStyle.Standard
                ? CreateStandardStroke(line, ink, width)
                : CreateSketchStroke(line, ink, width));
        }

        if (selected)
        {
            foreach (var handle in GetHandlePoints(toViewport))
            {
                var dot = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(92, 166, 255)),
                    StrokeThickness = 2,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, handle.X - 5);
                Canvas.SetTop(dot, handle.Y - 5);
                host.Children.Add(dot);
            }
        }
        return host;
    }

    public bool HitTest(Point viewportPoint, Func<Point, Point> toViewport, double radius,
        double viewScale = 1, double imageWidth = 1, double imageHeight = 1)
    {
        var width = Math.Max(0.6, Thickness * viewScale * (Highlighter ? 2.6 : 1));
        foreach (var line in BuildPolylines(imageWidth, imageHeight))
        {
            var screen = line.Select(toViewport).ToList();
            for (var i = 0; i < screen.Count - 1; i++)
                if (DistanceToSegment(viewportPoint, screen[i], screen[i + 1]) <= radius + width / 2)
                    return true;
        }
        return false;
    }

    public List<(int Index, Point Value)> GetHandles()
    {
        if (Points.Count < 2) return new List<(int, Point)>();
        if (Tool is AnnotationTool.Line or AnnotationTool.Wave or AnnotationTool.DoubleLine)
            return new List<(int, Point)> { (0, Points[0]), (Points.Count - 1, Points[^1]) };
        if (Tool is AnnotationTool.Circle or AnnotationTool.Freehand)
        {
            // Two diagonal handles scale the element's width and height independently.
            var l = Points.Min(p => p.X);
            var r = Points.Max(p => p.X);
            var t = Points.Min(p => p.Y);
            var b = Points.Max(p => p.Y);
            return new List<(int, Point)> { (0, new Point(l, t)), (1, new Point(r, b)) };
        }
        return new List<(int, Point)>();
    }

    /// <summary>Handle indices are logical (0 / 1), not point indices. Snapshot the real
    /// value when a drag starts so the editor can apply a delta without mixing spaces.</summary>
    public Point GetHandleValue(int handleIndex)
    {
        var handles = GetHandles();
        return handleIndex >= 0 && handleIndex < handles.Count ? handles[handleIndex].Value : new Point(0, 0);
    }

    private List<Point> GetHandlePoints(Func<Point, Point> toViewport) =>
        GetHandles().Select(h => toViewport(h.Value)).ToList();

    public bool ApplyHandle(int handleIndex, Point target, Point originalBoundsTopLeft, Point originalBoundsBottomRight)
    {
        if (Tool is AnnotationTool.Line or AnnotationTool.Wave or AnnotationTool.DoubleLine)
        {
            var idx = handleIndex == 0 ? 0 : Points.Count - 1;
            Points[idx] = new Point(Math.Clamp(target.X, -0.5, 1.5), Math.Clamp(target.Y, -0.5, 1.5));
            return true;
        }
        if (Tool is (AnnotationTool.Circle or AnnotationTool.Freehand) && Points.Count > 0)
        {
            var l = originalBoundsTopLeft.X;
            var t = originalBoundsTopLeft.Y;
            var r = originalBoundsBottomRight.X;
            var b = originalBoundsBottomRight.Y;
            var w = Math.Max(0.01, r - l);
            var h = Math.Max(0.01, b - t);
            for (var i = 0; i < Points.Count; i++)
            {
                var nx = (Points[i].X - l) / w;
                var ny = (Points[i].Y - t) / h;
                var tl = handleIndex == 0 ? target : new Point(l, t);
                var br = handleIndex == 0 ? new Point(r, b) : target;
                var nw = Math.Max(0.01, br.X - tl.X);
                var nh = Math.Max(0.01, br.Y - tl.Y);
                Points[i] = new Point(tl.X + nx * nw, tl.Y + ny * nh);
            }
            return true;
        }
        return false;
    }

    public static bool CanChangeTo(AnnotationTool from, AnnotationTool to) =>
        to is AnnotationTool.Line or AnnotationTool.Wave or AnnotationTool.DoubleLine &&
        from is AnnotationTool.Line or AnnotationTool.Wave or AnnotationTool.DoubleLine;

    // ---------------- geometry, generated in image-pixel space ----------------

    private List<List<Point>> BuildPolylines(double imageWidth, double imageHeight)
    {
        var w = Math.Max(1, imageWidth);
        var h = Math.Max(1, imageHeight);
        if (Points.Count < 2) return new List<List<Point>> { Points.ToList() };

        return Tool switch
        {
            AnnotationTool.Line => new List<List<Point>> { PathLine(w, h) },
            AnnotationTool.Wave => PathWave(w, h),
            AnnotationTool.DoubleLine => PathDouble(w, h),
            AnnotationTool.Circle => PathCircle(w, h),
            _ => new List<List<Point>> { Points.ToList() }
        };
    }

    private static Point ToPx(Point p, double w, double h) => new(p.X * w, p.Y * h);
    private static Point ToNorm(Point p, double w, double h) => new(p.X / w, p.Y / h);

    private List<Point> PathLine(double w, double h)
    {
        var a = ToPx(Points[0], w, h);
        var b = ToPx(Points[^1], w, h);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / len;
        var ny = dx / len;
        var result = new List<Point>();
        for (var i = 0; i <= 32; i++)
        {
            var t = i / 32.0;
            double offset = 0;
            if (Style == InkStrokeStyle.HandDrawn)
            {
                var bow = len * 0.012 * (Noise(0) > 0.5 ? 1 : -1);
                var micro = Math.Max(0.5, Thickness * 0.55);
                offset = Math.Sin(t * Math.PI) * bow
                       + Math.Sin(t * Math.PI * 3 + Noise(2) * Math.PI * 2) * micro * 0.35
                       + (Noise(i + 1) - 0.5) * micro * 0.5;
            }
            result.Add(ToNorm(new Point(a.X + dx * t + nx * offset,
                a.Y + dy * t + ny * offset), w, h));
        }
        return result;
    }

    private List<List<Point>> PathWave(double w, double h)
    {
        var a = ToPx(Points[0], w, h);
        var b = ToPx(Points[^1], w, h);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / len;
        var ny = dx / len;
        var waves = Math.Max(2, Math.Min(12, len / 60));
        var amp = Math.Max(2, Thickness * 2.0);
        var phase = Style == InkStrokeStyle.Standard ? 0 : Noise(3) * Math.PI * 2;
        var passes = Style == InkStrokeStyle.Standard ? 1 : 2;
        var result = new List<List<Point>>();
        for (var p = 0; p < passes; p++)
        {
            var line = new List<Point>();
            for (var i = 0; i <= 72; i++)
            {
                var t = i / 72.0;
                var jitter = Style == InkStrokeStyle.Standard ? 0
                    : (Noise(i + 10 + p * 100) - 0.5) * amp * 0.22;
                var offset = Math.Sin(t * waves * Math.PI * 2 + phase + p * 0.4) * amp + jitter
                           + (p == 0 ? 0 : amp * 1.1);
                line.Add(ToNorm(new Point(a.X + dx * t + nx * offset,
                    a.Y + dy * t + ny * offset), w, h));
            }
            result.Add(line);
        }
        return result;
    }

    private List<List<Point>> PathDouble(double w, double h)
    {
        var a = ToPx(Points[0], w, h);
        var b = ToPx(Points[^1], w, h);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Max(1, Hypot(dx, dy));
        var nx = -dy / len;
        var ny = dx / len;
        var gap = Math.Max(3, Thickness * 2.6);
        var phase = Noise(12) * Math.PI * 2;
        var result = new List<List<Point>>();
        for (var p = 0; p < 2; p++)
        {
            var sign = p == 0 ? 1 : -1;
            var line = new List<Point>();
            for (var i = 0; i <= 40; i++)
            {
                var t = i / 40.0;
                var jitter = Style == InkStrokeStyle.Standard ? 0
                    : Math.Sin(t * Math.PI * 4 + phase + p) * gap * 0.08
                      + (Noise(i + 20 + p * 80) - 0.5) * Math.Max(0.6, Thickness * 0.5);
                var offset = sign * gap / 2 + jitter;
                line.Add(ToNorm(new Point(a.X + dx * t + nx * offset,
                    a.Y + dy * t + ny * offset), w, h));
            }
            result.Add(line);
        }
        return result;
    }

    private List<List<Point>> PathCircle(double w, double h)
    {
        var raw = Points.Select(p => ToPx(p, w, h)).ToList();
        var left = raw.Min(p => p.X);
        var right = raw.Max(p => p.X);
        var top = raw.Min(p => p.Y);
        var bottom = raw.Max(p => p.Y);
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var rx = Math.Max(2, (right - left) / 2);
        var ry = Math.Max(2, (bottom - top) / 2);
        var hand = Style == InkStrokeStyle.HandDrawn;
        var phase = hand ? Noise(5) * Math.PI * 2 : 0;
        var passes = hand ? 2 : 1;
        var result = new List<List<Point>>();
        for (var p = 0; p < passes; p++)
        {
            var loop = new List<Point>();
            var start = hand ? -0.28 + p * 0.52 + phase * 0.04 : 0;
            var sweep = Math.PI * 2 + (hand ? (p == 0 ? 0.32 : -0.24) : 0);
            for (var i = 0; i <= 88; i++)
            {
                var t = i / 88.0;
                var angle = start + sweep * t;
                var wobble = hand
                    ? 1 + 0.045 * Math.Sin(angle * 3 + phase)
                      + 0.02 * Math.Sin(angle * 7 + p * 1.7)
                      + (Noise(i + 50 + p * 120) - 0.5) * 0.012
                    : 1;
                loop.Add(ToNorm(new Point(cx + Math.Cos(angle) * rx * wobble,
                    cy + Math.Sin(angle) * ry * wobble), w, h));
            }
            result.Add(loop);
        }
        return result;
    }

    // ---------------- rendering ----------------

    private static Path CreateStandardStroke(IReadOnlyList<Point> points, Color ink, double width)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count > 0)
            {
                ctx.BeginFigure(points[0], false, false);
                if (points.Count == 2)
                {
                    ctx.LineTo(points[1], true, true);
                }
                else
                {
                    // Quadratic midpoint smoothing keeps standard lines smooth and stable.
                    for (var i = 1; i < points.Count - 1; i++)
                    {
                        var mid = new Point((points[i].X + points[i + 1].X) / 2,
                            (points[i].Y + points[i + 1].Y) / 2);
                        ctx.QuadraticBezierTo(points[i], mid, true, true);
                    }
                    ctx.LineTo(points[^1], true, true);
                }
            }
        }
        return new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(ink),
            StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false
        };
    }

    private Path CreateSketchStroke(IReadOnlyList<Point> points, Color ink, double width)
    {
        if (points.Count < 2) return CreateStandardStroke(points, ink, width);
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
            var t = i / (double)(points.Count - 1);
            var taper = Highlighter ? 1 : 0.55 + 0.45 * Math.Pow(Math.Sin(Math.PI * t), 0.4);
            var pressure = 0.88 + 0.22 * Noise(i * 3 + 7);
            var half = Math.Max(0.35, width * 0.5 * taper * pressure);
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
            Fill = new SolidColorBrush(ink),
            IsHitTestVisible = false,
            SnapsToDevicePixels = false
        };
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
        var lenSq = vx * vx + vy * vy;
        if (lenSq < 0.0001) return Hypot(p.X - a.X, p.Y - a.Y);
        var t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq, 0, 1);
        return Hypot(p.X - (a.X + t * vx), p.Y - (a.Y + t * vy));
    }
}
