using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WBImageViewer.Models;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace WBImageViewer.Services;

/// <summary>
/// .gjpg — a Guaishushu-viewer annotated picture container. Physically it is a JSON
/// document (UTF-8) that embeds the original bitmap as base64 PNG together with every
/// editable trace: ink strokes, paint-bucket fills, translation / OCR text cards and
/// adjustment settings. Reopening a .gjpg restores all of them as live, editable objects.
/// </summary>
internal static class GjpgService
{
    public const string Extension = ".gjpg";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class Document
    {
        public string Format { get; set; } = "GJPG";
        public int Version { get; set; } = 1;
        public string ImageName { get; set; } = "image.png";
        public string ImageBase64 { get; set; } = "";
        public List<StrokeDto> Annotations { get; set; } = new();
        public List<FillDto> Fills { get; set; } = new();
        public List<TextDto> Texts { get; set; } = new();
        public ImageAdjustments? Adjustments { get; set; }
    }

    private sealed class StrokeDto
    {
        public string Tool { get; set; } = "";
        public List<double[]> Points { get; set; } = new();
        public uint Color { get; set; }
        public double Thickness { get; set; }
        public double Opacity { get; set; } = 1;
        public bool Highlighter { get; set; }
        public string Style { get; set; } = "HandDrawn";
    }

    private sealed class FillDto
    {
        public double[] Seed { get; set; } = new double[2];
        public uint Color { get; set; }
        public double Opacity { get; set; }
        public double Tolerance { get; set; }
    }

    private sealed class TextDto
    {
        public double[] TopLeft { get; set; } = new double[2];
        public double Width { get; set; }
        public string Source { get; set; } = "";
        public string Text { get; set; } = "";
        public string Kind { get; set; } = "";
        public double Opacity { get; set; } = 1;
    }

    public static void Save(string path, BitmapSource source, string imageName,
        IReadOnlyList<AnnotationStroke> strokes, IReadOnlyList<AnnotationFill> fills,
        IReadOnlyList<AnnotationText> texts, ImageAdjustments adjustments)
    {
        var doc = new Document
        {
            ImageName = imageName,
            ImageBase64 = EncodePng(source),
            Adjustments = adjustments.Clone(),
            Annotations = strokes.Select(s => new StrokeDto
            {
                Tool = s.Tool.ToString(),
                Points = s.Points.Select(p => new[] { p.X, p.Y }).ToList(),
                Color = ToArgb(s.Color),
                Thickness = s.Thickness,
                Opacity = s.Opacity,
                Highlighter = s.Highlighter,
                Style = s.Style.ToString()
            }).ToList(),
            Fills = fills.Select(f => new FillDto
            {
                Seed = new[] { f.Seed.X, f.Seed.Y },
                Color = ToArgb(f.Color),
                Opacity = f.Opacity,
                Tolerance = f.Tolerance
            }).ToList(),
            Texts = texts.Select(t => new TextDto
            {
                TopLeft = new[] { t.TopLeft.X, t.TopLeft.Y },
                Width = t.Width,
                Source = t.Source,
                Text = t.Text,
                Kind = t.Kind,
                Opacity = t.Opacity
            }).ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options));
    }

    public static bool TryLoad(string path, out BitmapSource image, out string imageName,
        out List<AnnotationStroke> strokes, out List<AnnotationFill> fills,
        out List<AnnotationText> texts, out ImageAdjustments adjustments)
    {
        image = null!;
        imageName = "";
        strokes = new List<AnnotationStroke>();
        fills = new List<AnnotationFill>();
        texts = new List<AnnotationText>();
        adjustments = new ImageAdjustments();
        try
        {
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (doc == null || doc.Format != "GJPG") return false;
            image = DecodePng(doc.ImageBase64);
            imageName = doc.ImageName;
            adjustments = doc.Adjustments ?? new ImageAdjustments();

            foreach (var s in doc.Annotations)
            {
                if (!Enum.TryParse<AnnotationTool>(s.Tool, out var tool)) continue;
                var pts = s.Points.Where(p => p.Length == 2)
                    .Select(p => new Point(p[0], p[1])).ToList();
                if (pts.Count < 2) continue;
                strokes.Add(new AnnotationStroke(tool, pts, FromArgb(s.Color), Math.Max(0.25, s.Thickness),
                    Enum.TryParse<InkStrokeStyle>(s.Style, out var style) ? style : InkStrokeStyle.HandDrawn)
                {
                    Opacity = s.Opacity == 0 ? 1 : s.Opacity,
                    Highlighter = s.Highlighter
                });
            }
            foreach (var f in doc.Fills)
            {
                var seed = f.Seed.Length == 2 ? new Point(f.Seed[0], f.Seed[1]) : new Point(0.5, 0.5);
                fills.Add(new AnnotationFill
                {
                    Seed = seed,
                    Color = FromArgb(f.Color),
                    Opacity = f.Opacity <= 0 ? 0.45 : f.Opacity,
                    Tolerance = f.Tolerance <= 0 ? 40 : f.Tolerance
                });
            }
            foreach (var t in doc.Texts)
            {
                var tl = t.TopLeft.Length == 2 ? new Point(t.TopLeft[0], t.TopLeft[1]) : new Point(0.5, 0.5);
                texts.Add(new AnnotationText
                {
                    TopLeft = tl,
                    Width = t.Width <= 0 ? 0.3 : t.Width,
                    Source = t.Source,
                    Text = t.Text,
                    Kind = t.Kind,
                    Opacity = t.Opacity <= 0 ? 1 : t.Opacity
                });
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static BitmapSource DecodePng(string base64)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static uint ToArgb(Color c) =>
        (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);

    private static Color FromArgb(uint v) => Color.FromArgb(
        (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
}
