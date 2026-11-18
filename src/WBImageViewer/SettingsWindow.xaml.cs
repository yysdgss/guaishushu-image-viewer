using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WBImageViewer.Models;
using WBImageViewer.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ColorDialog = System.Windows.Forms.ColorDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WBImageViewer;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var settings = App.Settings;
        SingleWindowBox.IsChecked = settings.SingleWindow;
        SelectByTag(MoveModeBox, settings.MoveMode.ToString());
        TopMostBox.Text = settings.ToggleTopMostHotkey;
        FitWindowBox.Text = settings.FitWindowToImageHotkey;
        FitImageBox.Text = settings.FitImageToWindowHotkey;
        SelectByTag(WheelModifierBox, settings.WheelWindowModifiers);
        ZoomStepBox.Text = settings.ZoomStep.ToString(CultureInfo.InvariantCulture);
        CornerRadiusBox.Text = settings.CornerRadius.ToString(CultureInfo.InvariantCulture);
        BackgroundColorBox.Text = settings.BackgroundColor;
        OpacitySlider.Value = Math.Clamp(settings.PanelOpacity, 0.2, 1.0);
        AutoOptimizeBox.IsChecked = settings.AutoOptimize;
        AssociateBox.IsChecked = settings.AssociateAsDefaultViewer;

        foreach (var box in new[] { TopMostBox, FitWindowBox, FitImageBox })
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;

        if (e.Key == Key.Escape)
        {
            box.Text = string.Empty;
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(e.Key.ToString());
        box.Text = string.Join("+", parts);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null)
            OpacityLabel.Text = $"面板背景不透明度（图片不受影响） {OpacitySlider.Value:P0}";
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new ColorDialog { FullOpen = true };
        try
        {
            var current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                .ConvertFromString(BackgroundColorBox.Text);
            dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        }
        catch { /* keep the default dialog colour */ }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            BackgroundColorBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private void ChooseDefault_Click(object sender, RoutedEventArgs e)
    {
        FileAssociationService.OpenChooseAppDialog();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var topMost = TopMostBox.Text.Trim();
        var fitWindow = FitWindowBox.Text.Trim();
        var fitImage = FitImageBox.Text.Trim();

        if (!HotkeySpec.TryParse(topMost, out _, out _) ||
            !HotkeySpec.TryParse(fitWindow, out _, out _) ||
            !HotkeySpec.TryParse(fitImage, out _, out _))
        {
            MessageBox.Show("快捷键必须包含 Shift / Ctrl / Alt 中至少一个修饰键，以及一个普通按键。",
                "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var hotkeys = new[] { topMost, fitWindow, fitImage };
        if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
        {
            MessageBox.Show("三个全局快捷键不能重复。", "设置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(ZoomStepBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var step)
            || step <= 1.01 || step > 4)
        {
            MessageBox.Show("缩放步长请输入 1.01 到 4 之间的数字。", "设置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(CornerRadiusBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var radius)
            || radius < 0 || radius > 24)
        {
            MessageBox.Show("圆角半径请输入 0 到 24 之间的整数。", "设置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var background = BackgroundColorBox.Text.Trim();
        try
        {
            System.Windows.Media.ColorConverter.ConvertFromString(background);
        }
        catch
        {
            MessageBox.Show("背景颜色请输入 #RRGGBB 格式，例如 #101216。", "设置无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var opacity = Math.Clamp(OpacitySlider.Value, 0.2, 1.0);
        var settings = new AppSettings
        {
            SingleWindow = SingleWindowBox.IsChecked == true,
            MoveMode = Enum.TryParse<WindowMoveMode>(SelectedTag(MoveModeBox), ignoreCase: true, out var mode)
                ? mode : WindowMoveMode.RightButton,
            ToggleTopMostHotkey = topMost,
            FitWindowToImageHotkey = fitWindow,
            FitImageToWindowHotkey = fitImage,
            WheelWindowModifiers = SelectedTag(WheelModifierBox) ?? "Ctrl",
            ZoomStep = step,
            CornerRadius = radius,
            BackgroundColor = background,
            PanelOpacity = opacity,
            AutoOptimize = AutoOptimizeBox.IsChecked == true,
            AssociateAsDefaultViewer = AssociateBox.IsChecked == true,
            Adjustments = App.Settings.Adjustments?.Clone() ?? new ImageAdjustments(),
            PreviousAssociations = new Dictionary<string, string?>(App.Settings.PreviousAssociations)
        };

        App.ApplySettings(settings);
        Close();
    }
}
