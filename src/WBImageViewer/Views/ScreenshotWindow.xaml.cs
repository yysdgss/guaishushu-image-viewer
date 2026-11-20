using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WBImageViewer.Models;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;

namespace WBImageViewer.Views;

/// <summary>
/// WeChat-style screen capture. The screen is snapshotted once before the overlay opens,
/// then every mode crops from that frozen bitmap. This keeps the overlay out of the result
/// and avoids repeated GDI screen reads that could stall the viewer.
///
/// IMPORTANT: this window deliberately uses NO StreamGeometry. Its ByteStreamGeometryContext
/// is disposed by the using() pattern while WPF may still be rendering the Path, which threw
/// "Cannot access a disposed object" intermittently on fast selections. All visuals are now
/// made of Rectangles / Polyline / PathGeometry built from immutable figures.
/// </summary>
public partial class ScreenshotWindow : Window
{
    private readonly BitmapSource _screenImage;
    public BitmapSource? CapturedImage { get; private set; }

    private readonly ScreenshotMode _mode;
    private System.Drawing.Rectangle _screenBounds;
    private double _scaleX = 1, _scaleY = 1;
    private Point _start;
    private bool _selecting;
    private bool _selected;
    private Rect _selection;
    private readonly List<Point> _freePoints = new();
    private PathGeometry? _clipGeometry;
    private DateTime _lastWindowProbe = DateTime.MinValue;
    private NativeRect _hoverWindow;

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

    public ScreenshotWindow(ScreenshotMode mode, BitmapSource screenImage)
    {
        _mode = mode;
        _screenImage = screenImage;
        InitializeComponent();
    }

    /// <summary>Capture the untouched screen before any overlay is created.</summary>
    public static BitmapSource? CaptureScreen()
    {
        try
        {
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (bounds.Width < 1 || bounds.Height < 1) return null;
            using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var gfx = System.Drawing.Graphics.FromImage(bmp))
                gfx.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);

