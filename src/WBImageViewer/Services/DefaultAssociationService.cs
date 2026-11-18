using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WBImageViewer.Services;

/// <summary>
/// Registers the viewer as a real per-user default handler.
///
/// Windows 8+ protects HKCU\...\FileExts\&lt;ext&gt;\UserChoice with a per-user hash.
/// Writing only OpenWithProgids makes the app a candidate but never the default, which is
/// why the installer used to open the Settings page. This service computes the same
/// UserChoice hash the shell uses, so the association is applied immediately and survives
/// the shell's next validation pass. The algorithm is undocumented by Microsoft and may
/// change in a future Windows update; failures are reported instead of being swallowed.
/// </summary>
internal static class DefaultAssociationService
{
    public const string ProgId = "GuaishushuViewer.Image";
    private const string VerbName = "GuaishushuViewer.Open";
    private const string Experience =
        "user choice set via windows user experience {d18b6dd5-6124-4341-9318-804003bafa0b}";

    /// <summary>
    /// Register ProgID, extension defaults, context menu and UserChoice defaults in one pass.
    /// Returns one entry per failed extension; an empty list means everything succeeded.
    /// </summary>
    public static List<string> ApplyAll(string executablePath, IEnumerable<string> extensions)
    {
        var errors = new List<string>();
        var list = extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        try
        {
            using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
            progId.SetValue(string.Empty, "怪叔叔看图");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{executablePath},0\"");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
        }
        catch (Exception ex)
        {
            errors.Add($"注册 ProgID 失败：{ex.Message}");
        }

