using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WBImageViewer.Models;
using MessageBox = System.Windows.Forms.MessageBox;

namespace WBUninstaller;

internal static class Program
{
    private const string AppName = "怪叔叔看图";
    private const string ExeName = "怪叔叔看图.exe";
    private const string UninstallerName = "怪叔叔看图卸载.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\怪叔叔看图";
    private const string LegacyUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer";
    private const string ProgId = "怪叔叔看图.Image";
    private const string LegacyProgId = "GuaishushuViewer.Image";
    private const string VerbName = "怪叔叔看图.Open";
    private const string LegacyVerbName = "GuaishushuViewer.Open";

    private static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var silent = args.Any(a => a is "/S" or "-s" or "--silent" or "--quiet");
        var location = ResolveInstallLocation();

        if (silent)
        {
            var errors = new List<string>();
            Cleanup(location, null, errors);
            ScheduleSelfDelete(location);
            return;
        }

        Application.Run(new UninstallForm(location));
    }

    private static string ResolveInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            var registered = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(registered) && Directory.Exists(registered) &&
                File.Exists(Path.Combine(registered, ExeName)))
                return Path.GetFullPath(registered);
        }
        catch { /* fall back to the running file's directory */ }

        return Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    internal static bool Cleanup(string directory, IProgress<(int Value, string Message)>? progress,
        List<string> errors)
    {
        void Step(int value, string message) => progress?.Report((value, message));

        Step(4, "正在关闭正在运行的程序…");
        KillRunningProcesses(directory, errors);

        Step(18, "正在删除桌面与开始菜单快捷方式…");
        RemoveShortcuts(errors);

        Step(40, "正在清理文件关联与右键菜单…");
        RemoveAssociations(errors);

        Step(60, "正在删除用户配置…");
        RemoveConfig(errors);

        Step(78, "正在删除安装文件…");
        RemoveInstalledFiles(directory, errors);

        Step(92, "正在删除卸载注册信息…");
        RemoveUninstallRegistration(errors);

        Step(100, "正在通知 Windows 刷新文件关联…");
        NotifyShell();

        return errors.Count == 0;
    }

    private static void KillRunningProcesses(string directory, List<string> errors)
    {
        var target = Path.Combine(directory, ExeName);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var file = process.MainModule?.FileName;
                if (!string.Equals(file, target, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill(true);
                process.WaitForExit(4000);
            }
            catch
            {
                // A process that already exited or belongs to another session needs no action.
            }
        }
    }

    private static void RemoveShortcuts(List<string> errors)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var menuFolder = Path.Combine(programs, AppName);

        DeleteFile(Path.Combine(desktop, AppName + ".lnk"), errors);
        DeleteFile(Path.Combine(desktop, "卸载 " + AppName + ".lnk"), errors);
        DeleteFile(Path.Combine(programs, AppName + ".lnk"), errors);

        if (Directory.Exists(menuFolder))
        {
            foreach (var link in Directory.GetFiles(menuFolder, "*.lnk"))
                DeleteFile(link, errors);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(menuFolder).Any())
                    Directory.Delete(menuFolder, false);
            }
            catch (Exception ex)
            {
                errors.Add($"删除开始菜单文件夹失败：{ex.Message}");
            }
        }
    }

    private static void RemoveAssociations(List<string> errors)
    {
        DeleteSubKeyTree($@"Software\Classes\{ProgId}", errors);
        DeleteSubKeyTree($@"Software\Classes\{LegacyProgId}", errors);

        foreach (var extension in SupportedFormats.All.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{normalized}", true);
                if (key is not null)
                {
                    var current = key.GetValue(string.Empty) as string;
                    if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(current, LegacyProgId, StringComparison.OrdinalIgnoreCase))
                        key.DeleteValue(string.Empty, false);

                    using var openWith = key.OpenSubKey("OpenWithProgids", true);
                    openWith?.DeleteValue(ProgId, false);
                    openWith?.DeleteValue(LegacyProgId, false);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{normalized}：清理类别注册失败：{ex.Message}");
            }

            DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{normalized}\shell\{VerbName}", errors);
            DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{normalized}\shell\{LegacyVerbName}", errors);

            var userChoice = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{normalized}\UserChoice";
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(userChoice, true))
                {
                    var chosen = key?.GetValue("ProgId") as string;
                    if (string.Equals(chosen, ProgId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(chosen, LegacyProgId, StringComparison.OrdinalIgnoreCase))
                    {
                        key?.Dispose();
                        Registry.CurrentUser.DeleteSubKeyTree(userChoice, false);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{normalized}：清理默认程序记录失败：{ex.Message}");
            }

            try
            {
                using var toasts = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts", true);
                toasts?.DeleteValue(ProgId + "_" + normalized, false);
                toasts?.DeleteValue(LegacyProgId + "_" + normalized, false);
            }
            catch (Exception ex)
            {
                errors.Add($"{normalized}：清理关联提示记录失败：{ex.Message}");
            }
        }
    }

    private static void RemoveConfig(List<string> errors)
    {
        foreach (var folder in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WBImageViewer"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GuaishushuViewer")
                 })
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception ex)
            {
                errors.Add($"删除用户配置失败：{ex.Message}");
            }
        }
    }

    private static void RemoveInstalledFiles(string directory, List<string> errors)
    {
        DeleteFile(Path.Combine(directory, ExeName), errors);
        DeleteFile(Path.Combine(directory, ".gsw-write-test"), errors);
        // The running uninstaller is removed by ScheduleSelfDelete after the process exits.
    }

    private static void RemoveUninstallRegistration(List<string> errors)
    {
        DeleteSubKeyTree(UninstallKey, errors);
        DeleteSubKeyTree(LegacyUninstallKey, errors);
    }

    private static void DeleteFile(string path, List<string> errors)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            errors.Add($"删除文件失败：{path}：{ex.Message}");
        }
    }

    private static void DeleteSubKeyTree(string path, List<string> errors)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
        catch (Exception ex)
        {
            errors.Add($"删除注册表项失败：{path}：{ex.Message}");
        }
    }

    internal static void ScheduleSelfDelete(string directory)
    {
        try
        {
            var uninstaller = Path.Combine(directory, UninstallerName);
            var command =
                "Start-Sleep -Milliseconds 800; " +
                $"Remove-Item -LiteralPath '{PsQuote(uninstaller)}' -Force -ErrorAction SilentlyContinue; " +
                $"if ((Get-ChildItem -LiteralPath '{PsQuote(directory)}' -Force -ErrorAction SilentlyContinue | " +
                $"Measure-Object).Count -eq 0) {{ Remove-Item -LiteralPath '{PsQuote(directory)}' -Force -ErrorAction SilentlyContinue }}";

            var start = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-WindowStyle");
            start.ArgumentList.Add("Hidden");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(command);
            Process.Start(start);
        }
        catch
        {
            // The user can still remove the small uninstaller file manually.
        }
    }

    private static string PsQuote(string value) => value.Replace("'", "''");

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* shell refresh is best effort */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private sealed class UninstallForm : Form
    {
        private readonly string _location;
        private readonly ListBox _log = new() { IntegralHeight = false };
        private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous };
        private readonly Button _uninstall = new() { Text = "卸载(&U)", Size = new Size(96, 32) };
        private readonly Button _cancel = new() { Text = "取消", Size = new Size(78, 32) };

        public UninstallForm(string location)
        {
            _location = location;
            Text = "怪叔叔看图 卸载程序";
            ClientSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;
            CancelButton = _cancel;

            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.ico");
                if (stream is not null) Icon = new Icon(stream);
            }
            catch { /* keep default icon */ }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.White
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(11, 127, 199) };
            header.Controls.Add(new Label
            {
                Text = "怪叔叔看图 卸载程序", ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                Location = new Point(24, 12), AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = $"版本 {Version}   ·   将从本机移除程序及其全部写入", ForeColor = Color.FromArgb(226, 242, 255),
                Location = new Point(26, 42), AutoSize = true
            });
            root.Controls.Add(header, 0, 0);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 8) };
            body.Controls.Add(new Label
            {
                Text = "安装位置", ForeColor = Color.FromArgb(90, 98, 110),
                Location = new Point(24, 18), AutoSize = true
            });
            body.Controls.Add(new TextBox
            {
                Text = _location, ReadOnly = true, BorderStyle = BorderStyle.None,
                Location = new Point(24, 40), Size = new Size(548, 24),
                BackColor = Color.FromArgb(246, 248, 251), ForeColor = Color.FromArgb(45, 52, 62)
            });
            body.Controls.Add(new Label
            {
                Text = "将删除：程序文件、开始菜单/桌面快捷方式、文件关联与右键菜单、用户配置与卸载注册信息。",
                Location = new Point(24, 78), Size = new Size(548, 36),
                ForeColor = Color.FromArgb(120, 62, 24)
            });
            _log.Location = new Point(24, 122);
            _log.Size = new Size(548, 188);
            _log.Font = new Font("Microsoft YaHei UI", 9F);
            body.Controls.Add(_log);
            _progress.Location = new Point(24, 320);
            _progress.Size = new Size(548, 12);
            body.Controls.Add(_progress);
            root.Controls.Add(body, 0, 1);

            var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
            _uninstall.Location = new Point(414, 13);
            _cancel.Location = new Point(520, 13);
            _uninstall.Click += Uninstall_Click;
            _cancel.Click += (_, _) => Close();
            footer.Controls.AddRange(_uninstall, _cancel);
            root.Controls.Add(footer, 0, 2);

            _log.Items.Add("确认后将开始卸载。建议先保存正在编辑的图片工程。");
        }

        private async void Uninstall_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("确定要卸载 怪叔叔看图 并清除其系统写入吗？", "卸载确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _uninstall.Enabled = false;
            _cancel.Enabled = false;
            var errors = new List<string>();
            var progress = new Progress<(int Value, string Message)>(item =>
            {
                _progress.Value = Math.Max(0, Math.Min(100, item.Value));
                _log.Items.Add($"{DateTime.Now:HH:mm:ss}  {item.Message}");
                _log.SelectedIndex = _log.Items.Count - 1;
            });

            var success = await Task.Run(() => Cleanup(_location, progress, errors));
            foreach (var error in errors)
                _log.Items.Add("× " + error);

            if (success)
                MessageBox.Show("卸载完成。程序文件、快捷方式、关联信息与用户配置均已清除。", "卸载完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("卸载已执行，但部分项目未能清除：\n\n" + string.Join("\n", errors),
                    "卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            ScheduleSelfDelete(_location);
            Close();
        }
    }
}