            var image = new BitmapImage();
            image.BeginInit();
            using (var ms = new System.IO.MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, System.IO.SeekOrigin.Begin);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            BitmapSource source = image.Format != PixelFormats.Bgra32
                ? new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)
                : image;
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        _screenBounds = new System.Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        ScreenBackdrop.Source = _screenImage;

        var dpi = VisualTreeHelper.GetDpi(this);
        _scaleX = dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY;

        UpdateDimmer(new Rect(0, 0, 0, 0));

        if (_mode == ScreenshotMode.FullScreen)
        {
            _selection = new Rect(0, 0, ActualWidth, ActualHeight);
            _selected = true;
            SelectionRect.Visibility = Visibility.Visible;
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
                SelectionRect.Visibility = Visibility.Visible;
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
        SelectionRect.Visibility = _mode == ScreenshotMode.Region ? Visibility.Visible : Visibility.Collapsed;
        SelectionFree.Visibility = Visibility.Collapsed;
        SelectionFree.Points.Clear();
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
            // Window enumeration is comparatively expensive; sample it at most 20 times/s.
            if ((DateTime.Now - _lastWindowProbe).TotalMilliseconds >= 50)
            {
                _lastWindowProbe = DateTime.Now;
                _hoverWindow = FindWindowAt(ToPhysical(p));
            }
            if (_hoverWindow.Area > 100)
            {
                Canvas.SetLeft(SelectionRect, (_hoverWindow.Left - _screenBounds.X) / _scaleX);
                Canvas.SetTop(SelectionRect, (_hoverWindow.Top - _screenBounds.Y) / _scaleY);
                SelectionRect.Width = (_hoverWindow.Right - _hoverWindow.Left) / _scaleX;
                SelectionRect.Height = (_hoverWindow.Bottom - _hoverWindow.Top) / _scaleY;
                SelectionRect.Visibility = Visibility.Visible;
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
            if (Math.Sqrt((p.X - last.X) * (p.X - last.X) + (p.Y - last.Y) * (p.Y - last.Y)) >= 3)
            {
                _freePoints.Add(p);
                DrawFreeform();
            }
            if (_freePoints.Count > 0)
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
            SelectionRect.Visibility = Visibility.Collapsed;
            SelectionFree.Visibility = Visibility.Collapsed;
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
        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (hWnd == own || !IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0) return true;
                if (!GetWindowRect(hWnd, out var r)) return true;
                if (physical.X < r.Left || physical.X > r.Right || physical.Y < r.Top || physical.Y > r.Bottom) return true;
                if (r.Right - r.Left < 40 || r.Bottom - r.Top < 30) return true;
                if (best.Area == 0 || r.Area < best.Area) best = r;
                return true;
            }, IntPtr.Zero);
        }
        catch { /* an unrelated window disappearing mid-enumeration must not kill capture */ }
        return best;
    }

    private void DrawFreeform()
    {
        if (_freePoints.Count < 2) return;
        var points = new PointCollection(_freePoints);
        SelectionFree.Points = points;
        SelectionFree.Visibility = Visibility.Visible;
    }

    /// <summary>Build the freeform clip as an immutable PathGeometry (no disposable context).</summary>
    private void BuildClipGeometry()
    {
        if (_freePoints.Count < 3) return;
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            IsFilled = true,
            IsClosed = true,
            StartPoint = new Point(
                (_freePoints[0].X - _selection.Left) * _scaleX,
                (_freePoints[0].Y - _selection.Top) * _scaleY)
        };
        for (var i = 1; i < _freePoints.Count; i++)
            figure.Segments.Add(new LineSegment(new Point(
                (_freePoints[i].X - _selection.Left) * _scaleX,
                (_freePoints[i].Y - _selection.Top) * _scaleY), true));
        geometry.Figures.Add(figure);
        geometry.Freeze();
        _clipGeometry = geometry;
    }

    private void ApplySelection()
    {
        if (_mode == ScreenshotMode.Freeform)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SelectionFree.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionFree.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _selection.Left);
            Canvas.SetTop(SelectionRect, _selection.Top);
            SelectionRect.Width = Math.Max(0, _selection.Width);
            SelectionRect.Height = Math.Max(0, _selection.Height);
        }
        UpdateDimmer(_selection);

        var w = Math.Max(0, (int)Math.Round(_selection.Width * _scaleX));
        var h = Math.Max(0, (int)Math.Round(_selection.Height * _scaleY));
        SizeText.Text = $"{w} × {h}";
        Canvas.SetLeft(SizeTip, Math.Max(0, _selection.Left));
        Canvas.SetTop(SizeTip, Math.Max(0, _selection.Top - 26));
        Canvas.SetLeft(Toolbar, Math.Max(4, Math.Min(ActualWidth - 160, _selection.Right - 146)));
        Canvas.SetTop(Toolbar, Math.Min(Math.Max(0, ActualHeight - 44), _selection.Bottom + 6));
    }

    /// <summary>Four rectangles instead of a StreamGeometry path with a hole.</summary>
    private void UpdateDimmer(Rect hole)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var top = Math.Max(0, Math.Min(h, hole.Top));
        var bottom = Math.Max(0, Math.Min(h, hole.Bottom));
        var left = Math.Max(0, Math.Min(w, hole.Left));
        var right = Math.Max(0, Math.Min(w, hole.Right));

        SetRect(DimTop, 0, 0, w, top);
        SetRect(DimBottom, 0, bottom, w, Math.Max(0, h - bottom));
        SetRect(DimLeft, 0, top, left, Math.Max(0, bottom - top));
        SetRect(DimRight, right, top, Math.Max(0, w - right), Math.Max(0, bottom - top));
    }

    private static void SetRect(FrameworkElement rect, double x, double y, double w, double h)
    {
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(0, w);
        rect.Height = Math.Max(0, h);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rect = PhysicalSelection();
            if (rect.Width < 1 || rect.Height < 1) throw new InvalidOperationException("截图区域为空。");

            BitmapSource cropped = new CroppedBitmap(_screenImage, rect);
            cropped.Freeze();
            CapturedImage = _mode == ScreenshotMode.Freeform && _clipGeometry != null
                ? ClipBitmap(cropped) : cropped;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            CapturedImage = null;
            MessageBox.Show($"截图失败：{ex.Message}", "屏幕截图", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
        Close();
    }

    private Int32Rect PhysicalSelection()
    {
        var x = _screenBounds.X + (int)Math.Round(_selection.Left * _scaleX);
        var y = _screenBounds.Y + (int)Math.Round(_selection.Top * _scaleY);
        var w = (int)Math.Round(_selection.Width * _scaleX);
        var h = (int)Math.Round(_selection.Height * _scaleY);
        var left = Math.Max(0, x - _screenBounds.X);
        var top = Math.Max(0, y - _screenBounds.Y);
        var right = Math.Min(_screenImage.PixelWidth, left + w);
        var bottom = Math.Min(_screenImage.PixelHeight, top + h);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
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
