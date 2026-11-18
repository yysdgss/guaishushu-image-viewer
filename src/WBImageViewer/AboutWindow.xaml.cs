using System.Windows;
using WBImageViewer.Models;

namespace WBImageViewer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductName.Text = AppInfo.Name;
        VersionText.Text = $"版本 {AppInfo.Version} · MIT 免费开源";
        FormatsText.Text = SupportedFormats.DisplayText;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