        foreach (var extension in list)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
                key.SetValue(string.Empty, ProgId);
                using var openWith = key.CreateSubKey("OpenWithProgids");
                openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);

                if (!SetUserChoice(extension, ProgId))
                    errors.Add($"{extension}：默认程序哈希写入失败");

                RegisterContextMenu(extension, executablePath);
            }
            catch (Exception ex)
            {
                errors.Add($"{extension}：{ex.Message}");
            }
        }

        try
        {
            using var toasts = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts");
            foreach (var extension in list)
                toasts.SetValue(ProgId + "_" + extension, 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            errors.Add("抑制系统关联提示失败：" + ex.Message);
        }

        NotifyShell();
        return errors;
    }

    /// <summary>Explicit entry point used by --set-default and the installer.</summary>
    public static List<string> SetAllDefaults(IEnumerable<string> extensions)
    {
        var errors = new List<string>();
        foreach (var extension in extensions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!SetUserChoice(extension, ProgId))
                errors.Add($"{extension}：默认程序哈希写入失败");
        }
        NotifyShell();
        return errors;
    }

    /// <summary>Remove our UserChoice records and Explorer context menu entries.</summary>
    public static void RemoveAll(IEnumerable<string> extensions)
    {
        foreach (var extension in extensions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var userChoice = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice";
                using (var key = Registry.CurrentUser.OpenSubKey(userChoice, true))
                {
                    if (string.Equals(key?.GetValue("ProgId") as string, ProgId, StringComparison.OrdinalIgnoreCase))
                        Registry.CurrentUser.DeleteSubKey(userChoice, false);
                }

                using (var shell = Registry.CurrentUser.OpenSubKey(
                           $@"Software\Classes\SystemFileAssociations\{extension}\shell", true))
                    shell?.DeleteSubKeyTree(VerbName, false);
            }
            catch
            {
                // best-effort cleanup
            }
        }
        NotifyShell();
    }

    private static void RegisterContextMenu(string extension, string executablePath)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\SystemFileAssociations\{extension}\shell\{VerbName}");
        verb.SetValue(string.Empty, "用怪叔叔看图打开");
        verb.SetValue("Icon", $"\"{executablePath},0\"");
        using var command = verb.CreateSubKey("command");
        command.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
    }

    private static bool SetUserChoice(string extension, string progId)
    {
        try
        {
            var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
            var path = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{normalized}\UserChoice";

            // Prefer a clean recreate so LastWriteTime reflects "now". If Windows
            // protects an existing UserChoice key (denies delete), fall back to
            // opening it in place and overwriting ProgId/Hash instead of failing.
            RegistryKey? key;
            try
            {
                Registry.CurrentUser.DeleteSubKey(path, false);
                key = Registry.CurrentUser.CreateSubKey(path);
            }
            catch
            {
                key = Registry.CurrentUser.OpenSubKey(path, true);
            }
            if (key is null) return false;
            using (key)
            {
                var handle = key.Handle.DangerousGetHandle();
                if (RegQueryInfoKey(handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, out var lastWrite) != 0)
                    return false;

                var time = DateTime.FromFileTime(lastWrite);
                time = time.AddTicks(-(time.Ticks % 600000000)); // shell clamps to the minute
                var regDate = time.ToFileTime().ToString("x16");
                var sid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

                var source = (normalized + sid + progId + regDate + Experience + "\0").ToLowerInvariant();
                var bytes = Encoding.Unicode.GetBytes(source);
                var md5 = MD5.HashData(bytes);
                var hash = Convert.ToBase64String(XorBytes(Sub1(bytes, md5), Sub2(bytes, md5)));

                key.SetValue("ProgId", progId);
                key.SetValue("Hash", hash);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static byte[] XorBytes(byte[] a, byte[] b)
    {
        var result = new byte[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < a.Length; i++) result[i] ^= a[i];
        for (var i = 0; i < b.Length; i++) result[i] ^= b[i];
        return result;
    }

    // The next two functions reproduce the shell's undocumented 32-bit multiply/add mix.
    // All arithmetic must stay unchecked: overflow is part of the algorithm.
    private static byte[] Sub1(byte[] data, byte[] md5)
    {
        unchecked
        {
            var result = new byte[8];
            var length = (uint)((((data.Length >> 2) & 1) < 1 ? 1 : 0) + (data.Length >> 2) - 1);
            if (length <= 1 || (length & 1) == 1) return result;

            var words = new uint[length];
            for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt32(data, i * 4);

            var a = (BitConverter.ToUInt32(md5, 0) | 1) + 0x69FB0000u;
            var b = (BitConverter.ToUInt32(md5, 4) | 1) + 0x13DB0000u;

            uint v5 = 0, acc = 0, index = 0;
            var pairs = ((length - 2) >> 1) + 1;
            var tail = (length - 2) >> 1;
            var tailIndex = tail + 1;

            do
            {
                var x = words[index] + acc;
                index += 2;
                var hi = x >> 16;
                var t1 = 0x79F8A395u * (a * x - 0x10FA9605u * hi)
                         + 0x689B6B9Fu * ((a * x - 0x10FA9605u * hi) >> 16);
                var t2 = 0xEA970001u * t1 - 0x3C101569u * (t1 >> 16);
                v5 = t2 + v5;
                var y = words[index - 1] + t2;
                var t3 = b * y - 0x3CE8EC25u * (y >> 16);
                var t4 = 0x59C3AF2Du * t3 - 0x2232E0F1u * (t3 >> 16);
                acc = 0x1EC90001u * t4 + 0x35BD1EC9u * (t4 >> 16);
                --pairs;
            } while (pairs != 0);

            if (length - 2 - 2 * tail == 1)
            {
                var x = words[2 * tailIndex] + acc;
                var t1 = a * x - 0x10FA9605u * (x >> 16);
                var t2 = 0x39646B9Fu * (t1 >> 16) + 0x28DBA395u * t1
                         - 0x3C101569u * ((0x689B6B9Fu * (t1 >> 16) + 0x79F8A395u * t1) >> 16);
                var t3 = b * t2 - 0x3CE8EC25u * (t2 >> 16);
                var t4 = 0x59C3AF2Du * t3 - 0x2232E0F1u * (t3 >> 16);
                acc = 0x35BD1EC9u * (t4 >> 16) + 0x2A18AF2Du * t4 - 0xFD6BE0F1u * (t4 >> 16);
                v5 += acc + t2;
            }

            BitConverter.GetBytes(acc).CopyTo(result, 0);
            BitConverter.GetBytes(v5).CopyTo(result, 4);
            return result;
        }
    }

    private static byte[] Sub2(byte[] data, byte[] md5)
    {
        unchecked
        {
            var result = new byte[8];
            var length = (uint)((((data.Length >> 2) & 1) < 1 ? 1 : 0) + (data.Length >> 2) - 1);
            if (length <= 1 || (length & 1) == 1) return result;

            var words = new uint[length];
            for (var i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt32(data, i * 4);

            var v21 = BitConverter.ToUInt32(md5, 0) | 1;
            var v22 = BitConverter.ToUInt32(md5, 4) | 1;
            var v23 = 0xB1110000u * v21;
            var v24 = 0x16F50000u * v22;
            var rounds = ((length - 2) >> 1) + 1;
            var tail = (length - 2) >> 1;
            var tailIndex = tail + 1;

            uint v5 = 0, v7 = 0, index = 0;
            do
            {
                index += 2;
                var t1 = (words[index - 2] + v5) * v23 - 0x30674EEFu * (v21 * (words[index - 2] + v5) >> 16);
                var hi = t1 >> 16;
                var t2 = 0xE9B30000u * hi + 0x12CEB96Du * ((0x5B9F0000u * t1 - 0x78F7A461u * hi) >> 16);
                var t3 = 0x1D830000u * t2 + 0x257E1D83u * (t2 >> 16);
                var sum = t3 + words[index - 1];
                var p = sum * v24 - 0x5D8BE90Bu * (v22 * sum >> 16);
                var t4 = p >> 16;
                var t5 = 0x96FF0000u * p - (0x2C7C6901u * t4 >> 16);
                v5 = 0xF2310000u * t5 - 0x405B6097u * ((0x7C932B89u * t5 - 0x5C890000u * t4) >> 16);
                v7 += v5 + t3;
                --rounds;
            } while (rounds != 0);

            if (length - 2 - 2 * tail == 1)
            {
                var x = words[2 * tailIndex] + v5;
                var t1 = 0xB1110000u * v21 * x - 0x30674EEFu * (v21 * x >> 16);
                var hi = t1 >> 16;
                var t2 = (0x5B9F0000u * t1 - 0x78F7A461u * hi) >> 16;
                var t3 = 0x257E1D83u * ((0xE9B30000u * hi + 0x12CEB96Du * t2) >> 16) + 0x3BC70000u * t2;
                var p = 0x16F50000u * t3 * v22 - 0x5D8BE90Bu * (t3 * v22 >> 16);
                var t4 = p >> 16;
                var t5 = (0x96FF0000u * p - 0x2C7C6901u * t4) >> 16;
                v5 = 0xF2310000u * t5 - 0x405B6097u * ((0x7C932B89u * t5 - 0x5C890000u * t4) >> 16);
                v7 += v5 + t3;
            }

            BitConverter.GetBytes(v5).CopyTo(result, 0);
            BitConverter.GetBytes(v7).CopyTo(result, 4);
            return result;
        }
    }

    private static void NotifyShell()
    {
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKey", SetLastError = true)]
    private static extern int RegQueryInfoKey(
        IntPtr hKey, IntPtr lpClass, IntPtr lpcbClass, IntPtr lpReserved,
        IntPtr lpcSubKeys, IntPtr lpcbMaxSubKeyLen, IntPtr lpcbMaxClassLen,
        IntPtr lpcValues, IntPtr lpcbMaxValueNameLen, IntPtr lpcbMaxValueLen,
        IntPtr lpcbSecurityDescriptor, out long lpftLastWriteTime);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
