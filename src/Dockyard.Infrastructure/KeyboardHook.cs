using System.Runtime.InteropServices;
using static Dockyard.Infrastructure.Native;

namespace Dockyard.Infrastructure;

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>
/// Low-level keyboard hook for the shortcuts. A hook rather than RegisterHotKey, because a bound key may also
/// belong to Windows or another app (Ctrl+Win+←/→ switches virtual desktops) and must keep working when no group
/// has the focus: the receiver decides per key press whether to take it.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    private readonly MouseHookProc _proc; // kept alive so the GC does not collect the callback
    private readonly nint _hook;
    private readonly HashSet<uint> _swallowed = [];

    /// <summary>A key pressed with Ctrl, Alt or Win held, and the modifiers held. Returns true to take the key.</summary>
    public Func<int, KeyModifiers, bool>? KeyPressed { get; set; }

    public KeyboardHook()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, 0, 0);
        if (_hook == 0) Log.Error("Keyboard hook", new System.ComponentModel.Win32Exception());
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
                // Only our own synthetic keys are skipped: keys remapped by tools such as PowerToys or AutoHotkey
                // arrive injected too, and must still work as shortcuts.
                if (key.extraInfo == OwnInput) return CallNextHookEx(_hook, code, message, data);
                var down = message is 0x0100 or 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
                // The key-up of a key we took goes too, or the app would see a release without a press.
                if (!down && _swallowed.Remove(key.vkCode)) return 1;
                if (down && Modifiers() is var modifiers && (modifiers & ~KeyModifiers.Shift) != 0 &&
                    KeyPressed?.Invoke((int)key.vkCode, modifiers) == true)
                {
                    _swallowed.Add(key.vkCode);
                    // Releasing Win after a combination Windows did not see would open the Start menu.
                    SendDummyKey();
                    return 1;
                }
                if (down && _swallowed.Contains(key.vkCode)) return 1;
            }
            // An exception escaping a native callback kills the process outright.
            catch (Exception ex) { Log.Error("Keyboard hook", ex); }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>
    /// The modifiers held. The right Alt key does not count, nor the left Ctrl key with it: on many layouts that
    /// is AltGr, which Windows reports as Ctrl+Alt, and AltGr with a key types a character (AltGr+8 is "[" on a
    /// German keyboard) that must not turn into a Ctrl+Alt shortcut.
    /// </summary>
    private static KeyModifiers Modifiers()
    {
        var altGr = IsDown(VK_RMENU);
        return (IsDown(VK_RCONTROL) || (IsDown(VK_LCONTROL) && !altGr) ? KeyModifiers.Ctrl : 0) | (IsDown(VK_LMENU) ? KeyModifiers.Alt : 0) |
            (IsDown(VK_SHIFT) ? KeyModifiers.Shift : 0) | (IsDown(VK_LWIN) || IsDown(VK_RWIN) ? KeyModifiers.Win : 0);
    }


    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
    }
}
