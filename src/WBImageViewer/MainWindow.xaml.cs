using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WBImageViewer.Models;
using WBImageViewer.Native;
using WBImageViewer.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using WinForms = System.Windows.Forms;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace WBImageViewer;

public partial class MainWindow : Window
{
    private int[]? _sourcePixels;
    private int[]? _displayPixels;
    private WriteableBitmap? _display;
    private int _pixelWidth;
    private int _pixelHeight;
    private double _zoom = 1;
    private double _offsetX;
    private double _offsetY;

    private ImageAdjustments _adjustments = new();
    private readonly DispatcherTimer _adjustTimer;
    private bool _updatingControls;

    private bool _movingWindow;
    private bool _panningImage;
    private bool _fittingWindow;
    private Win32.POINT _moveCursorStart;
    private Win32.RECT _moveWindowStart;
    private Point _lastPanPosition;

    // Annotation state.
    private readonly List<AnnotationStroke> _annotations = new();
    private AnnotationTool _annotationTool = AnnotationTool.Pointer;
    private Color _annotationColor = Color.FromRgb(255, 82, 82);
    private double _annotationSize = 3;
    private double _eraserSize = 16;
    private bool _annotating;
    private List<Point> _draftPoints = new();
    private Point _draftPointer;
    private UIElement? _draftVisual;
    private int _movingAnnotation = -1;
    private Point _moveStartImage;
    private List<Point>? _moveOriginalPoints;

    // Hierarchical compass state.
    private static readonly List<CompassNode> CompassTree = BuildCompassTree();
    private readonly List<UIElement> _compassVisuals = new();
    private bool _compassOpen;
    private Point _compassOrigin;
    private Point _compassPointer;
    private int _compassActiveRing = -1;
    private Line? _compassGuideHalo;
    private Line? _compassGuideFlow;
    private Border? _compassTooltip;
    private TextBlock? _compassTooltipText;

    public MainWindow()
    {
        InitializeComponent();
        AllowDrop = true;
        TopmostCoordinator.Register(this);
        _adjustTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _adjustTimer.Tick += (_, _) =>
        {
            _adjustTimer.Stop();
            ApplyAdjustments();
        };

        IslandHitArea.MouseEnter += (_, _) => AnimateIsland(true);
        IslandHitArea.MouseLeave += (_, _) => AnimateIsland(false);
        CloseHost.MouseEnter += (_, _) => AnimateCloseButton(0);
        CloseHost.MouseLeave += (_, _) => AnimateCloseButton(-32);
        ImageViewport.SizeChanged += (_, _) => { if (!_fittingWindow) ApplyImagePosition(); };
        UpdatePinState();
        ApplyVisualSettings(App.Settings);
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { if (_sourcePixels != null) FitImageToWindow(); };
        ContentRendered += (_, _) => { if (_sourcePixels != null) FitImageToWindow(); };
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        DragEnter += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyVisualSettings(App.Settings);
        SizeChanged += (_, _) => ApplyCornerRadius(App.Settings.CornerRadius);
    }

    /// <summary>DWM draws anti-aliased corners, avoiding the jagged GDI window region.</summary>
    public void ApplyCornerRadius(int radius)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Remove any legacy GDI region first - it is the source of hard, pixelated edges.
        Win32.SetWindowRgn(hwnd, IntPtr.Zero, true);

