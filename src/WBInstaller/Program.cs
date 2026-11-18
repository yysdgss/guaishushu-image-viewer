using System.Diagnostics;
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
        Application.Run(new InstallForm());
    }
}

/// <summary>
/// A normal Windows-style setup executable: it embeds the viewer, installs it under
/// %LOCALAPPDATA%, registers file associations, shortcuts and the uninstall entry,
/// then opens the protected Windows "default app" confirmation dialog.
/// </summary>
internal sealed class InstallForm : Form
{
    private const string ExeName = "GuaishushuViewer.exe";
    private const string ProgId = "GuaishushuViewer.Image";
    private const string ResourceName = ExeName;
    private static readonly string[] Extensions = SupportedFormats.All;

    private static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    private readonly string _destination = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuaishushuViewer");
    private readonly string _exePath;
    private readonly Button _install = new() { Text = "安装", Width = 112, Height = 34 };
    private readonly Button _cancel = new() { Text = "取消", Width = 88, Height = 34 };
    private readonly Label _status = new() { Location = new Point(20, 148), Size = new Size(400, 56), ForeColor = System.Drawing.Color.FromArgb(48, 52, 59) };
    private readonly ProgressBar _progress = new() { Location = new Point(20, 122), Size = new Size(400, 16) };

    private static System.Drawing.Color ForegroundColor => System.Drawing.Color.FromArgb(48, 52, 59);

    public InstallForm()
    {
        _exePath = Path.Combine(_destination, ExeName);

        Text = "怪叔叔看图 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 270);
        BackColor = System.Drawing.Color.FromArgb(247, 248, 250);
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "怪叔叔看图",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 17F, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(20, 24, 30)
        };
        var version = new Label
        {
            Text = $"版本 {Version} · 安装到 {_destination}",
            Location = new Point(20, 52),
            Size = new Size(420, 22),
            ForeColor = System.Drawing.Color.FromArgb(107, 114, 128)
        };
        var note = new Label
        {
            Text = "安装后会自动创建桌面与开始菜单快捷方式，并注册为图片打开候选。Windows 11 需在弹出窗口中确认一次默认应用。",
            Location = new Point(20, 78),
            Size = new Size(420, 40),
            ForeColor = ForegroundColor
        };

        _status.Text = "准备就绪，点击“安装”开始。";
        _install.Location = new Point(236, 218);
        _cancel.Location = new Point(356, 218);
        _install.Click += Install_Click;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(title, version, note, _progress, _status, _install, _cancel);
        AcceptButton = _install;
        CancelButton = _cancel;
    }

    private async void Install_Click(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        _cancel.Enabled = false;
        var progress = new Progress<int>(value => _progress.Value = value);

        try
        {
            await Task.Run(() => InstallCore(progress));
            _progress.Value = 100;
            _status.Text = "安装完成，正在打开默认应用确认窗口…";
            MessageBox.Show(this,
                "安装完成。请在弹出的 Windows 窗口中选择“怪叔叔看图”，并勾选“始终使用此应用打开”。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo(_exePath, "--set-default") { UseShellExecute = true });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _progress.Value = 0;
            _status.Text = "安装失败：" + ex.Message;
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true;
            _cancel.Enabled = true;
        }
    }

    private void InstallCore(IProgress<int> progress)
    {
        progress.Report(10);
        Directory.CreateDirectory(_destination);
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (resource is null) throw new InvalidOperationException("安装程序内未找到主程序资源。");
            using var file = new FileStream(_exePath, FileMode.Create, FileAccess.Write, FileShare.None);
            resource.CopyTo(file);
        }

        progress.Report(35);
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(string.Empty, "怪叔叔看图");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{_exePath},0\"");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{_exePath}\" \"%1\"");
        }

        progress.Report(60);
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

        progress.Report(80);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CreateShortcut(Path.Combine(desktop, "怪叔叔看图.lnk"), _exePath);
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(programs);
        CreateShortcut(Path.Combine(programs, "怪叔叔看图.lnk"), _exePath);

        progress.Report(92);
        using (var uninstall = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer"))
        {
            uninstall.SetValue("DisplayName", "怪叔叔看图");
            uninstall.SetValue("DisplayVersion", Version);
            uninstall.SetValue("Publisher", "怪叔叔");
            uninstall.SetValue("InstallLocation", _destination);
            uninstall.SetValue("UninstallString", $"\"{_exePath}\" --uninstall");
        }

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        progress.Report(100);
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
}
