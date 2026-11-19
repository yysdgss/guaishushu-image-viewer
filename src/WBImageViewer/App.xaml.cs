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
                    "怪叔叔看图");
                var legacyDest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\怪叔叔看图", false);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\怪叔叔看图.Image", false);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer", false);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Classes\GuaishushuViewer.Image", false);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                if (Directory.Exists(legacyDest)) Directory.Delete(legacyDest, true);
            }
            catch { /* best effort */ }
            Shutdown();
            return;
        }

        if (e.Args.Any(arg => arg is "--set-default" or "--setdefault"))
        {
            try
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppInfo.Name + ".exe");
                var errors = DefaultAssociationService.ApplyAll(exe, SupportedFormats.All);
                if (errors.Count > 0)
                    MessageBox.Show("部分格式未能设为默认：\n" + string.Join("\n", errors),
                        "怪叔叔看图", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置默认打开程序失败：" + ex.Message,
                    "怪叔叔看图", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        NotifyHotkeyFailures();
        if (files.Length == 1 && string.Equals(System.IO.Path.GetExtension(files[0]), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
            OpenDocument(files[0]);
        else
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

    private void NotifyHotkeyFailures()
    {
        var failed = Hotkeys.FailedRegistrations;
        if (failed.Count == 0 || _tray is null) return;

        // Resolve the likely owner of each conflicting combo for a useful message.
        var lines = failed.Select(f =>
        {
            var spec = f[(f.IndexOf(':') + 1)..].Trim();
            var info = HotkeyConflictService.Check(spec, Array.Empty<string>(), ownedByUs: false);
            return info.State == HotkeyConflictState.Occupied && info.Suspects.Count > 0
                ? $"{spec} → {string.Join("、", info.Suspects)}"
                : $"{spec} → {info.Message}";
        });
        var detail = string.Join("\n", lines);
        _tray.ShowBalloonTip(8000, "快捷键冲突",
            "以下快捷键未能注册，可能与其他软件冲突：\n" + detail + "\n请在后台设置中修改。",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private static void OnGlobalHotkey(HotkeyAction action)
    {
        var target = Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                     ?? Current.MainWindow as MainWindow
                     ?? Current.Windows.OfType<MainWindow>().FirstOrDefault();
        target?.ExecuteHotkey(action);
    }

    /// <summary>Open an image or a .gjpg editable project, choosing the right loader.</summary>
    public static void OpenDocument(string path)
    {
        if (string.Equals(System.IO.Path.GetExtension(path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var project = Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                          ?? Current.MainWindow as MainWindow
                          ?? Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (project != null && project.LoadProject(path))
            {
                project.Show();
                project.Activate();
                return;
            }
        }
        (Current as App)?.OpenFiles(new[] { path });
    }

    /// <summary>Show a captured bitmap in a new, topmost pinned viewer window.</summary>
    public static void OpenBitmap(System.Windows.Media.Imaging.BitmapSource bitmap, string title)
    {
        var window = new MainWindow();
        Current.MainWindow ??= window;
        window.ShowCaptured(bitmap, title);
        window.Show();
        window.Topmost = true;
        window.Activate();
    }

    private void OpenFiles(IReadOnlyList<string> files)
    {
        if (Settings.SingleWindow)
        {
            var existing = Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (files.Count > 1) existing.SetPlaylist(files, true);
                if (files.Count > 0) LoadInto(files[0], existing);
                existing.Show();
                existing.Activate();
                return;
            }

            var window = new MainWindow();
            Current.MainWindow = window;
            if (files.Count > 1) window.SetPlaylist(files, true);
            if (files.Count > 0) LoadInto(files[0], window);
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
            LoadInto(file, window);
            window.Show();
            Current.MainWindow ??= window;
        }
    }

    private static void LoadInto(string path, MainWindow window)
    {
        if (string.Equals(System.IO.Path.GetExtension(path), GjpgService.Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (window.LoadProject(path)) return;
        }
        window.LoadImage(path);
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开后台设置", null, (_, _) => ShowSettings());
        menu.Items.Add("关于", null, (_, _) => ShowAbout());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        menu.Items.Add(new ToolStripSeparator());
        var status = new ToolStripLabel($"{AppInfo.Name}{Environment.NewLine}v{AppInfo.Version}")
        {
            AutoSize = false,
            Size = new System.Drawing.Size(154, 34),
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
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

    /// <summary>Open the settings panel with the AI section highlighted.</summary>
    public static void ShowAiSettings()
    {
        var existing = Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.FocusAiSection();
            existing.Show();
            existing.Activate();
            return;
        }
        new SettingsWindow(aiFocus: true).Show();
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

    public static void ApplyAiConfig(AiProviderConfig config)
    {
        Settings.AiConfig = config;
        Settings.AiTargetLanguage = config.TargetLanguage;
        SettingsService.Save(Settings);
        foreach (var window in Current.Windows.OfType<MainWindow>())
            window.ApplyVisualSettings(Settings);
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
