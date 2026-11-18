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
    EraserFree,
    EraserClick
}

/// <summary>
/// One annotation stroke. Points are stored in normalized image space (0..1) so the
/// ink follows the picture when the viewport is zoomed, panned or resized.
/// </summary>
internal sealed class AnnotationStroke
{
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
    }

    public UIElement CreateVisual(Func<Point, Point> toViewport)
    {
        if (Tool == AnnotationTool.DoubleLine)
        {
            var host = new Canvas { IsHitTestVisible = false };
            foreach (var line in BuildPolylines(toViewport))
                host.Children.Add(CreatePolyline(line));
            return host;
        }

        return CreatePolyline(BuildPolylines(toViewport).First());
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

    private Polyline CreatePolyline(IReadOnlyList<Point> points)
    {
        var shape = new Polyline
        {
            Stroke = new SolidColorBrush(Color),
            StrokeThickness = Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false
        };
        foreach (var point in points) shape.Points.Add(point);
        return shape;
    }

    private List<List<Point>> BuildPolylines(Func<Point, Point> toViewport)
    {
        var raw = Points.Select(toViewport).ToList();
        if (raw.Count < 2) return new List<List<Point>> { raw };

        return Tool switch
        {
            AnnotationTool.Wave => new List<List<Point>> { BuildWave(raw) },
            AnnotationTool.DoubleLine => BuildDouble(raw),
            AnnotationTool.Circle => new List<List<Point>> { BuildCircle(raw) },
            _ => new List<List<Point>> { raw }
        };
    }

    private List<Point> BuildWave(IReadOnlyList<Point> raw)
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
        var result = new List<Point>();
        for (var i = 0; i <= 64; i++)
        {
            var t = i / 64.0;
            var offset = Math.Sin(t * waves * Math.PI * 2) * amplitude;
            result.Add(new Point(a.X + dx * t + nx * offset, a.Y + dy * t + ny * offset));
        }
        return result;
    }

    private List<List<Point>> BuildDouble(IReadOnlyList<Point> raw)
    {
        var a = raw[0];
        var b = raw[^1];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Max(1, Hypot(dx, dy));
        var gap = Math.Max(3, Thickness * 2.2);
        var nx = -dy / length * gap / 2;
        var ny = dx / length * gap / 2;
        return new List<List<Point>>
        {
            new() { new Point(a.X + nx, a.Y + ny), new Point(b.X + nx, b.Y + ny) },
            new() { new Point(a.X - nx, a.Y - ny), new Point(b.X - nx, b.Y - ny) }
        };
    }

    private List<Point> BuildCircle(IReadOnlyList<Point> raw)
    {
        var left = raw.Min(p => p.X);
        var right = raw.Max(p => p.X);
        var top = raw.Min(p => p.Y);
        var bottom = raw.Max(p => p.Y);
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var rx = Math.Max(2, (right - left) / 2);
        var ry = Math.Max(2, (bottom - top) / 2);
        var result = new List<Point>();
        for (var i = 0; i <= 48; i++)
        {
            var angle = i / 48.0 * Math.PI * 2;
            var wobble = 1 + 0.035 * Math.Sin(angle * 5 + 0.7);
            result.Add(new Point(cx + Math.Cos(angle) * rx * wobble,
                cy + Math.Sin(angle) * ry * wobble));
        }
        return result;
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
