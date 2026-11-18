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
/// Conventional Windows setup wizard for 怪叔叔看图.
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

    private readonly Panel _header = new();
    private readonly Panel _content = new();
    private readonly Panel _footer = new();
    private readonly Panel[] _pages = new Panel[5];

    private readonly Button _back = new() { Text = "上一步(&B)", Size = new Size(96, 30) };
    private readonly Button _next = new() { Text = "下一步(&N)", Size = new Size(96, 30) };
    private readonly Button _cancel = new() { Text = "取消", Size = new Size(80, 30) };

    private readonly CheckBox _accept = new() { Text = "我接受许可协议中的条款(&A)" };
    private readonly TextBox _folderBox = new() { ReadOnly = true };
    private readonly TextBox _licenseBox = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true
    };

    private readonly Label _progressTitle = new() { Text = "正在安装…" };
    private readonly Label _progressDetail = new() { Text = "准备文件…" };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous };

    private readonly CheckBox _runApp = new() { Text = "安装完成后运行 怪叔叔看图(&R)", Checked = true };
    private readonly CheckBox _setDefault = new() { Text = "打开 Windows 默认应用确认窗口(&D)", Checked = true };

    private int _page;
    private bool _installing;
    private bool _installed;
    private string _destination;

    public InstallWizard()
    {
        _destination = _defaultDestination;

        Text = "怪叔叔看图 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 392);
        BackColor = SystemColors.Window;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildHeader();
        BuildFooter();
        BuildPages();
        ShowPage(0);

        CancelButton = _cancel;
        AcceptButton = _next;
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 76;
        _header.BackColor = Color.FromArgb(0, 122, 204);
        _header.Padding = new Padding(22, 12, 20, 10);

        var name = new Label
        {
            Text = "怪叔叔看图",
            Location = new Point(22, 13),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = $"版本 {Version}    ·    无边框置顶图片查看器",
            Location = new Point(24, 46),
            AutoSize = true,
            ForeColor = Color.FromArgb(225, 240, 255)
        };
        var badge = new Label
        {
            Text = "安装向导",
            Location = new Point(440, 26),
            Size = new Size(78, 24),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };
        _header.Controls.AddRange(name, subtitle, badge);
        Controls.Add(_header);
    }

    private void BuildFooter()
    {
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 58;
        _footer.BackColor = SystemColors.Control;
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        _back.Location = new Point(312, 14);
        _next.Location = new Point(418, 14);
        _cancel.Location = new Point(434, 14);
        _back.Click += (_, _) => ShowPage(Math.Max(0, _page - 1));
        _next.Click += Next_Click;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _footer.Controls.AddRange(_back, _next, _cancel);
        Controls.Add(_footer);
    }

    private void BuildPages()
    {
        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(24, 20, 24, 16);
        Controls.Add(_content);

        // 0 - Welcome
        _pages[0] = new Panel();
        AddTitle(_pages[0], "欢迎使用 怪叔叔看图 安装向导");
        AddBody(_pages[0],
            "本向导将把 怪叔叔看图 安装到您的计算机。\n\n" +
            "安装内容包括：\n" +
            "  · 主程序与运行所需组件\n" +
            "  · 桌面与开始菜单快捷方式\n" +
            "  · 常用图片及脑图/流程图格式的文件关联候选\n" +
            "  · 控制面板中的卸载入口\n\n" +
            "点击“下一步”继续，或点击“取消”退出安装。");

        // 1 - License
        _pages[1] = new Panel();
        AddTitle(_pages[1], "许可协议");
        AddBody(_pages[1], "请仔细阅读以下许可协议，然后勾选“我接受”。", 34);
        _licenseBox.Location = new Point(24, 66);
        _licenseBox.Size = new Size(468, 190);
        _licenseBox.Font = new Font("Consolas", 9F);
        _licenseBox.BackColor = Color.White;
        _licenseBox.Text = ReadLicense();
        _accept.Location = new Point(24, 268);
        _accept.AutoSize = true;
        _accept.CheckedChanged += (_, _) => RefreshButtons();
        _pages[1].Controls.AddRange(_licenseBox, _accept);

        // 2 - Destination
        _pages[2] = new Panel();
        AddTitle(_pages[2], "选择目标位置");
        AddBody(_pages[2], "安装程序将把文件复制到下面的文件夹。", 34);
        var folderLabel = new Label { Text = "目标文件夹：", Location = new Point(24, 72), AutoSize = true };
        _folderBox.Location = new Point(24, 96);
        _folderBox.Size = new Size(372, 28);
        _folderBox.Text = _destination;
        var browse = new Button { Text = "浏览(&B)…", Size = new Size(88, 28), Location = new Point(404, 96) };
        browse.Click += Browse_Click;
        var hint = new Label
        {
            Text = "建议保留默认位置。卸载时该目录中的程序文件会被移除。",
            Location = new Point(24, 136),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        _pages[2].Controls.AddRange(folderLabel, _folderBox, browse, hint);

        // 3 - Installing
        _pages[3] = new Panel();
        AddTitle(_pages[3], "正在安装");
        AddBody(_pages[3], "正在复制文件并注册文件关联，请稍候。", 34);
        _progressTitle.Location = new Point(24, 78);
        _progressTitle.AutoSize = true;
        _progressTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        _progress.Location = new Point(24, 108);
        _progress.Size = new Size(468, 20);
        _progressDetail.Location = new Point(24, 140);
        _progressDetail.AutoSize = true;
        _progressDetail.ForeColor = SystemColors.GrayText;
        _pages[3].Controls.AddRange(_progressTitle, _progress, _progressDetail);

        // 4 - Finished
        _pages[4] = new Panel();
        AddTitle(_pages[4], "安装完成");
        AddBody(_pages[4],
            "怪叔叔看图 已成功安装到您的计算机。\n\n" +
            "若希望它成为图片默认查看器，请在随后打开的 Windows 窗口中\n" +
            "选择“怪叔叔看图”并勾选“始终使用此应用打开”。\n\n" +
            "可在“设置 → 应用 → 安装的应用”中卸载本程序。", 34);
        _runApp.Location = new Point(24, 224);
        _runApp.AutoSize = true;
        _setDefault.Location = new Point(24, 252);
        _setDefault.AutoSize = true;
        _pages[4].Controls.AddRange(_runApp, _setDefault);

        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.BackColor = SystemColors.Window;
            page.Visible = false;
            _content.Controls.Add(page);
        }
    }

    private static void AddTitle(Panel page, string text)
    {
        page.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(24, 16),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 28, 34)
        });
    }

    private static void AddBody(Panel page, string text, int y = 56)
    {
        page.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(24, y),
            Size = new Size(470, 300),
            ForeColor = Color.FromArgb(55, 60, 68)
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
            Description = "选择 怪叔叔看图 的安装文件夹",
            SelectedPath = _folderBox.Text
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folderBox.Text = dialog.SelectedPath;
    }

    private void ShowPage(int page)
    {
        _page = page;
        foreach (var item in _pages) item.Visible = false;
        _pages[page].Visible = true;
        RefreshButtons();
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

        _back.Enabled = _page > 0 && _page < 3;
        _cancel.Enabled = true;
        _cancel.Visible = true;
        _next.Visible = true;

        if (_page == 1)
        {
            _next.Text = "下一步(&N)";
            _next.Enabled = _accept.Checked;
        }
        else if (_page == 2)
        {
            _next.Text = "安装(&I)";
            _next.Enabled = true;
        }
        else if (_page == 4)
        {
            _next.Text = "完成(&F)";
            _next.Enabled = true;
        }
        else
        {
            _next.Text = "下一步(&N)";
            _next.Enabled = true;
        }
    }

    private async void Next_Click(object? sender, EventArgs e)
    {
        if (_installing) return;

        if (_page < 3)
        {
            if (_page == 2)
            {
                _destination = _folderBox.Text.Trim();
                if (_destination.Length == 0)
                {
                    MessageBox.Show(this, "请先选择目标文件夹。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            ShowPage(_page + 1);
            if (_page == 3) await RunInstall();
            return;
        }

        if (_page == 4)
        {
            if (_runApp.Checked)
            {
                var exe = Path.Combine(_destination, ExeName);
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            if (_setDefault.Checked && _installed)
            {
                var exe = Path.Combine(_destination, ExeName);
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe, "--set-default") { UseShellExecute = true });
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async Task RunInstall()
    {
        _installing = true;
        RefreshButtons();
        var progress = new Progress<InstallProgress>(p =>
        {
            _progress.Value = p.Value;
            _progressTitle.Text = p.Title;
            _progressDetail.Text = p.Detail;
        });

        try
        {
            await Task.Run(() => InstallCore(_destination, progress));
            _installed = true;
            _installing = false;
            ShowPage(4);
        }
        catch (Exception ex)
        {
            _installing = false;
            _progress.Value = 0;
            _progressDetail.Text = "安装失败。";
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowPage(2);
        }
    }

    private void InstallCore(string destination, IProgress<InstallProgress> progress)
    {
        progress.Report(new InstallProgress(8, "创建目标文件夹", destination));
        Directory.CreateDirectory(destination);
        var exePath = Path.Combine(destination, ExeName);

        progress.Report(new InstallProgress(24, "复制主程序", exePath));
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (resource is null) throw new InvalidOperationException("安装程序内未找到主程序资源。");
            using var file = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
            resource.CopyTo(file);
        }

        progress.Report(new InstallProgress(48, "注册文件关联", "常用图片、脑图与流程图格式"));
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(string.Empty, "怪叔叔看图");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{exePath},0\"");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
        }

        using (var toasts = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts"))
        {
            foreach (var extension in Extensions)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
                key.SetValue(string.Empty, ProgId);
                using var openWith = key.CreateSubKey("OpenWithProgids");
                openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);
                toasts.SetValue(ProgId + "_" + extension, 0, RegistryValueKind.DWord);
            }
        }

        progress.Report(new InstallProgress(72, "创建快捷方式", "桌面与开始菜单"));
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CreateShortcut(Path.Combine(desktop, "怪叔叔看图.lnk"), exePath);
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(programs);
        CreateShortcut(Path.Combine(programs, "怪叔叔看图.lnk"), exePath);

        progress.Report(new InstallProgress(90, "写入卸载信息", "设置 → 应用"));
        using (var uninstall = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer"))
        {
            uninstall.SetValue("DisplayName", "怪叔叔看图");
            uninstall.SetValue("DisplayVersion", Version);
            uninstall.SetValue("Publisher", "怪叔叔");
            uninstall.SetValue("DisplayIcon", exePath);
            uninstall.SetValue("InstallLocation", destination);
            uninstall.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        }

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        progress.Report(new InstallProgress(100, "安装完成", "所有步骤已成功完成"));
    }

    private static void CreateShortcut(string linkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

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
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private readonly record struct InstallProgress(int Value, string Title, string Detail);
}
