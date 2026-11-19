using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace WBImageViewer.Views;

public partial class ColorPaletteWindow : Window
{
    public Color SelectedColor { get; set; } = Color.FromRgb(255, 82, 82);

    private static readonly (string Name, string Hex)[] Presets =
    {
        ("红", "#F04848"), ("橙", "#F48C34"), ("黄", "#ECC838"), ("绿", "#50CE6C"), ("青", "#38D0D6"), ("蓝", "#4096F6"),
        ("紫", "#A86EE2"), ("粉", "#EE76B0"), ("棕", "#9C6644"), ("黑", "#20242C"), ("灰", "#969CA8"), ("白", "#F4F6FA"),
        ("荧光黄", "#F3FF41"), ("荧光绿", "#66FF7A"), ("荧光粉", "#FF6FD8"), ("天蓝", "#7FD6FF"), ("金", "#E8B84B"), ("银", "#C9D2DC")
    };

    public ColorPaletteWindow()
    {
        InitializeComponent();
        foreach (var (name, hex) in Presets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var button = new Button
            {
                Width = 34, Height = 30, Margin = new Thickness(3),
                Background = new SolidColorBrush(color), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{name} {hex}", Tag = color,
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) =>
            {
                SelectedColor = color;
                HexBox.Text = hex;
                Preview.Background = new SolidColorBrush(color);
            };
            SwatchGrid.Children.Add(button);
        }
        HexBox.Text = "#" + SelectedColor.R.ToString("X2") + SelectedColor.G.ToString("X2") + SelectedColor.B.ToString("X2");
        Preview.Background = new SolidColorBrush(SelectedColor);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SelectedColor = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim());
            DialogResult = true;
            Close();
        }
        catch
        {
            MessageBox.Show("颜色格式无效，请用 #RRGGBB 形式。", "调色板", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
