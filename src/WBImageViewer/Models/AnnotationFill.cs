using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace WBImageViewer.Models;

/// <summary>
/// Paint-bucket fill. Stores the seed point in normalized image space plus the fill
/// colour and flood tolerance; the actual filled region is computed against the source
/// pixels and cached as a transparent bitmap. Seed-based storage keeps it editable and
/// serializable in the .gjpg project format.
/// </summary>
internal sealed class AnnotationFill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Point Seed { get; set; }
    public Color Color { get; set; }
    public double Opacity { get; set; } = 0.45;
    public double Tolerance { get; set; } = 40;

    private WriteableBitmap? _cache;
    private int[]? _cachePixels;
    private int _cacheW;
    private int _cacheH;

    public UIElement? CreateVisual(bool selected = false, Point? seedViewport = null)
    {
        if (_cache == null) return null;
        var host = new Canvas { IsHitTestVisible = false };
        var image = new System.Windows.Controls.Image
        {
            Source = _cache,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        host.Children.Add(image);
        if (selected)
        {
            var marker = new Ellipse
            {
                Width = 15, Height = 15,
                Fill = new SolidColorBrush(Color.FromArgb(90, 108, 182, 255)),
                Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false
            };
            Canvas.SetLeft(marker, (seedViewport?.X ?? 0) - 7.5);
            Canvas.SetTop(marker, (seedViewport?.Y ?? 0) - 7.5);
            host.Children.Add(marker);
        }
        return host;
    }

    public bool HitTest(Point viewportPoint, Func<Point, Point> seedToViewport, double radius)
    {
        var p = seedToViewport(Seed);
        return System.Math.Abs(p.X - viewportPoint.X) <= radius + 12 &&
               System.Math.Abs(p.Y - viewportPoint.Y) <= radius + 12;
    }

    /// <summary>Flood fill from the seed over the source pixels, producing a transparent overlay.</summary>
    public void Build(int[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height) return;
        _cacheW = width;
        _cacheH = height;

        var sx = Math.Clamp((int)(Seed.X * width), 0, width - 1);
        var sy = Math.Clamp((int)(Seed.Y * height), 0, height - 1);
        var target = pixels[sy * width + sx];

        var tr = (byte)(target >> 16 & 0xFF);
        var tg = (byte)(target >> 8 & 0xFF);
        var tb = (byte)(target & 0xFF);
        var visited = new bool[width * height];
        var mask = new bool[width * height];
        var stack = new Stack<int>();
        var start = sy * width + sx;
        stack.Push(start);
        visited[start] = true;
        var tol2 = Tolerance * Tolerance * 3;

        while (stack.Count > 0)
        {
            var idx = stack.Pop();
            var c = pixels[idx];
            var r = (byte)(c >> 16 & 0xFF);
            var g = (byte)(c >> 8 & 0xFF);
            var b = (byte)(c & 0xFF);
            var dr = r - tr;
            var dg = g - tg;
            var db = b - tb;
            if (dr * dr + dg * dg + db * db > tol2) continue;

            mask[idx] = true;
            var x = idx % width;
            var y = idx / width;
            if (x > 0 && !visited[idx - 1]) { visited[idx - 1] = true; stack.Push(idx - 1); }
            if (x < width - 1 && !visited[idx + 1]) { visited[idx + 1] = true; stack.Push(idx + 1); }
            if (y > 0 && !visited[idx - width]) { visited[idx - width] = true; stack.Push(idx - width); }
            if (y < height - 1 && !visited[idx + width]) { visited[idx + width] = true; stack.Push(idx + width); }
        }

        _cachePixels = new int[width * height];
        var fr = (byte)Math.Clamp((int)(Color.R * Opacity), 0, 255);
        var fg = (byte)Math.Clamp((int)(Color.G * Opacity), 0, 255);
        var fb = (byte)Math.Clamp((int)(Color.B * Opacity), 0, 255);
        var fa = (byte)Math.Clamp((int)(255 * Opacity), 0, 255);
        var fillArgb = fa << 24 | fr << 16 | fg << 8 | fb;
        for (var i = 0; i < mask.Length; i++)
            if (mask[i]) _cachePixels[i] = fillArgb;

        _cache = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _cache.WritePixels(new Int32Rect(0, 0, width, height), _cachePixels, width * 4, 0);
    }

    public void Invalidate() => _cache = null;
}

/// <summary>
/// A text annotation produced by translation or OCR on a selected image region.
/// Position/size are normalized image space so it tracks zoom and pan like ink.
/// </summary>
internal sealed class AnnotationText
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Point TopLeft { get; set; }
    public double Width { get; set; } = 0.3;
    public string Source { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Kind { get; set; } = "translate"; // translate | ocr
    public double Opacity { get; set; } = 1.0;

    public UIElement CreateVisual(Func<Point, Point> toViewport, double viewWidth, bool selected, double scale = 1)
    {
        var tl = toViewport(TopLeft);
        var s = Math.Clamp(scale, 0.35, 4);
        var host = new Canvas { IsHitTestVisible = false };
        // Selection is expressed by the card's own accent border, not by an outer frame.
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 18, 22, 30)),
            BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(180, 215, 255) : Color.FromRgb(90, 160, 230)),
            BorderThickness = new Thickness(selected ? 1.6 : 1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7 * s, 5 * s, 7 * s, 5 * s),
            Opacity = Opacity,
            IsHitTestVisible = false
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Kind == "ocr" ? "文字识别" : "翻译",
            FontSize = 9 * s,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 190, 255))
        });
        stack.Children.Add(new TextBlock
        {
            Text = Text,
            FontSize = 12 * s,
            Foreground = System.Windows.Media.Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(80, viewWidth * Width - 20 * s)
        });
        border.Child = stack;
        Canvas.SetLeft(border, tl.X);
        Canvas.SetTop(border, tl.Y);
        host.Children.Add(border);
        return host;
    }

    public bool HitTest(Point viewportPoint, Func<Point, Point> toViewport, double viewWidth, double viewHeight, double radius)
    {
        var tl = toViewport(TopLeft);
        var w = Math.Max(80, viewWidth * Width);
        var h = 52;
        return viewportPoint.X >= tl.X - radius && viewportPoint.X <= tl.X + w + radius &&
               viewportPoint.Y >= tl.Y - radius && viewportPoint.Y <= tl.Y + h + radius;
    }
}
