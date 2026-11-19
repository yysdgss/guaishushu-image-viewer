using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace WBImageViewer.Views;

/// <summary>
/// WeChat-style screen capture: dims the virtual screen, drag to select a rectangle,
/// then confirm to copy that region into a bitmap that is shown in a new pinned window.
/// </summary>
public partial class ScreenshotWindow : Window
{
    public BitmapSource? CapturedImage { get; private set; }

    private System.Drawing.Rectangle _screenBounds;
    private double _scaleX = 1, _scaleY = 1;
    private Point _start;
    private bool _selecting;
    private bool _selected;
    private Rect _selection;

    public ScreenshotWindow()
    {
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
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_selected) return;
        _start = e.GetPosition(Root);
        _selecting = true;
        SelectionBox.Visibility = Visibility.Visible;
        SizeTip.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting) return;
        var p = e.GetPosition(Root);
        _selection = new Rect(_start, p);
        ApplySelection();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting) return;
        _selecting = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_selection.Width >= 8 && _selection.Height >= 8)
        {
            _selected = true;
            var x = Canvas.GetLeft(Toolbar);
            var y = Canvas.GetTop(Toolbar);
            Toolbar.Visibility = Visibility.Visible;
            Canvas.SetLeft(Toolbar, Math.Max(4, x));
            Canvas.SetTop(Toolbar, Math.Min(ActualHeight - 44, y + 4));
        }
        else
        {
            SelectionBox.Visibility = Visibility.Collapsed;
            SizeTip.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplySelection()
    {
        Canvas.SetLeft(SelectionBox, _selection.Left);
        Canvas.SetTop(SelectionBox, _selection.Top);
        SelectionBox.Width = _selection.Width;
        SelectionBox.Height = _selection.Height;

        UpdateDimmer(_selection);

        var w = (int)Math.Round(_selection.Width * _scaleX);
        var h = (int)Math.Round(_selection.Height * _scaleY);
        SizeText.Text = $"{w} × {h}";
        Canvas.SetLeft(SizeTip, _selection.Left);
        Canvas.SetTop(SizeTip, Math.Max(0, _selection.Top - 26));

        // toolbar above the selection's bottom-right
        Canvas.SetLeft(Toolbar, _selection.Right - 146);
        Canvas.SetTop(Toolbar, _selection.Bottom + 6);
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
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);

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
            CapturedImage = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"截图失败：{ex.Message}", "屏幕截图", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
        Close();
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
