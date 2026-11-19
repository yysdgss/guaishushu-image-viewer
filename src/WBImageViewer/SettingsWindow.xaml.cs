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
        TopmostCoordinator.Register(this);

        var settings = App.Settings;
        SingleWindowBox.IsChecked = settings.SingleWindow;
        SelectByTag(MoveModeBox, settings.MoveMode.ToString());
        SelectByTag(CompassModifierBox, settings.CompassModifier.ToString());
        SelectByTag(CompassButtonBox, settings.CompassButton.ToString());
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
        PenWidthBox.Text = settings.DefaultPenWidth.ToString(CultureInfo.InvariantCulture);
        AiEndpointBox.Text = settings.AiEndpoint;
        AiModelBox.Text = settings.AiModel;
        AiKeyBox.Text = settings.AiKey;
        AiLanguageBox.Text = settings.AiTargetLanguage;

        foreach (var box in new[] { TopMostBox, FitWindowBox, FitImageBox })
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;

        CheckAllConflicts();
    }

    private (TextBox Box, TextBlock Status, string Current)[] HotkeyRows => new[]
    {
        (TopMostBox, TopMostConflict, App.Settings.ToggleTopMostHotkey),
        (FitWindowBox, FitWindowConflict, App.Settings.FitWindowToImageHotkey),
        (FitImageBox, FitImageConflict, App.Settings.FitImageToWindowHotkey)
    };

    private void CheckAllConflicts()
    {
        var rows = HotkeyRows;
        foreach (var (box, status, current) in rows)
        {
            var spec = box.Text.Trim();
            if (spec.Length == 0)
            {
                status.Text = "未设置";
                status.Foreground = System.Windows.Media.Brushes.Gray;
                continue;
            }
            var others = rows.Where(r => !ReferenceEquals(r.Box, box))
                             .Select(r => r.Box.Text.Trim()).Where(s => s.Length > 0).ToList();
            bool ownedByUs = string.Equals(spec, current, StringComparison.OrdinalIgnoreCase);
            var result = HotkeyConflictService.Check(spec, others, ownedByUs);
            status.Text = result.Message;
            status.Foreground = result.State switch
            {
                HotkeyConflictState.Available => System.Windows.Media.Brushes.SeaGreen,
                HotkeyConflictState.Occupied => System.Windows.Media.Brushes.Firebrick,
                HotkeyConflictState.Reserved => System.Windows.Media.Brushes.DarkOrange,
                _ => System.Windows.Media.Brushes.Gray
            };
        }
    }

    private void CheckConflicts_Click(object sender, RoutedEventArgs e) => CheckAllConflicts();

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

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;

        if (e.Key == Key.Escape)
        {
            box.Text = string.Empty;
            CheckAllConflicts();
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
        CheckAllConflicts();
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

    private void ApplyDefaults_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "GuaishushuViewer.exe");
            var errors = DefaultAssociationService.ApplyAll(exe, SupportedFormats.All);
            MessageBox.Show(errors.Count == 0
                    ? "已把支持的图片、脑图与流程图格式设为默认打开方式，并注入右键菜单。"
                    : "部分格式未成功：\n" + string.Join("\n", errors),
                "怪叔叔看图", MessageBoxButton.OK,
                errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("应用默认关联失败：" + ex.Message, "怪叔叔看图",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestAi_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = AiEndpointBox.Text.Trim();
        var model = AiModelBox.Text.Trim();
        var key = AiKeyBox.Text.Trim();
        if (endpoint.Length == 0 || model.Length == 0)
        {
            MessageBox.Show("请先填写接口地址与模型名称。", "API 测试", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var saved = App.Settings;
        var previous = (saved.AiEndpoint, saved.AiModel, saved.AiKey);
        saved.AiEndpoint = endpoint;
        saved.AiModel = model;
        saved.AiKey = key;
        try
        {
            var reply = await AiService.TranslateTextAsync("hello");
            MessageBox.Show($"连接成功！模型返回：\n{reply}", "API 测试", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接失败：{ex.Message}", "API 测试", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            saved.AiEndpoint = previous.AiEndpoint;
            saved.AiModel = previous.AiModel;
            saved.AiKey = previous.AiKey;
        }
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

        // Warn about external conflicts / system-reserved combos, but let the user save:
        // conflict detection is advisory and Windows may free the combo later.
        var conflicts = hotkeys
            .Select((spec, i) => HotkeyConflictService.Check(spec,
                hotkeys.Where((_, j) => j != i).ToList(),
                string.Equals(spec, HotkeyRows[i].Current, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r.State is HotkeyConflictState.Occupied or HotkeyConflictState.Reserved)
            .ToList();
        if (conflicts.Count > 0)
        {
            var detail = string.Join("\n", conflicts.Select(c => $"· {c.Spec}：{c.Message}"));
            var answer = MessageBox.Show(
                "以下快捷键存在冲突，保存后可能无法生效：\n" + detail + "\n\n仍然保存？",
                "快捷键冲突", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
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
            CompassModifier = Enum.TryParse<CompassModifier>(SelectedTag(CompassModifierBox), ignoreCase: true, out var compassModifier)
                ? compassModifier : CompassModifier.Ctrl,
            CompassButton = Enum.TryParse<CompassButton>(SelectedTag(CompassButtonBox), ignoreCase: true, out var compassButton)
                ? compassButton : CompassButton.Right,
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
            DefaultPenWidth = double.TryParse(PenWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pw)
                ? Math.Clamp(pw, 1, 24) : 3,
            AiEndpoint = AiEndpointBox.Text.Trim(),
            AiModel = AiModelBox.Text.Trim(),
            AiKey = AiKeyBox.Text.Trim(),
            AiTargetLanguage = string.IsNullOrWhiteSpace(AiLanguageBox.Text) ? "简体中文" : AiLanguageBox.Text.Trim(),
            Adjustments = App.Settings.Adjustments?.Clone() ?? new ImageAdjustments(),
            PreviousAssociations = new Dictionary<string, string?>(App.Settings.PreviousAssociations)
        };

        App.ApplySettings(settings);
        Close();
    }
}
