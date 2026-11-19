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
    private readonly List<AnnotationFill> _fills = new();
    private readonly List<AnnotationText> _textCards = new();
    private AnnotationTool _annotationTool = AnnotationTool.Pointer;
    private Color _annotationColor = Color.FromRgb(255, 82, 82);
    private double _annotationSize = 3;
    private double _eraserSize = 16;
    private double _annotationOpacity = 0.45;
    private bool _annotationHighlighter;
    private InkStrokeStyle _annotationLineStyle = InkStrokeStyle.HandDrawn;
    private int _draftSeed = 1;
    private bool _annotating;
    private List<Point> _draftPoints = new();
    private Point _draftPointer;
    private UIElement? _draftVisual;
    private int _movingAnnotation = -1;
    private Point _moveStartImage;
    private List<Point>? _moveOriginalPoints;
    private string _currentImagePath = string.Empty;
    private string _currentProjectName = string.Empty;

    // Element selection & editing (line endpoints, circle bounds, move, delete).
    private enum ElementKind { None, Stroke, Fill, Text }
    private ElementKind _selectedKind;
    private int _selectedIndex = -1;
    private bool _editingElement;
    private int _editingHandle = -1;
    private Point _editStartImage;
    private Point _editOriginalTopLeft;
    private Point _editOriginalBottomRight;
    private List<Point>? _editOriginalPoints;

    // Region selection for screenshot translate / OCR.
    private enum RegionAction { None, Translate, Ocr }
    private RegionAction _regionAction;
    private bool _regionSelecting;
    private Point _regionStartViewport;
    private Point _regionEndViewport;
    private UIElement? _regionVisual;

    // Hierarchical compass state. Three contexts: canvas (file ops), image (ink), element (edit).
    private enum CompassContext { Canvas, Image, Element }
    private CompassContext _compassContext;
    private static readonly List<CompassNode> CanvasCompassTree = BuildCanvasCompassTree();
    private static readonly List<CompassNode> ImageCompassTree = BuildCompassTree();
    private static readonly List<CompassNode> ElementCompassTree = BuildElementCompassTree();
    private readonly List<UIElement> _compassVisuals = new();
    private bool _compassOpen;
    private Point _compassOrigin;
    private Point _compassPointer;
    private int _compassActiveRing = -1;
    private int[] _compassSelection = { -1, -1, -1, -1 };
    private enum CompassTrigger { None, Pointer, Line }
    private CompassTrigger[] _compassTrigger = { CompassTrigger.None, CompassTrigger.None, CompassTrigger.None, CompassTrigger.None };
    private int _compassPreviewRing = -1;
    private Line? _compassGuideHalo;
    private Line? _compassGuideFlow;
    private Border? _compassTooltip;
    private TextBlock? _compassTooltipText;

    // Bottom filmstrip / playlist. A dropped batch takes precedence over the directory list.
    private sealed class FilmItem
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public BitmapSource? Thumbnail { get; set; }
        public System.Windows.Media.Brush Brush { get; set; } = System.Windows.Media.Brushes.Transparent;
    }
    private readonly List<FilmItem> _playlist = new();
    private bool _playlistFromDrop;
    private bool _suppressFilmSelection;

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
        ToolBadge.MouseEnter += (_, _) => ToolBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(170, 108, 182, 255));
        ToolBadge.MouseLeave += (_, _) => ToolBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
        BuildToolBadgeMenu();
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
        _annotationSize = settings.DefaultPenWidth;
        _annotationOpacity = settings.DefaultOpacity;
        _annotationHighlighter = settings.DefaultHighlighter;
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

    /// <summary>Load a screenshot bitmap captured from the screen into this window.</summary>
    public void ShowCaptured(System.Windows.Media.Imaging.BitmapSource bitmap, string title)
    {
        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
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
        _adjustments = new ImageAdjustments();
        UpdateAdjustmentControls();
        ApplyAdjustments();
        ImageView.Source = _display;
        Hint.Visibility = Visibility.Collapsed;
        Title = $"{AppInfo.Name} - {title}";
        _currentImagePath = string.Empty;
        _currentProjectName = title;
        ApplyImagePosition();
        if (IsLoaded) FitImageToWindow();
        _playlist.Clear();
        _playlistFromDrop = false;
        RefreshFilmstrip();
    }

    public void LoadImage(string path)
    {
        _currentImagePath = path;
        _currentProjectName = System.IO.Path.GetFileName(path);
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
            SyncPlaylistForCurrentImage();
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

    private bool IsFromFilmstripOrNavZone(RoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null)
        {
            if (ReferenceEquals(element, PrevZone) || ReferenceEquals(element, NextZone) ||
                ReferenceEquals(element, FilmstripHost) || ReferenceEquals(element, FilmstripBar) ||
                ReferenceEquals(element, ToolBadgeMenu) || ReferenceEquals(element, ToolBadge))
                return true;
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
            case HotkeyAction.ScreenshotRegion:
                StartScreenshot(ScreenshotMode.Region);
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
        if (IsFromButton(e) || IsFromFilmstripOrNavZone(e)) return;
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
        if (image != null) App.OpenDocument(image);
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
}
