using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WBImageViewer.Models;

namespace WBInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallWizard());
    }
}

/// <summary>
/// Conventional Windows setup wizard with live validation and stage-by-stage error reporting.
/// Pages: Welcome -> License -> Destination -> Installing -> Finished.
/// </summary>
internal sealed class InstallWizard : Form
{
    private const string ExeName = "GuaishushuViewer.exe";
    private const string ProgId = "GuaishushuViewer.Image";
    private const string ResourceName = ExeName;
    private static readonly string[] Extensions = SupportedFormats.All;

    private static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    private readonly string _defaultDestination = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuaishushuViewer");

    private readonly Panel _body = new();
    private readonly Panel _side = new();
    private readonly Panel _content = new();
    private readonly Panel _footer = new();
    private readonly Panel[] _pages = new Panel[5];
    private readonly Label[] _stepLabels = new Label[5];

    private readonly PictureBox _logo = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _statusIcon = new();
    private readonly Label _statusText = new();
    private readonly Label _folderHint = new();
    private readonly CheckBox _accept = new() { Text = "我接受许可协议中的条款(&A)" };
    private readonly TextBox _folderBox = new() { ReadOnly = true };
    private readonly TextBox _licenseBox = new()
    {
        ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true
    };

    private readonly Label _currentStep = new() { Text = "准备中…" };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous };
    private readonly ListBox _log = new() { IntegralHeight = false };
    private readonly TextBox _errorBox = new()
    {
        ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
        BackColor = Color.FromArgb(255, 248, 248), ForeColor = Color.FromArgb(155, 35, 35)
    };

    private readonly CheckBox _runApp = new() { Text = "安装完成后运行 怪叔叔看图(&R)", Checked = true };
    private readonly CheckBox _setDefault = new() { Text = "打开 Windows 默认应用确认窗口(&D)", Checked = true };

    private readonly Button _back = new() { Text = "上一步(&B)", Size = new Size(86, 32) };
    private readonly Button _next = new() { Text = "下一步(&N)", Size = new Size(100, 32) };
    private readonly Button _cancel = new() { Text = "取消", Size = new Size(70, 32) };
    private readonly System.Windows.Forms.Timer _checkTimer = new() { Interval = 900 };

    private int _page;
    private bool _installing;
    private bool _installFailed;
    private bool _installed;
    private bool _destinationValid;
    private string _destination;

    public InstallWizard()
    {
        _destination = _defaultDestination;

        Text = "怪叔叔看图 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(700, 520);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        LoadBrandIcons();
        BuildHeader();
        BuildBody();
        BuildFooter();
        BuildPages();

        _checkTimer.Tick += (_, _) => { if (_page == 2 && !_installing) ValidateDestination(); };
        _checkTimer.Start();
        FormClosed += (_, _) => _checkTimer.Dispose();

        ShowPage(0);
        CancelButton = _cancel;
        AcceptButton = _next;
    }

    private void LoadBrandIcons()
    {
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.ico");
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        catch { /* keep the default assembly icon */ }

        try
        {
            using var logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppLogo.png");
            if (logoStream is not null)
            {
                using var temp = Image.FromStream(logoStream);
                _logo.Image = new Bitmap(temp);
            }
        }
        catch { /* header simply shows text without the logo */ }
    }

    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.FromArgb(11, 127, 199) };
        _logo.Location = new Point(20, 18);
        _logo.Size = new Size(46, 46);
        _logo.BackColor = Color.Transparent;

        var title = new Label
        {
            Text = "怪叔叔看图", Location = new Point(78, 16), AutoSize = true, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = $"版本 {Version}   ·   无边框置顶图片查看器", Location = new Point(80, 48),
            AutoSize = true, ForeColor = Color.FromArgb(224, 242, 255)
        };
        header.Controls.AddRange(_logo, title, subtitle);
        Controls.Add(header);
    }

    private void BuildBody()
    {
        _body.Dock = DockStyle.Fill;
        _body.BackColor = Color.White;
        Controls.Add(_body);

        _side.Dock = DockStyle.Left;
        _side.Width = 172;
        _side.BackColor = Color.FromArgb(243, 246, 250);
        _side.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 226, 234));
            e.Graphics.DrawLine(pen, _side.Width - 1, 0, _side.Width - 1, _side.Height);
        };

        string[] names = { "① 欢迎使用", "② 许可协议", "③ 目标位置", "④ 安装进度", "⑤ 安装完成" };
        for (var i = 0; i < names.Length; i++)
        {
            var label = new Label
            {
                Text = names[i], Location = new Point(0, 26 + i * 54), Size = new Size(172, 42),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(85, 96, 110)
            };
            _stepLabels[i] = label;
            _side.Controls.Add(label);
        }
        _body.Controls.Add(_side);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Color.White;
        _content.Padding = new Padding(26, 20, 22, 16);
        _body.Controls.Add(_content);
    }

    private void BuildFooter()
    {
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 62;
        _footer.BackColor = Color.FromArgb(247, 248, 250);
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 226, 234));
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        _statusIcon.Location = new Point(22, 21);
        _statusIcon.Size = new Size(18, 20);
        _statusIcon.Font = new Font("Microsoft YaHei UI", 13F);
        _statusText.Location = new Point(46, 21);
        _statusText.Size = new Size(340, 30);

        _back.Location = new Point(426, 15);
        _next.Location = new Point(520, 15);
        _cancel.Location = new Point(628, 15);
        _back.Click += (_, _) => ShowPage(Math.Max(0, _page - 1));
        _next.Click += Next_Click;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _footer.Controls.AddRange(_statusIcon, _statusText, _back, _next, _cancel);
        Controls.Add(_footer);
    }

    private void BuildPages()
    {
        // 0 - Welcome
        _pages[0] = new Panel();
        AddTitle(_pages[0], "欢迎使用 怪叔叔看图 安装向导");
        AddBody(_pages[0],
            "本向导将把 怪叔叔看图 安装到您的计算机。\n\n" +
            "安装内容包括：\n" +
            "   · 主程序与运行所需组件\n" +
            "   · 桌面与开始菜单快捷方式\n" +
            "   · 常用图片、脑图与流程图格式的文件关联候选\n" +
            "   · “设置 → 应用”中的卸载入口\n\n" +
            "点击“下一步”继续，或点击“取消”退出安装。");

        // 1 - License
        _pages[1] = new Panel();
        AddTitle(_pages[1], "许可协议");
        AddHint(_pages[1], "请仔细阅读以下许可协议。只有勾选接受后才能继续安装。", 52);
        _licenseBox.Location = new Point(24, 80);
        _licenseBox.Size = new Size(478, 206);
        _licenseBox.Font = new Font("Consolas", 9F);
        _licenseBox.Text = ReadLicense();
        _accept.Location = new Point(24, 298);
        _accept.AutoSize = true;
        _accept.CheckedChanged += (_, _) => RefreshLicenseState();
        _pages[1].Controls.AddRange(_licenseBox, _accept);

        // 2 - Destination
        _pages[2] = new Panel();
        AddTitle(_pages[2], "选择目标位置");
        AddHint(_pages[2], "安装程序将把文件复制到下面的文件夹。路径会自动检查。", 52);
        var folderLabel = new Label { Text = "目标文件夹", Location = new Point(24, 88), AutoSize = true };
        _folderBox.Location = new Point(24, 112);
        _folderBox.Size = new Size(378, 30);
        _folderBox.Text = _destination;
        var browse = new Button { Text = "浏览(&B)…", Size = new Size(88, 30), Location = new Point(414, 112) };
        browse.Click += Browse_Click;
        _folderHint.Location = new Point(24, 158);
        _folderHint.Size = new Size(478, 72);
        _folderHint.Font = new Font("Microsoft YaHei UI", 9F);
        _pages[2].Controls.AddRange(folderLabel, _folderBox, browse, _folderHint);

        // 3 - Installing
        _pages[3] = new Panel();
        AddTitle(_pages[3], "正在安装");
        _currentStep.Location = new Point(24, 54);
        _currentStep.Size = new Size(478, 24);
        _currentStep.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _progress.Location = new Point(24, 84);
        _progress.Size = new Size(478, 18);
        _log.Location = new Point(24, 120);
        _log.Size = new Size(478, 132);
        _log.Font = new Font("Consolas", 9F);
        _log.BorderStyle = BorderStyle.FixedSingle;
        var errorLabel = new Label { Text = "错误详情（如有）", Location = new Point(24, 260), AutoSize = true };
        _errorBox.Location = new Point(24, 282);
        _errorBox.Size = new Size(478, 50);
        _errorBox.Font = new Font("Consolas", 9F);
        _errorBox.Text = "无";
        _pages[3].Controls.AddRange(_currentStep, _progress, _log, errorLabel, _errorBox);

        // 4 - Finished
        _pages[4] = new Panel();
        AddTitle(_pages[4], "安装完成");
        AddBody(_pages[4],
            "怪叔叔看图 已成功安装到您的计算机。\n\n" +
            "若希望它成为图片默认查看器，请在随后打开的 Windows 窗口中\n" +
            "选择“怪叔叔看图”并勾选“始终使用此应用打开”。\n\n" +
            "可在“设置 → 应用 → 安装的应用”中卸载本程序。", 54);
        _runApp.Location = new Point(24, 228);
        _runApp.AutoSize = true;
        _setDefault.Location = new Point(24, 258);
        _setDefault.AutoSize = true;
        _pages[4].Controls.AddRange(_runApp, _setDefault);

        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.BackColor = Color.White;
            page.Visible = false;
            _content.Controls.Add(page);
        }
    }

    private static void AddTitle(Panel page, string text)
    {
        page.Controls.Add(new Label
        {
            Text = text, Location = new Point(24, 16), AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 28, 34)
        });
    }

    private static void AddHint(Panel page, string text, int y)
    {
        page.Controls.Add(new Label
        {
            Text = text, Location = new Point(24, y), AutoSize = true,
            ForeColor = Color.FromArgb(90, 100, 112)
        });
    }

    private static void AddBody(Panel page, string text, int y = 56)
    {
        page.Controls.Add(new Label
        {
            Text = text, Location = new Point(24, y), Size = new Size(478, 300),
            ForeColor = Color.FromArgb(55, 60, 68), Font = new Font("Microsoft YaHei UI", 9.5F)
        });
    }

    private static string ReadLicense()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE.txt");
        if (stream is null) return "（未找到许可协议文本）";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Browse_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 怪叔叔看图 的安装文件夹", SelectedPath = _folderBox.Text
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _folderBox.Text = dialog.SelectedPath;
        ValidateDestination();
    }

    private void ShowPage(int page)
    {
        _page = page;
        foreach (var item in _pages) item.Visible = false;
        _pages[page].Visible = true;

        for (var i = 0; i < _stepLabels.Length; i++)
        {
            var current = i == page;
            _stepLabels[i].BackColor = current ? Color.FromArgb(216, 235, 250) : Color.Transparent;
            _stepLabels[i].ForeColor = current ? Color.FromArgb(11, 127, 199) : Color.FromArgb(85, 96, 110);
            _stepLabels[i].Font = new Font("Microsoft YaHei UI", 10F,
                current ? FontStyle.Bold : FontStyle.Regular);
        }

        switch (page)
        {
            case 0:
                SetStatus(StatusKind.Info, "就绪 · 点击“下一步”开始安装");
                break;
            case 1:
                RefreshLicenseState();
                break;
            case 2:
                ValidateDestination();
                break;
            case 4:
                SetStatus(StatusKind.Ok, $"安装完成 · 版本 {Version}");
                break;
        }
        RefreshButtons();
    }

    private void RefreshLicenseState()
    {
        if (_accept.Checked)
            SetStatus(StatusKind.Ok, "已接受许可协议，可以继续安装");
        else
            SetStatus(StatusKind.Warn, "请先阅读并勾选“我接受许可协议”");
        RefreshButtons();
    }

    private bool ValidateDestination()
    {
        var path = _folderBox.Text.Trim();
        bool ok;
        string message;

        try
        {
            if (path.Length == 0)
            {
                ok = false; message = "目标文件夹不能为空。";
            }
            else
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    ok = false; message = "请不要直接选择磁盘根目录，建议选择“用户/AppData/Local”下的文件夹。";
                }
                else
                {
                    Directory.CreateDirectory(full);
                    var probe = Path.Combine(full, ".gsw-write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);

                    var drive = new DriveInfo(Path.GetPathRoot(full)!);
                    if (drive.AvailableFreeSpace < 10L * 1024 * 1024)
                    {
                        ok = false; message = "目标磁盘可用空间不足 10 MB，请更换位置。";
                    }
                    else if (File.Exists(Path.Combine(full, ExeName)))
                    {
                        ok = true; message = "目标位置可写；检测到已有安装，本次将覆盖更新。";
                    }
                    else
                    {
                        ok = true; message = "目标位置检查通过：可创建目录、可写入、空间充足。";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            message = "目标位置不可用：" + ex.Message;
        }

        _folderHint.Text = message;
        _folderHint.ForeColor = ok ? Color.FromArgb(35, 120, 70) : Color.FromArgb(170, 50, 50);
        _destinationValid = ok;
        SetStatus(ok ? StatusKind.Ok : StatusKind.Error, message);
        RefreshButtons();
        return ok;
    }

    private void SetStatus(StatusKind kind, string text)
    {
        (_statusIcon.Text, _statusIcon.ForeColor, _statusText.ForeColor) = kind switch
        {
            StatusKind.Ok => ("●", Color.FromArgb(35, 150, 80), Color.FromArgb(35, 110, 65)),
            StatusKind.Warn => ("●", Color.FromArgb(210, 145, 20), Color.FromArgb(145, 95, 10)),
            StatusKind.Error => ("●", Color.FromArgb(205, 55, 55), Color.FromArgb(160, 40, 40)),
            _ => ("●", Color.FromArgb(11, 127, 199), Color.FromArgb(60, 80, 100))
        };
        _statusText.Text = text;
    }

    private void RefreshButtons()
    {
        if (_installing)
        {
            _back.Enabled = false;
            _next.Enabled = false;
            _cancel.Enabled = false;
            _next.Text = "正在安装…";
            return;
        }

        _cancel.Enabled = true;
        _back.Enabled = _page > 0 && _page != 3 && !_installFailed;
        _next.Enabled = _page switch
        {
            1 => _accept.Checked,
            2 => _destinationValid,
            3 => _installFailed,
            _ => true
        };
        _next.Text = _page switch
        {
            2 => "安装(&I)",
            3 when _installFailed => "重试(&R)",
            4 => "完成(&F)",
            _ => "下一步(&N)"
        };
    }

    private async void Next_Click(object? sender, EventArgs e)
    {
        if (_installing) return;

        if (_page < 3)
        {
            if (_page == 2)
            {
                _destination = _folderBox.Text.Trim();
                if (!ValidateDestination()) return;
            }
            _installFailed = false;
            ShowPage(_page + 1);
            if (_page == 3) await RunInstall();
            return;
        }

        if (_page == 3 && _installFailed)
        {
            _installFailed = false;
            _errorBox.Text = "无";
            _log.Items.Clear();
            _progress.Value = 0;
            await RunInstall();
            return;
        }

        if (_page == 4)
        {
            var exe = Path.Combine(_destination, ExeName);
            try
            {
                if (_runApp.Checked && File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                if (_setDefault.Checked && _installed && File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe, "--set-default") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(StatusKind.Error, "启动程序失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async Task RunInstall()
    {
        _installing = true;
        RefreshButtons();
        SetStatus(StatusKind.Info, "正在安装，请查看实时日志…");
        var progress = new Progress<InstallEvent>(OnInstallEvent);

        var result = await Task.Run(() => InstallCore(_destination, progress));
        _installing = false;

        if (result)
        {
            _installed = true;
            _installFailed = false;
            _progress.Value = 100;
            _currentStep.Text = "安装已成功完成";
            SetStatus(StatusKind.Ok, "安装完成 · 所有步骤均已通过");
            ShowPage(4);
        }
        else
        {
            _installFailed = true;
            _currentStep.Text = "安装失败，请查看错误详情";
            SetStatus(StatusKind.Error, "安装失败 · 可点击“重试”或“上一步”修改位置");
            RefreshButtons();
        }
    }

    private void OnInstallEvent(InstallEvent item)
    {
        switch (item.Kind)
        {
            case InstallEventKind.Log:
                _log.Items.Add($"{DateTime.Now:HH:mm:ss}  {item.Message}");
                _log.SelectedIndex = _log.Items.Count - 1;
                break;
            case InstallEventKind.Progress:
                _progress.Value = Math.Clamp(item.Value, 0, 100);
                _currentStep.Text = item.Message;
                SetStatus(StatusKind.Info, item.Message);
                break;
            case InstallEventKind.Error:
                _log.Items.Add($"{DateTime.Now:HH:mm:ss}  ✗ {item.Message}");
                _errorBox.Text = item.Message;
                SetStatus(StatusKind.Error, item.Message);
                break;
            case InstallEventKind.Done:
                _log.Items.Add($"{DateTime.Now:HH:mm:ss}  √ {item.Message}");
                break;
        }
    }

    private bool InstallCore(string destination, IProgress<InstallEvent> progress)
    {
        if (!Stage(progress, 8, "创建目标文件夹", () =>
            {
                Directory.CreateDirectory(destination);
                var probe = Path.Combine(destination, ".gsw-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            })) return false;

        var exePath = Path.Combine(destination, ExeName);
        if (!Stage(progress, 26, "释放主程序", () =>
            {
                using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (resource is null) throw new InvalidOperationException("安装程序内未找到主程序资源。");
                using var file = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
                resource.CopyTo(file);
            })) return false;

        if (!Stage(progress, 50, "注册 ProgID 与图标", () =>
            {
                using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
                progId.SetValue(string.Empty, "怪叔叔看图");
                using var icon = progId.CreateSubKey("DefaultIcon");
                icon.SetValue(string.Empty, $"\"{exePath},0\"");
                using var command = progId.CreateSubKey(@"shell\open\command");
                command.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
            })) return false;

        if (!Stage(progress, 72, "注册文件关联候选", () =>
            {
                using var toasts = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts");
                foreach (var extension in Extensions)
                {
                    using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
                    key.SetValue(string.Empty, ProgId);
                    using var openWith = key.CreateSubKey("OpenWithProgids");
                    openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);
                    toasts.SetValue(ProgId + "_" + extension, 0, RegistryValueKind.DWord);
                }
            })) return false;

        if (!Stage(progress, 88, "创建快捷方式", () =>
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktop, "怪叔叔看图.lnk"), exePath);
                var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(programs);
                CreateShortcut(Path.Combine(programs, "怪叔叔看图.lnk"), exePath);
            })) return false;

        if (!Stage(progress, 96, "写入卸载信息", () =>
            {
                using var uninstall = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer");
                uninstall.SetValue("DisplayName", "怪叔叔看图");
                uninstall.SetValue("DisplayVersion", Version);
                uninstall.SetValue("Publisher", "怪叔叔");
                uninstall.SetValue("DisplayIcon", exePath);
                uninstall.SetValue("InstallLocation", destination);
                uninstall.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            })) return false;

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        progress.Report(new InstallEvent(InstallEventKind.Progress, 100, "安装完成"));
        progress.Report(new InstallEvent(InstallEventKind.Done, 100, "所有安装步骤均已成功完成"));
        return true;
    }

    private static bool Stage(IProgress<InstallEvent> progress, int value, string name, Action action)
    {
        progress.Report(new InstallEvent(InstallEventKind.Log, value, $"开始：{name}"));
        try
        {
            action();
            progress.Report(new InstallEvent(InstallEventKind.Progress, value, name));
            progress.Report(new InstallEvent(InstallEventKind.Log, value, $"完成：{name}"));
            return true;
        }
        catch (Exception ex)
        {
            progress.Report(new InstallEvent(InstallEventKind.Error, value, $"{name}失败：{ex.GetType().Name}: {ex.Message}"));
            return false;
        }
    }

    private static void CreateShortcut(string linkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) throw new InvalidOperationException("无法创建 WScript.Shell 快捷方式对象。");

        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            object shortcut = shellType.InvokeMember(
                "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath })!;
            try
            {
                var type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { Path.GetDirectoryName(target)! });
                type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { Marshal.ReleaseComObject(shortcut); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private enum StatusKind { Info, Ok, Warn, Error }

    private enum InstallEventKind { Progress, Log, Error, Done }

    private readonly record struct InstallEvent(InstallEventKind Kind, int Value, string Message);
}
