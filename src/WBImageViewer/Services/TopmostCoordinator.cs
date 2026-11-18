using System.Windows;
using System.Windows.Interop;
using WBImageViewer.Native;

namespace WBImageViewer.Services;

/// <summary>
/// Coordinates the three application windows (image panel, About, Settings) so that:
///  - they all share one topmost state (the pushpin toggles all of them together);
///  - whichever of them is activated is raised above the other two.
/// WPF's Topmost places a window in the topmost band but does not guarantee it sits
/// above its siblings, so each activation explicitly re-inserts it at the top of that band.
/// </summary>
internal static class TopmostCoordinator
{
    private static readonly List<Window> Tracked = new();

    /// <summary>Current shared topmost state. Main window is the authority.</summary>
    public static bool TopmostEnabled { get; private set; } = true;

    public static void Register(Window window)
    {
        if (Tracked.Contains(window)) return;
        Tracked.Add(window);
        window.Topmost = TopmostEnabled;
        window.Activated += (_, _) => Raise(window);
        window.Closed += (_, _) => Tracked.Remove(window);
    }

    /// <summary>Apply a new topmost state to every tracked window.</summary>
    public static void SetTopmost(bool enabled)
    {
        TopmostEnabled = enabled;
        foreach (var window in Tracked.ToArray())
            window.Topmost = enabled;
    }

    /// <summary>Bring a tracked window to the top of its z-order band on activation.</summary>
    public static void Raise(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        // HWND_TOPMOST keeps it above all normal windows; HWND_TOP (0) only reorders
        // within the normal band when topmost is currently off.
        var insertAfter = TopmostEnabled ? Win32.HWND_TOPMOST : IntPtr.Zero;
        Win32.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }
}
