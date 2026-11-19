using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WBImageViewer.Models;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;

namespace WBImageViewer.Views;

/// <summary>
/// WeChat-style screen capture with four modes: full screen, picked window, rectangle and
/// freeform closed shape. All GDI work is wrapped so a failed capture never kills the app.
/// </summary>
public partial class ScreenshotWindow : Window
{
    public BitmapSource? CapturedImage { get; private set; }

    private readonly ScreenshotMode _mode;
    private System.Drawing.Rectangle _screenBounds;
    private double _scaleX = 1, _scaleY = 1;
    private Point _start;
    private bool _selecting;
    private bool _selected;
    private Rect _selection;
    private readonly List<Point> _freePoints = new();
    private StreamGeometry? _clipGeometry;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public long Area => Math.Max(0, (long)(Right - Left) * Math.Max(0, Bottom - Top));
    }

    public ScreenshotWindow(ScreenshotMode mode = ScreenshotMode.Region)
    {
        _mode = mode;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        _screenBounds = new System.Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        var dpi = VisualTreeHelper.GetDpi(this);
        _scaleX = dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY;

        UpdateDimmer(new Rect(0, 0, 0, 0));

        if (_mode == ScreenshotMode.FullScreen)
        {
            _selection = new Rect(0, 0, ActualWidth, ActualHeight);
            _selected = true;
            SelectionBox.Visibility = Visibility.Visible;
            ApplySelection();
            Toolbar.Visibility = Visibility.Visible;
            HintText.Text = "全屏截图 — 单击“确定”保存，Esc 取消";
        }
        else if (_mode == ScreenshotMode.Window)
        {
            HintText.Text = "将鼠标移到目标窗口上并单击，Esc 取消";
        }
        else if (_mode == ScreenshotMode.Freeform)
        {
            HintText.Text = "按住左键自由绘制封闭区域，松开后确认，Esc 取消";
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_selected) return;
        var p = e.GetPosition(Root);

        if (_mode == ScreenshotMode.Window)
        {
            var target = FindWindowAt(ToPhysical(p));
            if (target.Area > 100)
            {
                _selection = new Rect(
                    (target.Left - _screenBounds.X) / _scaleX,
                    (target.Top - _screenBounds.Y) / _scaleY,
                    (target.Right - target.Left) / _scaleX,
                    (target.Bottom - target.Top) / _scaleY);
                _selected = true;
                SelectionBox.Visibility = Visibility.Visible;
                ApplySelection();
                Toolbar.Visibility = Visibility.Visible;
                HintText.Visibility = Visibility.Collapsed;
            }
            return;
        }

        _start = p;
        _selecting = true;
        _freePoints.Clear();
        _freePoints.Add(p);
        SelectionBox.Visibility = _mode == ScreenshotMode.Region ? Visibility.Visible : Visibility.Collapsed;
        SizeTip.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(Root);
        if (_mode == ScreenshotMode.Window && !_selected)
        {
            var target = FindWindowAt(ToPhysical(p));
            if (target.Area > 100)
            {
                Canvas.SetLeft(SelectionBox, (target.Left - _screenBounds.X) / _scaleX);
                Canvas.SetTop(SelectionBox, (target.Top - _screenBounds.Y) / _scaleY);
                SelectionBox.Width = (target.Right - target.Left) / _scaleX;
                SelectionBox.Height = (target.Bottom - target.Top) / _scaleY;
                SelectionBox.Visibility = Visibility.Visible;
            }
            return;
        }
        if (!_selecting) return;

        if (_mode == ScreenshotMode.Region)
        {
            _selection = new Rect(_start, p);
        }
        else if (_mode == ScreenshotMode.Freeform)
        {
            var last = _freePoints.Count > 0 ? _freePoints[^1] : _start;
            if (System.Math.Sqrt((p.X - last.X) * (p.X - last.X) + (p.Y - last.Y) * (p.Y - last.Y)) >= 2)
            {
                _freePoints.Add(p);
                DrawFreeform();
            }
            _selection = new Rect(_freePoints.Min(x => x.X), _freePoints.Min(x => x.Y),
                _freePoints.Max(x => x.X) - _freePoints.Min(x => x.X),
                _freePoints.Max(x => x.Y) - _freePoints.Min(x => x.Y));
        }
        ApplySelection();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting) return;
        _selecting = false;
        if (IsMouseCaptured) ReleaseMouseCapture();

        var valid = _mode == ScreenshotMode.Freeform
            ? _freePoints.Count >= 8 && _selection.Width >= 8 && _selection.Height >= 8
            : _selection.Width >= 8 && _selection.Height >= 8;
        if (valid)
        {
            _selected = true;
            if (_mode == ScreenshotMode.Freeform) BuildClipGeometry();
            ApplySelection();
            Toolbar.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionBox.Visibility = Visibility.Collapsed;
            SizeTip.Visibility = Visibility.Collapsed;
            _freePoints.Clear();
            _clipGeometry = null;
        }
    }

    private Point ToPhysical(Point dip) =>
        new(dip.X * _scaleX + _screenBounds.X, dip.Y * _scaleY + _screenBounds.Y);

    private NativeRect FindWindowAt(Point physical)
    {
        var own = new WindowInteropHelper(this).Handle;
        NativeRect best = default;
        EnumWindows((hWnd, _) =>
        {
            if (hWnd == own || !IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0) return true;
            if (!GetWindowRect(hWnd, out var r)) return true;
            if (physical.X < r.Left || physical.X > r.Right || physical.Y < r.Top || physical.Y > r.Bottom) return true;
            if (r.Right - r.Left < 40 || r.Bottom - r.Top < 30) return true;
            if (best.Area == 0 || r.Area < best.Area) best = r;
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private void DrawFreeform()
    {
        if (_freePoints.Count < 3) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(_freePoints[0], true, true);
            for (var i = 1; i < _freePoints.Count; i++) ctx.LineTo(_freePoints[i], true, true);
        }
        SelectionBox.Data = g;
        SelectionBox.Visibility = Visibility.Visible;
    }

    private void BuildClipGeometry()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var first = new Point((_freePoints[0].X - _selection.Left) * _scaleX,
                (_freePoints[0].Y - _selection.Top) * _scaleY);
            ctx.BeginFigure(first, true, true);
            for (var i = 1; i < _freePoints.Count; i++)
                ctx.LineTo(new Point((_freePoints[i].X - _selection.Left) * _scaleX,
                    (_freePoints[i].Y - _selection.Top) * _scaleY), true, true);
        }
        _clipGeometry = g;
    }

    private void ApplySelection()
    {
        if (_mode == ScreenshotMode.Region || _mode == ScreenshotMode.FullScreen || _mode == ScreenshotMode.Window)
        {
            Canvas.SetLeft(SelectionBox, _selection.Left);
            Canvas.SetTop(SelectionBox, _selection.Top);
            SelectionBox.Width = _selection.Width;
            SelectionBox.Height = _selection.Height;
            SelectionBox.Data = null;
        }
        UpdateDimmer(_selection);

        var w = (int)Math.Round(_selection.Width * _scaleX);
        var h = (int)Math.Round(_selection.Height * _scaleY);
        SizeText.Text = $"{w} × {h}";
        Canvas.SetLeft(SizeTip, Math.Max(0, _selection.Left));
        Canvas.SetTop(SizeTip, Math.Max(0, _selection.Top - 26));
        Canvas.SetLeft(Toolbar, Math.Max(4, Math.Min(ActualWidth - 160, _selection.Right - 146)));
        Canvas.SetTop(Toolbar, Math.Min(Math.Max(0, ActualHeight - 44), _selection.Bottom + 6));
    }

    private void UpdateDimmer(Rect hole)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(ActualWidth, 0), true, true);
            ctx.LineTo(new Point(ActualWidth, ActualHeight), true, true);
            ctx.LineTo(new Point(0, ActualHeight), true, true);
            ctx.Close();
            if (hole.Width > 0 && hole.Height > 0)
            {
                ctx.BeginFigure(new Point(hole.Left, hole.Top), true, true);
                ctx.LineTo(new Point(hole.Left, hole.Bottom), true, true);
                ctx.LineTo(new Point(hole.Right, hole.Bottom), true, true);
                ctx.LineTo(new Point(hole.Right, hole.Top), true, true);
                ctx.Close();
            }
        }
        Dimmer.Data = geometry;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var x = _screenBounds.X + (int)Math.Round(_selection.Left * _scaleX);
            var y = _screenBounds.Y + (int)Math.Round(_selection.Top * _scaleY);
            var w = (int)Math.Round(_selection.Width * _scaleX);
            var h = (int)Math.Round(_selection.Height * _scaleY);
            if (w < 1 || h < 1) throw new InvalidOperationException("截图区域为空。");

            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var gfx = System.Drawing.Graphics.FromImage(bmp))
                gfx.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);

            var source = new BitmapImage();
            source.BeginInit();
            using (var ms = new System.IO.MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, System.IO.SeekOrigin.Begin);
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.StreamSource = ms;
                source.EndInit();
            }
            source.Freeze();
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            CapturedImage = _mode == ScreenshotMode.Freeform && _clipGeometry != null
                ? ClipBitmap(converted) : converted;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"截图失败：{ex.Message}", "屏幕截图", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
        Close();
    }

    private BitmapSource ClipBitmap(BitmapSource source)
    {
        var canvas = new Canvas
        {
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Background = Brushes.Transparent,
            Clip = _clipGeometry
        };
        canvas.Children.Add(new System.Windows.Controls.Image
        {
            Source = source,
            Width = source.PixelWidth,
            Height = source.PixelHeight
        });
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(canvas);
        target.Freeze();
        return target;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CapturedImage = null;
        DialogResult = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CapturedImage = null;
            DialogResult = false;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
