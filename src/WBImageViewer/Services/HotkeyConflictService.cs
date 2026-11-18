using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WBImageViewer.Native;

namespace WBImageViewer.Services;

public enum HotkeyConflictState
{
    Available,
    Occupied,
    Invalid,
    Reserved
}

public sealed record HotkeyConflictInfo(
    string Spec,
    HotkeyConflictState State,
    string Message,
    IReadOnlyList<string> Suspects);

/// <summary>
/// Detects whether a global hotkey combination is free.
///
/// Windows offers no public API that names the process owning a registered hotkey, so the
/// detection is layered:
///  1. Try RegisterHotKey on a private message-only window. Failure with error 1409 means
///     another registration already owns the combination.
///  2. Scan Start Menu / Desktop .lnk files for an assigned shortcut key, which reliably
///     names programs such as Steam, Discord, QQ, input methods, etc.
///  3. Match a table of combos reserved by Windows itself.
/// The result reports the state plus the likely owners; when none can be named, the message
/// says so explicitly instead of pretending certainty.
/// </summary>
internal static class HotkeyConflictService
{
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    // Combos reserved by Windows / shell that our modifier set can produce.
    private static readonly Dictionary<string, string> ReservedCombos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl+Shift+Esc"] = "Windows 任务管理器",
        ["Ctrl+Shift+Enter"] = "Windows 管理员运行（资源管理器）",
        ["Ctrl+Alt+Del"] = "Windows 安全选项",
        ["Ctrl+Alt+Tab"] = "Windows 任务切换",
        ["Alt+F4"] = "Windows 关闭当前窗口",
        ["Alt+Tab"] = "Windows 任务切换",
        ["Alt+Space"] = "Windows 窗口系统菜单",
        ["Ctrl+Alt+M"] = "Windows 触摸键盘（部分版本）",
        ["Shift+F10"] = "Windows 上下文菜单",
        ["Ctrl+Shift+N"] = "Windows 新建文件夹（资源管理器）"
    };

    private static HwndSource? _probeSource;

    /// <summary>
    /// Probe one combination. <paramref name="ownedByUs"/> is true when this exact combo is one
    /// of our own currently registered hotkeys, in which case registration success is expected
    /// and no system probe is necessary.
    /// </summary>
    public static HotkeyConflictInfo Check(string spec, IReadOnlyList<string> otherSpecs, bool ownedByUs)
    {
        if (!HotkeySpec.TryParse(spec, out var modifiers, out var vk))
            return new(spec, HotkeyConflictState.Invalid, "快捷键格式无效", Array.Empty<string>());

        var normalized = Normalize(spec);

        if (ReservedCombos.TryGetValue(normalized, out var reservedOwner))
            return new(spec, HotkeyConflictState.Reserved,
                $"该组合被系统保留：{reservedOwner}", new[] { reservedOwner });

        foreach (var other in otherSpecs)
        {
            if (SameCombo(other, spec))
                return new(spec, HotkeyConflictState.Occupied,
                    "与本软件的另一个快捷键重复", new[] { "本软件的另一个功能" });
        }

        // Our own registration of this combo would make the probe report a false conflict.
        if (!ownedByUs)
        {
            var state = ProbeSystem(modifiers, vk);
            if (state == HotkeyConflictState.Occupied)
            {
                var suspects = FindShortcutOwners(spec);
                var message = suspects.Count == 0
                    ? "该组合已被其他程序占用（Windows 不公开占用者名称，请逐一排查常驻软件）"
                    : "该组合已被其他程序占用，疑似：" + string.Join("、", suspects);
                return new(spec, HotkeyConflictState.Occupied, message, suspects);
            }
            if (state == HotkeyConflictState.Invalid)
                return new(spec, HotkeyConflictState.Invalid, "该组合无法注册（可能含系统保留键）", Array.Empty<string>());
        }

        return new(spec, HotkeyConflictState.Available, "该组合当前可用", Array.Empty<string>());
    }

    private static HotkeyConflictState ProbeSystem(uint modifiers, uint vk)
    {
        try
        {
            var source = ProbeWindow;
            if (Win32.RegisterHotKey(source.Handle, 0x7B, modifiers, vk))
            {
                Win32.UnregisterHotKey(source.Handle, 0x7B);
                return HotkeyConflictState.Available;
            }
            var error = Marshal.GetLastWin32Error();
            return error == ErrorHotkeyAlreadyRegistered
                ? HotkeyConflictState.Occupied
                : HotkeyConflictState.Invalid;
        }
        catch
        {
            return HotkeyConflictState.Invalid;
        }
    }

    private static HwndSource ProbeWindow => _probeSource ??= new HwndSource(
        new HwndSourceParameters("WBImageViewer_HotkeyProbe")
        {
            ParentWindow = new IntPtr(Win32.HWND_MESSAGE)
        });

    /// <summary>Scan shell links for an assigned shortcut key that matches the combo.</summary>
    private static List<string> FindShortcutOwners(string spec)
    {
        var owners = new List<string>();
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return owners;
            object shell = Activator.CreateInstance(shellType)!;
            try
            {
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                };
                foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
                {
                    IEnumerable<string> links;
                    try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
                    catch { continue; }
                    foreach (var link in links)
                    {
                        object? shortcut = null;
                        try
                        {
                            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                                null, shell, new object[] { link });
                            if (shortcut is null) continue;
                            var type = shortcut.GetType();
                            var hotkey = type.InvokeMember("Hotkey", BindingFlags.GetProperty, null, shortcut, null) as string;
                            if (string.IsNullOrWhiteSpace(hotkey) || !SameCombo(hotkey, spec)) continue;

                            var target = type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                            var name = Path.GetFileNameWithoutExtension(link);
                            owners.Add(string.IsNullOrWhiteSpace(target) ? name : $"{name}（{target}）");
                        }
                        catch { /* skip links we cannot read */ }
                        finally { if (shortcut != null) Marshal.ReleaseComObject(shortcut); }
                    }
                }
            }
            finally { Marshal.ReleaseComObject(shell); }
        }
        catch { /* shortcut scanning is best-effort */ }
        return owners.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private static string Normalize(string spec)
    {
        var tokens = SplitTokens(spec);
        var mods = tokens.Where(t => t is "ctrl" or "shift" or "alt" or "win").ToList();
        var order = new[] { "ctrl", "shift", "alt", "win" };
        mods = mods.OrderBy(m => Array.IndexOf(order, m)).ToList();
        var key = tokens.LastOrDefault(t => t is not ("ctrl" or "shift" or "alt" or "win")) ?? "";
        return string.Join("+", mods.Select(m => char.ToUpperInvariant(m[0]) + m[1..]).Append(ToTitle(key)));
    }

    private static bool SameCombo(string a, string b)
    {
        static HashSet<string> Set(string s) =>
            new(SplitTokens(s).Select(t => t switch
            {
                "control" => "ctrl",
                "del" => "delete",
                "ins" => "insert",
                "spacebar" => "space",
                _ => t
            }), StringComparer.OrdinalIgnoreCase);
        return Set(a).SetEquals(Set(b));
    }

    private static IEnumerable<string> SplitTokens(string spec) =>
        spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ToTitle(string token) =>
        token.Length == 0 ? token : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
}
