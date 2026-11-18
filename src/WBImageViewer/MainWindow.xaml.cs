using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private readonly DispatcherTimer _clockTimer;
    private bool _updatingControls;

    private bool _movingWindow;
    private bool _panningImage;
    private bool _fittingWindow;
    private Win32.POINT _moveCursorStart;
    private Win32.RECT _moveWindowStart;
    private Point _lastPanPosition;

    public MainWindow()
    {
        InitializeComponent();
        AllowDrop = true;
        _adjustTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _adjustTimer.Tick += (_, _) =>
        {
            _adjustTimer.Stop();
            ApplyAdjustments();
        };

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _clockTimer.Tick += (_, _) => UpdateIslandClock();
        UpdateIslandClock();
        _clockTimer.Start();

        IslandHitArea.MouseEnter += (_, _) => AnimateIsland(true);
        IslandHitArea.MouseLeave += (_, _) => AnimateIsland(false);
        CloseHost.MouseEnter += (_, _) => AnimateCloseButton(2);
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
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();

            _pixelWidth = bitmap.PixelWidth;
            _pixelHeight = bitmap.PixelHeight;
            _sourcePixels = new int[_pixelWidth * _pixelHeight];
            bitmap.CopyPixels(_sourcePixels, _pixelWidth * 4, 0);
            bitmap.Freeze();

            _displayPixels = new int[_sourcePixels.Length];
            _display = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
            _zoom = 1;
            _offsetX = _offsetY = 0;

            _adjustments = App.Settings.AutoOptimize
                ? ImageAdjustments.SmartPreset()
                : (App.Settings.Adjustments ?? new ImageAdjustments()).Clone();
            UpdateAdjustmentControls();
            ApplyAdjustments();

            ImageView.Source = _display;
            Hint.Visibility = Visibility.Collapsed;
            Title = $"{AppInfo.Name} - {Path.GetFileName(path)}";
            ApplyImagePosition();

            if (IsLoaded) FitImageToWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开图片：{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void UpdateIslandClock()
    {
        IslandClock.Text = DateTime.Now.ToString("HH:mm");
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
    /// Reflect topmost state on the pushpin: pinned is bright and upright (driven in),
    /// unpinned is dimmed and tilted (pulled out). The visual states live in XAML
    /// DataTriggers keyed off the button Tag, so hover feedback remains intact.
    /// </summary>
    private void UpdatePinState()
    {
        PinButton.Tag = Topmost ? "Pinned" : "Unpinned";
        PinButton.ToolTip = Topmost
            ? "取消置顶 (Shift+Ctrl+Alt+T)"
            : "置顶窗口 (Shift+Ctrl+Alt+T)";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    public void ExecuteHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleTopMost:
                Topmost = !Topmost;
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
        _movingWindow = false;
        _panningImage = false;
        RestoreCursor();
    }

    /// <summary>
    /// Use Windows' built-in hand cursor while dragging; no custom .cur resource is loaded.
    /// Hovering over the image keeps the normal arrow because no Cursor override is active.
    /// </summary>
    private void SetGrabCursor() => Cursor = Cursors.Hand;

    private void RestoreCursor() => Cursor = Cursors.Arrow;

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
    }
}
