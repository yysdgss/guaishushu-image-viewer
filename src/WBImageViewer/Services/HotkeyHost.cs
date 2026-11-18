using System.Text;
using System.Windows.Input;
using System.Windows.Interop;
using WBImageViewer.Models;
using WBImageViewer.Native;

namespace WBImageViewer.Services;

public enum HotkeyAction
{
    ToggleTopMost,
    FitWindowToImage,
    FitImageToWindow
}

/// <summary>
/// Registers global hotkeys on a message-only HWND so they work regardless of which window has focus,
/// and regardless of how many image panels are open.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();

    public event Action<HotkeyAction>? Pressed;

    public HotkeyHost()
    {
        var parameters = new HwndSourceParameters("WBImageViewer_HotkeyHost", 0, 0)
        {
            ParentWindow = new IntPtr(Win32.HWND_MESSAGE)
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Reconfigure(AppSettings settings)
    {
        foreach (var id in _registered.Keys.ToArray())
            Win32.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();

        Register(1, HotkeyAction.ToggleTopMost, settings.ToggleTopMostHotkey);
        Register(2, HotkeyAction.FitWindowToImage, settings.FitWindowToImageHotkey);
        Register(3, HotkeyAction.FitImageToWindow, settings.FitImageToWindowHotkey);
    }

    private void Register(int id, HotkeyAction action, string spec)
    {
        if (HotkeySpec.TryParse(spec, out var modifiers, out var vk) &&
            Win32.RegisterHotKey(_source.Handle, id, modifiers, vk))
        {
            _registered[id] = action;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Win32.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys.ToArray())
            Win32.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

internal static class HotkeySpec
{
    public static bool TryParse(string? spec, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var key = Key.None;
        foreach (var token in Split(spec))
        {
            if (token.Equals("shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32.MOD_SHIFT;
            else if (token.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("control", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32.MOD_CONTROL;
            else if (token.Equals("alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= Win32.MOD_ALT;
            else if (Enum.TryParse(token, true, out Key parsed) && parsed != Key.None)
                key = parsed;
            else
                return false;
        }

        if (key == Key.None || modifiers == 0) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return true;
    }

    /// <summary>True when every configured modifier is currently held down.</summary>
    public static bool IsModifierDown(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var allDown = true;
        foreach (var token in Split(spec))
        {
            if (token.Equals("shift", StringComparison.OrdinalIgnoreCase))
                allDown &= Win32.IsKeyDown(Win32.VK_SHIFT);
            else if (token.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("control", StringComparison.OrdinalIgnoreCase))
                allDown &= Win32.IsKeyDown(Win32.VK_CONTROL);
            else if (token.Equals("alt", StringComparison.OrdinalIgnoreCase))
                allDown &= Win32.IsKeyDown(Win32.VK_MENU);
            else
                return false;
        }
        return allDown;
    }

    private static string[] Split(string spec) =>
        spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
