using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WBImageViewer.Models;

namespace WBImageViewer.Services;

internal static class FileAssociationService
{
    private const string ProgId = "GuaishushuViewer.Image";

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico" };

    private const string SamplePng =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAAAeklEQVR4nO3PUQkAIBTAwJfJTIY1liH8" +
        "OITBAtzm7PV1wwUNaEEDWtCAFjSgBQ1oQQNa0IAWNKAFDWhBA1rQgBY0oAUNaEEDWtCAFjSgBQ1oQQNa0" +
        "IAWNKAFDWhBA1rQgBY0oAUNaEEDWtCAFjSgBQ1oQQNa0IAWNKAFDWhBA1rQgBY0oAUNaEEDWtCAFjSgBQ1o" +
        "QQNa0IAWPHYBfpzhPARTZgUAAAAASUVORK5CYII=";

    public static void Apply(AppSettings settings)
    {
        if (settings.AssociateAsDefaultViewer) Register(settings);
        else Revert(settings);
    }

    /// <summary>
    /// Register the handler. Modern Windows protects the "UserChoice" records, so the final
    /// one-time selection is completed through the Windows "Open with" dialog instead of
    /// forging the protected hash.
    /// </summary>
    private static void Register(AppSettings settings)
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GuaishushuViewer.exe");

        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(string.Empty, "该怪叔叔看图");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, $"\"{exe},0\"");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
        }

        using (var toasts = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\ApplicationAssociationToasts"))
        {
            foreach (var extension in Extensions)
                toasts.SetValue(ProgId + "_" + extension, 0, RegistryValueKind.DWord);
        }

        foreach (var extension in Extensions)
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
            {
                if (!settings.PreviousAssociations.ContainsKey(extension))
                    settings.PreviousAssociations[extension] = key.GetValue(string.Empty) as string;
                key.SetValue(string.Empty, ProgId);
            }

            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);
        }

        SettingsService.Save(settings);
        NotifyShell();
    }

    private static void Revert(AppSettings settings)
    {
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", false))
        {
            var neverRegistered = classes?.OpenSubKey(ProgId) == null && settings.PreviousAssociations.Count == 0;
            if (neverRegistered) return;
        }

        foreach (var extension in Extensions)
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
            {
                var current = key.GetValue(string.Empty) as string;
                var previous = settings.PreviousAssociations.TryGetValue(extension, out var value) ? value : null;
                if (string.Equals(current, ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(previous)) key.DeleteValue(string.Empty, false);
                    else key.SetValue(string.Empty, previous);
                }
            }

            using (var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids"))
                openWith.DeleteValue(ProgId, false);
        }

        settings.PreviousAssociations.Clear();

        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        root?.DeleteSubKeyTree(ProgId, false);

        SettingsService.Save(settings);
        NotifyShell();
    }

    /// <summary>
    /// Opens the supported Windows "Choose an app to open this file" dialog with a sample
    /// image, where the user only needs to pick WB 参考图 once.
    /// </summary>
    public static void OpenChooseAppDialog()
    {
        try
        {
            var sample = Path.Combine(Path.GetTempPath(), "guaishushu-viewer-sample.png");
            File.WriteAllBytes(sample, Convert.FromBase64String(SamplePng));
            Process.Start(new ProcessStartInfo(sample) { Verb = "openas", UseShellExecute = true });
            return;
        }
        catch
        {
            // fall back to the Settings page
        }

        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch
        {
            // nothing else we can do from here
        }
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
        return key != null;
    }

    private static void NotifyShell()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
