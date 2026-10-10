using System.Runtime.InteropServices;
using static Dockyard.Infrastructure.Native;

namespace Dockyard.Infrastructure;

/// <summary>
/// Low-level keyboard hook for the Ctrl+Win shortcuts. A hook rather than RegisterHotKey, because some of the
/// keys belong to Windows (Ctrl+Win+←/→ switches virtual desktops) and must keep working when no group has the
/// focus: the receiver decides per key press whether to take it.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly MouseHookProc _proc; // kept alive so the GC does not collect the callback
    private readonly nint _hook;
    private readonly HashSet<uint> _swallowed = [];

    /// <summary>A key pressed with Ctrl and Win held, and whether Shift is held. Returns true to take the key.</summary>
    public Func<int, bool, bool>? KeyPressed { get; set; }

    public KeyboardHook()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, 0, 0);
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
                if ((key.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, code, message, data);
                var down = message is 0x0100 or 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
                // The key-up of a key we took goes too, or the app would see a release without a press.
                if (!down && _swallowed.Remove(key.vkCode)) return 1;
                if (down && IsDown(VK_CONTROL) && !IsDown(VK_MENU) && (IsDown(VK_LWIN) || IsDown(VK_RWIN)) &&
                    KeyPressed?.Invoke((int)key.vkCode, IsDown(VK_SHIFT)) == true)
                {
                    _swallowed.Add(key.vkCode);
                    // Releasing Win after a combination Windows did not see would open the Start menu.
                    keybd_event(0xE8, 0, 0, 0);
                    keybd_event(0xE8, 0, 2 /* KEYEVENTF_KEYUP */, 0);
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

    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
    }
}
