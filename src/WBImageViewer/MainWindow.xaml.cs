using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
using Cursor = System.Windows.Input.Cursor;
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

    private ImageAdjustments _adjustments = new();
    private readonly DispatcherTimer _adjustTimer;
    private bool _updatingControls;

    private bool _movingWindow;
    private bool _panningImage;
    private Cursor? _grabCursor;
    private Cursor? _previousCursor;
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

        LoadGrabCursor();
        CloseHost.MouseEnter += (_, _) => AnimateCloseButton(2);
        CloseHost.MouseLeave += (_, _) => AnimateCloseButton(-30);
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

    private void LoadGrabCursor()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("Assets/grab.cur", UriKind.Relative));
            if (resource?.Stream != null)
            {
                using var stream = resource.Stream;
                _grabCursor = new Cursor(stream);
            }
        }
        catch
        {
            _grabCursor = null;
        }
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
            var color = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
            var brush = new SolidColorBrush(color);
            Root.Background = brush;
            Background = brush;
        }
        catch
        {
            // keep the previous valid colour
        }

        Opacity = Math.Clamp(settings.PanelOpacity, 0.2, 1.0);
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
            ImageTranslate.X = ImageTranslate.Y = 0;

            _adjustments = App.Settings.AutoOptimize
                ? ImageAdjustments.SmartPreset()
                : (App.Settings.Adjustments ?? new ImageAdjustments()).Clone();
            UpdateAdjustmentControls();
            ApplyAdjustments();

            ImageView.Source = _display;
            Hint.Visibility = Visibility.Collapsed;
            Title = $"{AppInfo.Name} - {Path.GetFileName(path)}";

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

    private void AnimateCloseButton(double targetY)
    {
        var animation = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        CloseTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    public void ExecuteHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleTopMost:
                Topmost = !Topmost;
                PinButton.Content = Topmost ? "⌃" : "⌄";
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
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width < 8 || height < 8) return;

        _zoom = Math.Min(width / _pixelWidth, height / _pixelHeight);
        ApplyImageSize();
        ImageTranslate.X = ImageTranslate.Y = 0;
    }

    private void ApplyImageSize()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        ImageView.Width = _pixelWidth * _zoom;
        ImageView.Height = _pixelHeight * _zoom;
    }

    private void FitWindowToImage()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(_pixelWidth * _zoom * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(_pixelHeight * _zoom * dpi.DpiScaleY));
        var area = WorkAreaPhysical();
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);

        var left = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        Win32.SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_sourcePixels == null) return;
        e.Handled = true;

        var factor = e.Delta > 0 ? App.Settings.ZoomStep : 1.0 / App.Settings.ZoomStep;
        ZoomAt(e.GetPosition(Root), factor);

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
        var oldLeft = (Root.ActualWidth - oldWidth) / 2.0;
        var oldTop = (Root.ActualHeight - oldHeight) / 2.0;

        // Image is centred by layout and shifted only by ImageTranslate - no centre transform origin.
        var relativeX = (point.X - oldLeft - ImageTranslate.X) / oldWidth;
        var relativeY = (point.Y - oldTop - ImageTranslate.Y) / oldHeight;

        var newWidth = _pixelWidth * newZoom;
        var newHeight = _pixelHeight * newZoom;
        var newLeft = (Root.ActualWidth - newWidth) / 2.0;
        var newTop = (Root.ActualHeight - newHeight) / 2.0;

        ImageTranslate.X = point.X - newLeft - relativeX * newWidth;
        ImageTranslate.Y = point.Y - newTop - relativeY * newHeight;
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
            _lastPanPosition = e.GetPosition(Root);
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
            var current = e.GetPosition(Root);
            ImageTranslate.X += current.X - _lastPanPosition.X;
            ImageTranslate.Y += current.Y - _lastPanPosition.Y;
            _lastPanPosition = current;
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

    private void SetGrabCursor()
    {
        _previousCursor = Cursor;
        Cursor = _grabCursor ?? Cursors.Hand;
    }

    private void RestoreCursor()
    {
        if (_previousCursor != null) Cursor = _previousCursor;
        _previousCursor = null;
    }

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
