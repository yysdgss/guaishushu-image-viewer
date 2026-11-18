using System.IO;
using System.Windows;
using System.Windows.Forms;
using WBImageViewer.Models;
using WBImageViewer.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace WBImageViewer;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static HotkeyHost Hotkeys { get; private set; } = null!;

    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsService.Load();

        if (e.Args.Any(arg => arg == "--uninstall"))
        {
            try
            {
                var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GuaishushuViewer");
                foreach (var folder in new[]
                         {
                             Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                             Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                         })
                {
                    var shortcut = Path.Combine(folder, $"{AppInfo.Name}.lnk");
                    if (File.Exists(shortcut)) File.Delete(shortcut);
                }
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer", false);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\GuaishushuViewer.Image", false);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
            catch { /* best effort */ }
            Shutdown();
            return;
        }

        if (e.Args.Any(arg => arg is "--set-default" or "--setdefault"))
        {
            try { FileAssociationService.Apply(Settings); } catch { /* best effort */ }
            FileAssociationService.OpenChooseAppDialog();
            Shutdown();
            return;
        }

        try
        {
            FileAssociationService.Apply(Settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Association failed: {ex.Message}");
        }

        Hotkeys = new HotkeyHost();
        Hotkeys.Pressed += OnGlobalHotkey;
        Hotkeys.Reconfigure(Settings);

        var files = e.Args.Where(ImageFile.IsSupported).ToArray();

        if (Settings.SingleWindow && ImagePipe.TrySend(files))
        {
            Shutdown();
            return;
        }

        if (Settings.SingleWindow)
            ImagePipe.Start(OpenFiles);

        SetupTray();
        OpenFiles(files);

        if (Current.Windows.Count == 0)
            OpenFiles(Array.Empty<string>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Hotkeys?.Dispose();
        base.OnExit(e);
    }

    private static void OnGlobalHotkey(HotkeyAction action)
    {
        var target = Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                     ?? Current.MainWindow as MainWindow
                     ?? Current.Windows.OfType<MainWindow>().FirstOrDefault();
        target?.ExecuteHotkey(action);
    }

    private void OpenFiles(IReadOnlyList<string> files)
    {
        if (Settings.SingleWindow)
        {
            var existing = Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (files.Count > 0) existing.LoadImage(files[0]);
                existing.Show();
                existing.Activate();
                return;
            }

            var window = new MainWindow();
            Current.MainWindow = window;
            if (files.Count > 0) window.LoadImage(files[0]);
            window.Show();
            return;
        }

        if (files.Count == 0)
        {
            var window = new MainWindow();
            window.Show();
            Current.MainWindow ??= window;
            return;
        }

        foreach (var file in files)
        {
            var window = new MainWindow();
            window.LoadImage(file);
            window.Show();
            Current.MainWindow ??= window;
        }
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开后台设置", null, (_, _) => ShowSettings());
        menu.Items.Add("关于", null, (_, _) => ShowAbout());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        menu.Items.Add(new ToolStripSeparator());
        var status = new ToolStripLabel($"{AppInfo.Name}  v{AppInfo.Version}")
        {
            ForeColor = System.Drawing.Color.FromArgb(0x70, 0x75, 0x80),
            Font = new System.Drawing.Font("Segoe UI", 8)
        };
        menu.Items.Add(status);

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = $"{AppInfo.Name} v{AppInfo.Version}",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative));
            if (resource?.Stream != null)
                return new System.Drawing.Icon(resource.Stream);
        }
        catch
        {
            try
            {
                var path = Environment.ProcessPath;
                if (path != null) return System.Drawing.Icon.ExtractAssociatedIcon(path) ?? System.Drawing.SystemIcons.Application;
            }
            catch
            {
                // use the built-in icon
            }
        }
        return System.Drawing.SystemIcons.Application;
    }

    public static void ShowSettings()
    {
        var existing = Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        new SettingsWindow().Show();
    }

    public static void ShowAbout()
    {
        var existing = Current.Windows.OfType<AboutWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        new AboutWindow().Show();
    }

    public static void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        try
        {
            FileAssociationService.Apply(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置已保存，但文件关联修改失败：{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SettingsService.Save(settings);
        Hotkeys.Reconfigure(settings);
        foreach (var window in Current.Windows.OfType<MainWindow>())
        {
            window.ApplyVisualSettings(settings);
            window.ApplySavedAdjustments();
        }
    }
}