        var preference = radius <= 0 ? 1 : radius <= 12 ? 3 : 2; // no round / small / standard
        Win32.DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
    }

    public void ApplyVisualSettings(AppSettings settings)
    {
        try
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
            var alpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0, 1) * 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            Root.Background = brush;
            // The window itself stays fully transparent. Only the panel background receives
            // the configured alpha, so the image and controls remain fully opaque.
            Background = System.Windows.Media.Brushes.Transparent;
        }
        catch
        {
            // keep the previous valid colour
        }

        if (App.Settings.Adjustments != null && _sourcePixels == null)
            _adjustments = App.Settings.Adjustments.Clone();
        ApplyCornerRadius(settings.CornerRadius);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Win32.WM_GETMINMAXINFO)
        {
            var area = WorkAreaPhysical();
            var dpi = VisualTreeHelper.GetDpi(this);
            var info = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
            info.ptMinTrackSize = new Win32.POINT(
                (int)Math.Round(140 * dpi.DpiScaleX), (int)Math.Round(100 * dpi.DpiScaleY));
            info.ptMaxSize = new Win32.POINT(area.Width, area.Height);
            info.ptMaxPosition = new Win32.POINT(area.Left, area.Top);
            info.ptMaxTrackSize = new Win32.POINT(area.Width, area.Height);
            Marshal.StructureToPtr(info, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void LoadImage(string path)
    {
        try
        {
            // WIC decodes ordinary bitmap formats; document/vector formats fall back
            // to the Windows shell thumbnail provider (see ShellPreviewService).
            var decoded = DecodeImage(path)
                ?? throw new InvalidOperationException("系统无法解码该格式，且 Windows 未提供可用缩略图。");
            var source = decoded.Format == PixelFormats.Bgra32
                ? decoded
                : new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);

            _pixelWidth = source.PixelWidth;
            _pixelHeight = source.PixelHeight;
            _sourcePixels = new int[_pixelWidth * _pixelHeight];
            source.CopyPixels(_sourcePixels, _pixelWidth * 4, 0);

            _displayPixels = new int[_sourcePixels.Length];
            _display = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
            _zoom = 1;
            _offsetX = _offsetY = 0;
            _annotations.Clear();
            AnnotationLayer.Children.Clear();
            ClearDraftVisual();

            _adjustments = App.Settings.AutoOptimize
                ? ImageAdjustments.SmartPreset()
                : (App.Settings.Adjustments ?? new ImageAdjustments()).Clone();
            UpdateAdjustmentControls();
            ApplyAdjustments();

            ImageView.Source = _display;
            Hint.Visibility = Visibility.Collapsed;
            Title = $"{AppInfo.Name} - {System.IO.Path.GetFileName(path)}";
            ApplyImagePosition();

            if (IsLoaded) FitImageToWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开图片：{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Try native WIC decoding first, then the system thumbnail provider.</summary>
    private static BitmapSource? DecodeImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return ShellPreviewService.TryLoad(path);
        }
    }

    private void ApplyAdjustments()
    {
        if (_sourcePixels == null || _displayPixels == null || _display == null) return;
        var pixels = _displayPixels;
        ImageEnhanceService.Apply(_sourcePixels, pixels, _pixelWidth, _pixelHeight, _adjustments);
        _display.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight),
            pixels, _pixelWidth * 4, 0);
    }

    private void UpdateAdjustmentControls()
    {
        _updatingControls = true;
        BrightnessSlider.Value = _adjustments.Brightness;
        ContrastSlider.Value = _adjustments.Contrast;
        SaturationSlider.Value = _adjustments.Saturation;
        DenoiseSlider.Value = _adjustments.Denoise;
        SharpenSlider.Value = _adjustments.Sharpen;
        _updatingControls = false;
        UpdateAdjustmentLabels();
    }

    private void UpdateAdjustmentLabels()
    {
        BrightnessLabel.Text = $"亮度 {_adjustments.Brightness}";
        ContrastLabel.Text = $"对比度 {_adjustments.Contrast}";
        SaturationLabel.Text = $"饱和度 {_adjustments.Saturation}";
        DenoiseLabel.Text = $"去噪 {_adjustments.Denoise}";
        SharpenLabel.Text = $"锐化 {_adjustments.Sharpen}";
    }

    private void Adjustment_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingControls) return;
        _adjustments = new ImageAdjustments
        {
            Brightness = (int)Math.Round(BrightnessSlider.Value),
            Contrast = (int)Math.Round(ContrastSlider.Value),
            Saturation = (int)Math.Round(SaturationSlider.Value),
            Denoise = (int)Math.Round(DenoiseSlider.Value),
            Sharpen = (int)Math.Round(SharpenSlider.Value)
        };
        App.Settings.Adjustments = _adjustments.Clone();
        UpdateAdjustmentLabels();
        _adjustTimer.Stop();
        _adjustTimer.Start();
    }

    private void SmartPreset_Click(object sender, RoutedEventArgs e)
    {
        _adjustments = ImageAdjustments.SmartPreset();
        App.Settings.Adjustments = _adjustments.Clone();
        UpdateAdjustmentControls();
        ApplyAdjustments();
    }

    private void ResetAdjust_Click(object sender, RoutedEventArgs e)
    {
        _adjustments = new ImageAdjustments();
        App.Settings.Adjustments = _adjustments.Clone();
        UpdateAdjustmentControls();
        ApplyAdjustments();
    }

    public void ApplySavedAdjustments()
    {
        if (_sourcePixels == null) return;
        _adjustments = (App.Settings.Adjustments ?? new ImageAdjustments()).Clone();
        UpdateAdjustmentControls();
        ApplyAdjustments();
    }

    private void AdjustButton_Click(object sender, RoutedEventArgs e)
    {
        AdjustPanel.Visibility = AdjustPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseAdjust_Click(object sender, RoutedEventArgs e) => AdjustPanel.Visibility = Visibility.Collapsed;

    private void PinButton_Click(object sender, RoutedEventArgs e) => ExecuteHotkey(HotkeyAction.ToggleTopMost);
    private void WindowToImageButton_Click(object sender, RoutedEventArgs e) => ExecuteHotkey(HotkeyAction.FitWindowToImage);
    private void ImageToWindowButton_Click(object sender, RoutedEventArgs e) => ExecuteHotkey(HotkeyAction.FitImageToWindow);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.ShowSettings();
    private void AboutButton_Click(object sender, RoutedEventArgs e) => App.ShowAbout();
    private static bool IsFromButton(RoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null)
        {
            if (element is System.Windows.Controls.Button) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>
    /// Reveals the screenshot-style island by animating a rectangular clip from the
    /// collapsed hairline down to the full rounded shape. No layout properties animate,
    /// so the panel stays cheap to composite.
    /// </summary>
    private void AnimateIsland(bool expand)
    {
        var targetHeight = expand ? IslandRoot.Height : 2;
        var clipAnimation = new RectAnimation(
            new Rect(0, 0, IslandRoot.Width, targetHeight),
            TimeSpan.FromMilliseconds(expand ? 260 : 190))
        {
            EasingFunction = new QuadraticEase
            {
                EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        IslandClip.BeginAnimation(RectangleGeometry.RectProperty, clipAnimation);

        var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 180 : 110));
        IslandButtons.BeginAnimation(UIElement.OpacityProperty, fade);
        IslandButtons.IsHitTestVisible = expand;
    }

    private void AnimateCloseButton(double targetY)
    {
        var animation = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        CloseTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    /// <summary>
    /// Reflect topmost state on the 📌 pushpin. Pinned drives the pin down into the
    /// panel (top-in); unpinned pulls it out and tilts it (pull-out). The From values
    /// are the opposite state so every toggle plays the physical motion explicitly.
    /// </summary>
    private void UpdatePinState()
    {
        var pinned = Topmost;
        PinButton.Tag = pinned ? "Pinned" : "Unpinned";
        PinButton.ToolTip = pinned
            ? "取消置顶 (Shift+Ctrl+Alt+T)"
            : "置顶窗口 (Shift+Ctrl+Alt+T)";

        PinButton.ApplyTemplate();
        var template = PinButton.Template;
        if (template is null) return;

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut };
        if (template.FindName("PinRotate", PinButton) is RotateTransform rotate)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(
                pinned ? -28 : 0, pinned ? 0 : -28, duration) { EasingFunction = ease });
        }
        if (template.FindName("PinLift", PinButton) is TranslateTransform lift)
        {
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
                pinned ? -9 : 0, pinned ? 0 : -9, duration) { EasingFunction = ease });
        }
        if (template.FindName("PinGlyph", PinButton) is UIElement glyph)
        {
            glyph.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
                pinned ? 0.45 : 1, pinned ? 1 : 0.45, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    public void ExecuteHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleTopMost:
                TopmostCoordinator.SetTopmost(!TopmostCoordinator.TopmostEnabled);
                UpdatePinState();
                break;
            case HotkeyAction.FitWindowToImage:
                FitWindowToImage();
                break;
            case HotkeyAction.FitImageToWindow:
                FitImageToWindow();
                break;
        }
    }

    /// <summary>Fit the image inside the current window, preserving aspect ratio (no stretching).</summary>
    public void FitImageToWindow()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        var width = ImageViewport.ActualWidth;
        var height = ImageViewport.ActualHeight;
        if (width < 8 || height < 8) return;

        _zoom = Math.Min(width / _pixelWidth, height / _pixelHeight);
        _offsetX = _offsetY = 0;
        ApplyImageSize();
    }

    private void ApplyImageSize()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        ImageView.Width = _pixelWidth * _zoom;
        ImageView.Height = _pixelHeight * _zoom;
        ApplyImagePosition();
    }

    /// <summary>
    /// Positions the image inside the viewport. The Canvas clip keeps every zoom level
    /// confined to the current window size, so the picture can never cover or enlarge
    /// the surrounding panel layout.
    /// </summary>
    private void ApplyImagePosition()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        var centerX = (ImageViewport.ActualWidth - ImageView.Width) / 2.0;
        var centerY = (ImageViewport.ActualHeight - ImageView.Height) / 2.0;
        Canvas.SetLeft(ImageView, centerX + _offsetX);
        Canvas.SetTop(ImageView, centerY + _offsetY);
        RedrawAnnotations();
        if (_draftVisual != null) RefreshDraftVisual();
    }

    /// <summary>
    /// Resize the window so the image viewport exactly matches the image's current
    /// display size, and move the window around the image's current screen position.
    /// The anchor is the picture itself, never the window frame's top-left corner.
    /// WindowChrome adds an invisible resize border around the client area, so the
    /// outer rect must include that delta; otherwise the canvas keeps blank margins.
    /// </summary>
    private void FitWindowToImage()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var area = WorkAreaPhysical();

        // Client origin relative to the outer frame, used to convert between image
        // coordinates and physical screen coordinates.
        var clientOrigin = new Win32.POINT(0, 0);
        if (!Win32.ClientToScreen(hwnd, ref clientOrigin)) return;
        var clientPadX = clientOrigin.X - rect.Left;
        var clientPadY = clientOrigin.Y - rect.Top;

        // Capture where the picture is right now. After the resize we put the window
        // back around this point, so the picture appears not to move at all.
        var imageLeftDip = double.IsNaN(Canvas.GetLeft(ImageView)) ? 0 : Canvas.GetLeft(ImageView);
        var imageTopDip = double.IsNaN(Canvas.GetTop(ImageView)) ? 0 : Canvas.GetTop(ImageView);
        var imageLeftScreen = clientOrigin.X + (int)Math.Round(imageLeftDip * dpi.DpiScaleX);
        var imageTopScreen = clientOrigin.Y + (int)Math.Round(imageTopDip * dpi.DpiScaleY);

        // Outer rect minus real client area = invisible resize border on each side.
        var borderX = rect.Width - (int)Math.Round(ImageViewport.ActualWidth * dpi.DpiScaleX);
        var borderY = rect.Height - (int)Math.Round(ImageViewport.ActualHeight * dpi.DpiScaleY);
        if (borderX < 0) borderX = 0;
        if (borderY < 0) borderY = 0;

        // Target client size is the image's current on-screen size, in physical pixels.
        var clientW = Math.Max(1, (int)Math.Ceiling(_pixelWidth * _zoom * dpi.DpiScaleX));
        var clientH = Math.Max(1, (int)Math.Ceiling(_pixelHeight * _zoom * dpi.DpiScaleY));
        var width = clientW + borderX;
        var height = clientH + borderY;

        var maxW = Math.Max(140, area.Width);
        var maxH = Math.Max(100, area.Height);
        var scale = Math.Min(1.0, Math.Min(maxW / (double)width, maxH / (double)height));
        if (scale < 1)
        {
            width = Math.Max(140, (int)Math.Floor(width * scale));
            height = Math.Max(100, (int)Math.Floor(height * scale));
            clientW = width - borderX;
            clientH = height - borderY;
            _zoom = Math.Min(clientW / (_pixelWidth * dpi.DpiScaleX),
                             clientH / (_pixelHeight * dpi.DpiScaleY));
            ApplyImageSize();
        }

        // Position from the image anchor, then clamp only when the frame would leave
        // the work area. Any leftover clamp distance is absorbed by the image offset.
        var left = Math.Clamp(imageLeftScreen - clientPadX, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(imageTopScreen - clientPadY, area.Top, Math.Max(area.Top, area.Bottom - height));
        var residualOffsetX = (imageLeftScreen - (left + clientPadX)) / dpi.DpiScaleX;
        var residualOffsetY = (imageTopScreen - (top + clientPadY)) / dpi.DpiScaleY;

        _fittingWindow = true;
        Win32.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

        // Layout is async: snap the image to the final client size once the new
        // viewport has been arranged, while preserving the anchored screen position.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var vw = ImageViewport.ActualWidth;
            var vh = ImageViewport.ActualHeight;
            if (vw > 8 && vh > 8)
            {
                _zoom = Math.Min(vw / _pixelWidth, vh / _pixelHeight);
                _offsetX = residualOffsetX;
                _offsetY = residualOffsetY;
                ApplyImageSize();
            }
            _fittingWindow = false;
        }), DispatcherPriority.Render);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_sourcePixels == null) return;
        e.Handled = true;

        var factor = e.Delta > 0 ? App.Settings.ZoomStep : 1.0 / App.Settings.ZoomStep;
        ZoomAt(e.GetPosition(ImageViewport), factor);

        if (HotkeySpec.IsModifierDown(App.Settings.WheelWindowModifiers))
            ZoomWindow(factor);
    }

    private void ZoomAt(Point point, double factor)
    {
        var oldZoom = _zoom;
        var newZoom = Math.Clamp(oldZoom * factor, 0.02, 40);
        if (Math.Abs(newZoom - oldZoom) < 1e-9) return;

        var oldWidth = _pixelWidth * oldZoom;
        var oldHeight = _pixelHeight * oldZoom;
        var oldLeft = (ImageViewport.ActualWidth - oldWidth) / 2.0 + _offsetX;
        var oldTop = (ImageViewport.ActualHeight - oldHeight) / 2.0 + _offsetY;

        var relativeX = (point.X - oldLeft) / oldWidth;
        var relativeY = (point.Y - oldTop) / oldHeight;

        var newWidth = _pixelWidth * newZoom;
        var newHeight = _pixelHeight * newZoom;
        var newLeft = (ImageViewport.ActualWidth - newWidth) / 2.0;
        var newTop = (ImageViewport.ActualHeight - newHeight) / 2.0;

        _offsetX = point.X - newLeft - relativeX * newWidth;
        _offsetY = point.Y - newTop - relativeY * newHeight;
        _zoom = newZoom;
        ApplyImageSize();
    }

    private void ZoomWindow(double factor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;

        var area = WorkAreaPhysical();
        var centerX = (rect.Left + rect.Right) / 2;
        var centerY = (rect.Top + rect.Bottom) / 2;
        var width = Math.Clamp((int)Math.Round(rect.Width * factor), 140, area.Width);
        var height = Math.Clamp((int)Math.Round(rect.Height * factor), 100, area.Height);
        var left = Math.Clamp(centerX - width / 2, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(centerY - height / 2, area.Top, Math.Max(area.Top, area.Bottom - height));
        Win32.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (IsFromButton(e)) return;
        if (HandleCompassDown(e)) return;
        if (HandleAnnotationDown(e)) return;

        var mode = App.Settings.MoveMode;
        var wantsMove = e.ChangedButton switch
        {
            MouseButton.Right => mode == WindowMoveMode.RightButton,
            MouseButton.Middle => mode == WindowMoveMode.MiddleButton,
            MouseButton.Left => mode == WindowMoveMode.SpaceAndLeftButton && Win32.IsKeyDown(Win32.VK_SPACE),
            _ => false
        };

        if (wantsMove && !IsOverResizeBorder())
        {
            BeginWindowMove();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _sourcePixels != null)
        {
            _panningImage = true;
            _lastPanPosition = e.GetPosition(ImageViewport);
            SetGrabCursor();
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (HandleCompassMove(e)) return;
        if (HandleAnnotationMove(e)) return;

        if (_movingWindow)
        {
            if (Win32.GetCursorPos(out var cursor))
            {
                var dx = cursor.X - _moveCursorStart.X;
                var dy = cursor.Y - _moveCursorStart.Y;
                Win32.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                    _moveWindowStart.Left + dx, _moveWindowStart.Top + dy, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
            e.Handled = true;
            return;
        }

        if (_panningImage)
        {
            var current = e.GetPosition(ImageViewport);
            _offsetX += current.X - _lastPanPosition.X;
            _offsetY += current.Y - _lastPanPosition.Y;
            _lastPanPosition = current;
            ApplyImagePosition();
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (HandleCompassUp(e)) return;
        if (HandleAnnotationUp(e)) return;
        if (_movingWindow || _panningImage)
        {
            _movingWindow = false;
            _panningImage = false;
            RestoreCursor();
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_compassOpen) CloseCompass();
        _annotating = false;
        _movingAnnotation = -1;
        _moveOriginalPoints = null;
        ClearDraftVisual();
        _movingWindow = false;
        _panningImage = false;
        RestoreCursor();
    }

    /// <summary>
    /// Use Windows' built-in hand cursor while dragging; no custom .cur resource is loaded.
    /// Hovering over the image keeps the normal arrow because no Cursor override is active.
    /// </summary>
    private void SetGrabCursor() => Cursor = Cursors.Hand;

    private void RestoreCursor() => Cursor = _annotationTool switch
    {
        AnnotationTool.Pointer => Cursors.Arrow,
        AnnotationTool.Move => Cursors.SizeAll,
        _ => Cursors.Cross
    };

    private void BeginWindowMove()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var cursor) || !Win32.GetWindowRect(hwnd, out var rect))
            return;

        _moveCursorStart = cursor;
        _moveWindowStart = rect;
        _movingWindow = true;
        SetGrabCursor();
        CaptureMouse();
    }

    private bool IsOverResizeBorder()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var cursor) || !Win32.GetWindowRect(hwnd, out var rect))
            return false;

        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = Math.Max(3, (int)Math.Round(8 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY)));
        return cursor.X <= rect.Left + margin || cursor.X >= rect.Right - margin ||
               cursor.Y <= rect.Top + margin || cursor.Y >= rect.Bottom - margin;
    }

    private Win32.RECT WorkAreaPhysical()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var workingArea = hwnd != IntPtr.Zero && WinForms.Screen.FromHandle(hwnd) != null
            ? WinForms.Screen.FromHandle(hwnd).WorkingArea
            : WinForms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new Win32.RECT
        {
            Left = workingArea.X,
            Top = workingArea.Y,
            Right = workingArea.X + workingArea.Width,
            Bottom = workingArea.Y + workingArea.Height
        };
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var image = files.FirstOrDefault(ImageFile.IsSupported);
        if (image != null) LoadImage(image);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        RenderOptions.SetBitmapScalingMode(ImageView, BitmapScalingMode.LowQuality);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RenderOptions.SetBitmapScalingMode(ImageView, BitmapScalingMode.HighQuality);
        TopmostCoordinator.Raise(this);
    }

    // ---------------------------------------------------------------------
    // Annotation compass (SolidWorks-style) and ink overlay
    // ---------------------------------------------------------------------

    private enum CompassAction
    {
        None, Pointer, Move, Line, Wave, DoubleLine, Freehand, Circle,
        EraserFree, EraserClick, EraserBigger, EraserSmaller,
        Size1, Size2, Size3, Size4, Size5,
        SetColor, ColorCycle, ClearAll
    }

    /// <summary>One node of the hierarchical radial menu. Groups have children, leaves have an action.</summary>
    private sealed class CompassNode
    {
        public string Label { get; }
        public string Glyph { get; }
        public string Description { get; }
        public Color Accent { get; }
        public CompassAction Action { get; }
        public Color? TargetColor { get; }
        public List<CompassNode> Children { get; }
        public bool IsGroup => Children.Count > 0;

        public CompassNode(string label, string glyph, string description, Color accent,
            CompassAction action = CompassAction.None, Color? targetColor = null,
            IEnumerable<CompassNode>? children = null)
        {
            Label = label;
            Glyph = glyph;
            Description = description;
            Accent = accent;
            Action = action;
            TargetColor = targetColor;
            Children = children?.ToList() ?? new List<CompassNode>();
        }
    }

    private static readonly double[] CompassRingInner = { 34, 74, 114, 154 };
    private static readonly double[] CompassRingOuter = { 70, 110, 150, 190 };
    private static readonly int[] _compassSelection = { -1, -1, -1, -1 };

    private static CompassNode CNode(string label, string glyph, string desc, byte r, byte g, byte b,
        CompassAction action = CompassAction.None, Color? target = null, IEnumerable<CompassNode>? children = null) =>
        new(label, glyph, desc, Color.FromArgb(236, r, g, b), action, target, children);

    private static List<CompassNode> BuildCompassTree() => new()
    {
        CNode("形状", "✎", "线条与圈注工具", 92, 150, 210, children: new[]
        {
            CNode("直线", "─", "绘制直线", 208, 92, 72, CompassAction.Line),
            CNode("波浪线", "∿", "绘制波浪线", 72, 152, 196, CompassAction.Wave),
            CNode("双线", "═", "绘制双线", 132, 92, 196, CompassAction.DoubleLine),
            CNode("自由画", "✎", "自由手写", 92, 168, 116, CompassAction.Freehand),
            CNode("圈注", "◯", "手绘圈注", 196, 152, 72, CompassAction.Circle)
        }),
        CNode("颜色", "◐", "笔迹颜色（可再选色系）", 208, 120, 190, children: new[]
        {
            CNode("暖色", "☀", "红 / 橙 / 粉", 226, 118, 100, children: new[]
            {
                CNode("红色", "●", "红色笔迹", 255, 82, 82, CompassAction.SetColor, Color.FromRgb(255, 82, 82)),
                CNode("橙色", "●", "橙色笔迹", 255, 150, 60, CompassAction.SetColor, Color.FromRgb(255, 150, 60)),
                CNode("粉色", "●", "粉色笔迹", 244, 114, 182, CompassAction.SetColor, Color.FromRgb(244, 114, 182))
            }),
            CNode("冷色", "❄", "蓝 / 青 / 靛", 100, 160, 230, children: new[]
            {
                CNode("蓝色", "●", "蓝色笔迹", 64, 156, 255, CompassAction.SetColor, Color.FromRgb(64, 156, 255)),
                CNode("青色", "●", "青色笔迹", 64, 220, 216, CompassAction.SetColor, Color.FromRgb(64, 220, 216)),
                CNode("靛色", "●", "靛色笔迹", 116, 116, 246, CompassAction.SetColor, Color.FromRgb(116, 116, 246))
            }),
            CNode("自然", "♣", "绿 / 黄绿 / 金", 110, 200, 120, children: new[]
            {
                CNode("绿色", "●", "绿色笔迹", 96, 220, 120, CompassAction.SetColor, Color.FromRgb(96, 220, 120)),
                CNode("黄绿", "●", "黄绿色笔迹", 176, 220, 72, CompassAction.SetColor, Color.FromRgb(176, 220, 72)),
                CNode("金色", "●", "金色笔迹", 236, 196, 72, CompassAction.SetColor, Color.FromRgb(236, 196, 72))
            }),
            CNode("中性", "◆", "黑 / 灰 / 白", 150, 150, 160, children: new[]
            {
                CNode("黑色", "●", "黑色笔迹", 36, 40, 48, CompassAction.SetColor, Color.FromRgb(36, 40, 48)),
                CNode("灰色", "●", "灰色笔迹", 150, 156, 168, CompassAction.SetColor, Color.FromRgb(150, 156, 168)),
                CNode("白色", "●", "白色笔迹", 240, 244, 250, CompassAction.SetColor, Color.FromRgb(240, 244, 250))
            })
        }),
        CNode("橡皮", "▱", "擦除工具与擦头大小", 120, 128, 140, children: new[]
        {
            CNode("自由擦", "▱", "按住左键自由擦除", 156, 168, 184, CompassAction.EraserFree),
            CNode("点擦", "⌫", "点击擦除整个笔迹", 168, 128, 128, CompassAction.EraserClick),
            CNode("擦头＋", "＋", "增大擦头", 96, 168, 196, CompassAction.EraserBigger),
            CNode("擦头－", "－", "减小擦头", 96, 168, 196, CompassAction.EraserSmaller)
        }),
        CNode("粗细", "↕", "笔迹粗细", 96, 140, 168, children: new[]
        {
            CNode("极细", "·", "1 px", 128, 168, 200, CompassAction.Size1),
            CNode("细", "•", "2 px", 128, 168, 200, CompassAction.Size2),
            CNode("中", "●", "4 px", 128, 168, 200, CompassAction.Size3),
            CNode("粗", "⬤", "7 px", 128, 168, 200, CompassAction.Size4),
            CNode("极粗", "◉", "11 px", 128, 168, 200, CompassAction.Size5)
        }),
        CNode("工具", "⚙", "指针、清空与轮换", 168, 156, 96, children: new[]
        {
            CNode("指针", "✥", "返回指针（停止标注）", 82, 116, 168, CompassAction.Pointer),
            CNode("移动", "✜", "拖动已绘制的标注元素", 92, 176, 210, CompassAction.Move),
            CNode("换色", "◐", "轮换预设颜色", 92, 176, 196, CompassAction.ColorCycle),
            CNode("清空", "✕", "清空全部标注", 208, 88, 88, CompassAction.ClearAll)
        })
    };

    private ModifierKeys CompassModifiers => App.Settings.CompassModifier switch
    {
        CompassModifier.Shift => ModifierKeys.Shift,
        CompassModifier.Alt => ModifierKeys.Alt,
        CompassModifier.CtrlShift => ModifierKeys.Control | ModifierKeys.Shift,
        CompassModifier.CtrlAlt => ModifierKeys.Control | ModifierKeys.Alt,
        CompassModifier.ShiftAlt => ModifierKeys.Shift | ModifierKeys.Alt,
        _ => ModifierKeys.Control
    };

    private MouseButton CompassMouseButton => App.Settings.CompassButton == CompassButton.Middle
        ? MouseButton.Middle : MouseButton.Right;

    private bool IsCompassTrigger(MouseButtonEventArgs e) =>
        e.ChangedButton == CompassMouseButton && Keyboard.Modifiers == CompassModifiers;

    private bool HandleCompassDown(MouseButtonEventArgs e)
    {
        if (_sourcePixels == null || !IsCompassTrigger(e)) return false;
        OpenCompass(e.GetPosition(Root), e.GetPosition(ImageViewport));
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private bool HandleCompassMove(MouseEventArgs e)
    {
        if (!_compassOpen) return false;
        UpdateCompass(e.GetPosition(Root));
        e.Handled = true;
        return true;
    }

    private bool HandleCompassUp(MouseButtonEventArgs e)
    {
        if (!_compassOpen) return false;
        UpdateCompass(e.GetPosition(Root));

        // Invoke the highlighted leaf in the ring the pointer currently occupies, so
        // a click on a category group does not accidentally fire a previewed child.
        var ring = _compassActiveRing >= 0 ? _compassActiveRing : 0;
        var index = _compassSelection[ring];
        if (index >= 0)
        {
            var node = CompassNodeAt(ring, index);
            if (node is { IsGroup: false }) InvokeCompass(node);
        }

        CloseCompass();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        return true;
    }

    private void OpenCompass(Point rootPoint, Point imagePoint)
    {
        CloseCompass();
        _compassOrigin = rootPoint;
        _compassPointer = rootPoint;
        for (var i = 0; i < _compassSelection.Length; i++) _compassSelection[i] = -1;
        _compassOpen = true;
        _draftPointer = imagePoint;

        // Guide lines are added LAST so they float above every menu sector.
        _compassGuideHalo = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _compassGuideFlow = new Line
        {
            StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { 9, 7 },
            IsHitTestVisible = false
        };
        _compassGuideFlow.Effect = new DropShadowEffect
        {
            Color = Colors.Yellow, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9
        };
        var flowBrush = new SolidColorBrush(Colors.Yellow);
        _compassGuideFlow.Stroke = flowBrush;
        var colorAnim = new ColorAnimation
        {
            From = Colors.Yellow, To = Colors.Cyan, Duration = TimeSpan.FromMilliseconds(700),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
        };
        flowBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
        var dashAnim = new DoubleAnimation(0, -32, TimeSpan.FromMilliseconds(550))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _compassGuideFlow.BeginAnimation(Shape.StrokeDashOffsetProperty, dashAnim);

        _compassTooltip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 12, 15, 22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 8, 5), IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        _compassTooltipText = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 200
        };
        _compassTooltip.Child = _compassTooltipText;

        RebuildCompass();
        UpdateCompass(rootPoint);
    }

    /// <summary>Rebuild all menu sectors for the current active path. Cheap: a few dozen shapes at most.</summary>
    private void RebuildCompass()
    {
        foreach (var item in _compassVisuals) CompassLayer.Children.Remove(item);
        _compassVisuals.Clear();

        // Hub.
        var hub = new Ellipse
        {
            Width = CompassRingInner[0] * 2 - 6, Height = CompassRingInner[0] * 2 - 6,
            Fill = new SolidColorBrush(Color.FromArgb(244, 18, 21, 28)),
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1.2, IsHitTestVisible = false
        };
        hub.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.6 };
        Canvas.SetLeft(hub, _compassOrigin.X - (CompassRingInner[0] - 3));
        Canvas.SetTop(hub, _compassOrigin.Y - (CompassRingInner[0] - 3));
        CompassLayer.Children.Add(hub);
        _compassVisuals.Add(hub);
        AddCompassText("标注", _compassOrigin.X - 24, _compassOrigin.Y - 9, 48, 18, 11, Brushes.White, FontWeights.SemiBold);

        // One ring per active level. A ring only appears once its parent is highlighted.
        for (var ring = 0; ring < CompassRingInner.Length; ring++)
        {
            if (ring > 0 && _compassSelection[ring - 1] < 0) break;
            var nodes = CompassChildrenAt(ring);
            if (nodes.Count == 0) break;
            var span = 360.0 / nodes.Count;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var start = i * span - 90 + 1.2;
                var end = (i + 1) * span - 90 - 1.2;
                var highlighted = _compassSelection[ring] == i;
                var path = CreateSectorPath(_compassOrigin, CompassRingInner[ring], CompassRingOuter[ring], start, end, node.Accent, highlighted);
                CompassLayer.Children.Add(path);
                _compassVisuals.Add(path);

                var mid = (start + end) / 2;
                var radius = (CompassRingInner[ring] + CompassRingOuter[ring]) / 2;
                var labelPoint = Polar(_compassOrigin, radius, mid);
                var glyphSize = ring == 0 ? 14 : 12;
                AddCompassText(node.Glyph, labelPoint.X - 15, labelPoint.Y - 17, 30, 18, glyphSize, Brushes.White, FontWeights.SemiBold);
                // Category rings also print the name for at-a-glance recognition.
                if (ring <= 1)
                    AddCompassText(node.Label, labelPoint.X - 24, labelPoint.Y - 1, 48, 14, 9,
                        new SolidColorBrush(Color.FromArgb(235, 235, 240, 248)), FontWeights.Normal);
            }
        }

        // Tooltip, then guide lines last (topmost in the layer).
        if (_compassTooltip != null)
        {
            CompassLayer.Children.Add(_compassTooltip);
            _compassVisuals.Add(_compassTooltip);
        }
        if (_compassGuideHalo != null)
        {
            CompassLayer.Children.Add(_compassGuideHalo);
            _compassVisuals.Add(_compassGuideHalo);
        }
        if (_compassGuideFlow != null)
        {
            CompassLayer.Children.Add(_compassGuideFlow);
            _compassVisuals.Add(_compassGuideFlow);
        }
        UpdateCompassGuide();
    }

    private void AddCompassText(string text, double x, double y, double w, double h, double size, System.Windows.Media.Brush brush, FontWeight weight)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = brush,
            Width = w, Height = h, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        CompassLayer.Children.Add(label);
        _compassVisuals.Add(label);
    }

    private List<CompassNode> CompassChildrenAt(int ring)
    {
        var nodes = CompassTree;
        for (var i = 0; i < ring; i++)
        {
            var index = _compassSelection[i];
            if (index < 0 || index >= nodes.Count) return new List<CompassNode>();
            nodes = nodes[index].Children;
        }
        return nodes;
    }

    private CompassNode? CompassNodeAt(int ring, int index)
    {
        var nodes = CompassChildrenAt(ring);
        return index >= 0 && index < nodes.Count ? nodes[index] : null;
    }

    private Path CreateSectorPath(Point center, double inner, double outer, double start, double end, Color accent, bool highlighted)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Polar(center, inner, start), true, true);
            ctx.LineTo(Polar(center, outer, start), true, true);
            ctx.ArcTo(Polar(center, outer, end), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(Polar(center, inner, end), true, true);
            ctx.ArcTo(Polar(center, inner, start), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        var a = highlighted ? (byte)255 : accent.A;
        var boost = highlighted ? 34 : 0;
        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(Color.FromArgb(a,
                (byte)Math.Min(255, accent.R + boost),
                (byte)Math.Min(255, accent.G + boost),
                (byte)Math.Min(255, accent.B + boost))),
            Stroke = new SolidColorBrush(highlighted ? Colors.White : Color.FromArgb(190, 255, 255, 255)),
            StrokeThickness = highlighted ? 2 : 1,
            IsHitTestVisible = false
        };
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static Point Polar(Point center, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius);
    }

    private void UpdateCompass(Point pointer)
    {
        if (!_compassOpen) return;
        _compassPointer = pointer;
        var dx = pointer.X - _compassOrigin.X;
        var dy = pointer.Y - _compassOrigin.Y;
        var distance = Hypot(dx, dy);

        // Which ring is the pointer in? Gaps between rings keep the previous ring active.
        var activeRing = -1;
        for (var r = 0; r < CompassRingInner.Length; r++)
        {
            if (distance <= CompassRingOuter[r] + 14) { activeRing = r; break; }
        }
        if (distance < CompassRingInner[0] - 4) activeRing = -1; // hub: retract to the default single ring
        _compassActiveRing = activeRing;

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
        if (angle < 0) angle += 360;

        // Each hovered ring previews its child ring: hovering a category expands its
        // submenu; hovering a submenu expands its leaves. Moving back to the hub collapses all.
        var maxRing = activeRing < 0 ? -1 : Math.Min(activeRing + 1, CompassRingInner.Length - 1);
        var changed = false;
        for (var r = 0; r < _compassSelection.Length; r++)
        {
            int next;
            if (r > maxRing || (r > 0 && _compassSelection[r - 1] < 0))
            {
                next = -1; // parent not active -> retract this and all deeper levels
            }
            else
            {
                var nodes = CompassChildrenAt(r);
                next = nodes.Count == 0 ? -1 : (int)(angle / (360.0 / nodes.Count)) % nodes.Count;
            }
            if (next != _compassSelection[r]) { _compassSelection[r] = next; changed = true; }
        }

        if (changed) RebuildCompass();
        UpdateCompassGuide();
        UpdateCompassTooltip();
    }

    private void UpdateCompassGuide()
    {
        if (_compassGuideHalo == null || _compassGuideFlow == null) return;
        foreach (var line in new[] { _compassGuideHalo, _compassGuideFlow })
        {
            line.X1 = _compassOrigin.X;
            line.Y1 = _compassOrigin.Y;
            line.X2 = _compassPointer.X;
            line.Y2 = _compassPointer.Y;
        }
    }

    private void UpdateCompassTooltip()
    {
        if (_compassTooltip == null || _compassTooltipText == null) return;
        CompassNode? node = null;
        for (var r = _compassSelection.Length - 1; r >= 0; r--)
        {
            var index = _compassSelection[r];
            if (index < 0) continue;
            node = CompassNodeAt(r, index);
            if (node != null) break;
        }
        if (node == null)
        {
            _compassTooltip.Visibility = Visibility.Collapsed;
            return;
        }
        _compassTooltipText.Text = $"{node.Label}  —  {node.Description}";
        _compassTooltip.Visibility = Visibility.Visible;

        var x = Math.Min(_compassPointer.X + 18, CompassLayer.ActualWidth - 220);
        var y = Math.Min(_compassPointer.Y + 18, CompassLayer.ActualHeight - 56);
        Canvas.SetLeft(_compassTooltip, Math.Max(4, x));
        Canvas.SetTop(_compassTooltip, Math.Max(4, y));
        _compassTooltip.UpdateLayout();
    }

    private void CloseCompass()
    {
        _compassOpen = false;
        _compassActiveRing = -1;
        for (var i = 0; i < _compassSelection.Length; i++) _compassSelection[i] = -1;
        _compassGuideHalo = null;
        _compassGuideFlow = null;
        _compassTooltip = null;
        _compassTooltipText = null;
        foreach (var item in _compassVisuals) CompassLayer.Children.Remove(item);
        _compassVisuals.Clear();
    }

    private void InvokeCompass(CompassNode node)
    {
        switch (node.Action)
        {
            case CompassAction.Pointer: SetAnnotationTool(AnnotationTool.Pointer); break;
            case CompassAction.Move: SetAnnotationTool(AnnotationTool.Move); break;
            case CompassAction.Line: SetAnnotationTool(AnnotationTool.Line); break;
            case CompassAction.Wave: SetAnnotationTool(AnnotationTool.Wave); break;
            case CompassAction.DoubleLine: SetAnnotationTool(AnnotationTool.DoubleLine); break;
            case CompassAction.Freehand: SetAnnotationTool(AnnotationTool.Freehand); break;
            case CompassAction.Circle: SetAnnotationTool(AnnotationTool.Circle); break;
            case CompassAction.EraserFree: SetAnnotationTool(AnnotationTool.EraserFree); break;
            case CompassAction.EraserClick: SetAnnotationTool(AnnotationTool.EraserClick); break;
            case CompassAction.EraserBigger: _eraserSize = Math.Min(80, _eraserSize + 6); break;
            case CompassAction.EraserSmaller: _eraserSize = Math.Max(4, _eraserSize - 6); break;
            case CompassAction.Size1: _annotationSize = 1; break;
            case CompassAction.Size2: _annotationSize = 2; break;
            case CompassAction.Size3: _annotationSize = 4; break;
            case CompassAction.Size4: _annotationSize = 7; break;
            case CompassAction.Size5: _annotationSize = 11; break;
            case CompassAction.SetColor: if (node.TargetColor.HasValue) _annotationColor = node.TargetColor.Value; break;
            case CompassAction.ColorCycle:
                _annotationColor = _annotationColor == Color.FromRgb(255, 82, 82) ? Color.FromRgb(255, 214, 64)
                    : _annotationColor == Color.FromRgb(255, 214, 64) ? Color.FromRgb(64, 196, 255)
                    : _annotationColor == Color.FromRgb(64, 196, 255) ? Color.FromRgb(96, 220, 120)
                    : Color.FromRgb(255, 82, 82);
                break;
            case CompassAction.ClearAll:
                _annotations.Clear();
                AnnotationLayer.Children.Clear();
                break;
        }
        ShowToolBadge();
    }

    private void SetAnnotationTool(AnnotationTool tool)
    {
        _annotationTool = tool;
        Cursor = tool switch
        {
            AnnotationTool.Pointer => Cursors.Arrow,
            AnnotationTool.Move => Cursors.SizeAll,
            _ => Cursors.Cross
        };
    }

    private void ShowToolBadge()
    {
        var name = _annotationTool switch
        {
            AnnotationTool.Line => $"标注：直线 · {_annotationSize:0}",
            AnnotationTool.Wave => $"标注：波浪线 · {_annotationSize:0}",
            AnnotationTool.DoubleLine => $"标注：双线 · {_annotationSize:0}",
            AnnotationTool.Freehand => $"标注：自由画 · {_annotationSize:0}",
            AnnotationTool.Circle => $"标注：手绘圈注 · {_annotationSize:0}",
            AnnotationTool.EraserFree => $"标注：自由擦除 · {_eraserSize:0}",
            AnnotationTool.EraserClick => "标注：点击擦除元素",
            AnnotationTool.Move => "标注：移动元素（拖动已有笔迹）",
            _ => string.Empty
        };
        ToolBadgeText.Text = name;
        ToolBadge.Visibility = string.IsNullOrEmpty(name) ? Visibility.Collapsed : Visibility.Visible;
        ToolBadge.BorderBrush = new SolidColorBrush(_annotationColor);
    }

    // ---------------- annotation drawing ----------------

    private bool HandleAnnotationDown(MouseButtonEventArgs e)
    {
        if (_sourcePixels == null || _annotationTool == AnnotationTool.Pointer || e.ChangedButton != MouseButton.Left)
            return false;

        var point = e.GetPosition(ImageViewport);

        if (_annotationTool == AnnotationTool.Move)
        {
            var radius = Math.Max(12, _annotationSize * 2);
            for (var i = _annotations.Count - 1; i >= 0; i--)
            {
                if (!_annotations[i].HitTest(point, ToViewportSpace, radius)) continue;
                _movingAnnotation = i;
                _moveStartImage = ToImageSpace(point);
                _moveOriginalPoints = _annotations[i].Points.ToList();
                CaptureMouse();
                e.Handled = true;
                return true;
            }
            return false;
        }

        if (_annotationTool == AnnotationTool.EraserClick)
        {
            EraseAt(point, Math.Max(_eraserSize, 10));
            e.Handled = true;
            return true;
        }

        _annotating = true;
        _draftPoints = new List<Point> { ToImageSpace(point) };
        _draftPointer = point;
        RefreshDraftVisual();
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private bool HandleAnnotationMove(MouseEventArgs e)
    {
        if (_movingAnnotation >= 0 && _moveOriginalPoints != null)
        {
            var current = ToImageSpace(e.GetPosition(ImageViewport));
            var dx = current.X - _moveStartImage.X;
            var dy = current.Y - _moveStartImage.Y;
            var points = _annotations[_movingAnnotation].Points;
            for (var i = 0; i < points.Count; i++)
            {
                points[i] = new Point(_moveOriginalPoints[i].X + dx, _moveOriginalPoints[i].Y + dy);
            }
            RedrawAnnotations();
            e.Handled = true;
            return true;
        }

        if (!_annotating) return false;
        var point = e.GetPosition(ImageViewport);
        _draftPointer = point;

        if (_annotationTool == AnnotationTool.EraserFree)
        {
            EraseAt(point, _eraserSize);
            e.Handled = true;
            return true;
        }

        var normalized = ToImageSpace(point);
        var last = _draftPoints[^1];
        var lastViewport = ToViewportSpace(last);
        if (Hypot(point.X - lastViewport.X, point.Y - lastViewport.Y) >= 1.5)
        {
            _draftPoints.Add(normalized);
            RefreshDraftVisual();
        }
        e.Handled = true;
        return true;
    }

    private bool HandleAnnotationUp(MouseButtonEventArgs e)
    {
        if (_movingAnnotation >= 0)
        {
            _movingAnnotation = -1;
            _moveOriginalPoints = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
            return true;
        }

        if (!_annotating) return false;
        _annotating = false;
        ClearDraftVisual();

        if (_annotationTool != AnnotationTool.EraserFree && _draftPoints.Count >= 2)
        {
            _annotations.Add(new AnnotationStroke(_annotationTool, _draftPoints,
                _annotationColor, _annotationSize));
            RedrawAnnotations();
        }
        _draftPoints.Clear();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        return true;
    }

    private Point ToImageSpace(Point viewportPoint)
    {
        var left = Canvas.GetLeft(ImageView);
        var top = Canvas.GetTop(ImageView);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        var width = Math.Max(1, ImageView.ActualWidth);
        var height = Math.Max(1, ImageView.ActualHeight);
        return new Point((viewportPoint.X - left) / width, (viewportPoint.Y - top) / height);
    }

    private Point ToViewportSpace(Point imagePoint)
    {
        var left = Canvas.GetLeft(ImageView);
        var top = Canvas.GetTop(ImageView);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        return new Point(left + imagePoint.X * ImageView.ActualWidth,
            top + imagePoint.Y * ImageView.ActualHeight);
    }

    private void RedrawAnnotations()
    {
        AnnotationLayer.Children.Clear();
        foreach (var stroke in _annotations)
            AnnotationLayer.Children.Add(stroke.CreateVisual(ToViewportSpace));
    }

    private void RefreshDraftVisual()
    {
        ClearDraftVisual();
        if (_draftPoints.Count == 0) return;

        if (_annotationTool is AnnotationTool.EraserFree or AnnotationTool.EraserClick)
        {
            var cursor = new Ellipse
            {
                Width = _eraserSize * 2, Height = _eraserSize * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(cursor, _draftPointer.X - _eraserSize);
            Canvas.SetTop(cursor, _draftPointer.Y - _eraserSize);
            _draftVisual = cursor;
        }
        else
        {
            var draft = new AnnotationStroke(_annotationTool, _draftPoints, _annotationColor, _annotationSize);
            _draftVisual = draft.CreateVisual(ToViewportSpace);
        }
        AnnotationLayer.Children.Add(_draftVisual);
    }

    private void ClearDraftVisual()
    {
        if (_draftVisual != null) AnnotationLayer.Children.Remove(_draftVisual);
        _draftVisual = null;
    }

    private void EraseAt(Point viewportPoint, double radius)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].HitTest(viewportPoint, ToViewportSpace, radius))
            {
                _annotations.RemoveAt(i);
                RedrawAnnotations();
                return;
            }
        }
    }
}
