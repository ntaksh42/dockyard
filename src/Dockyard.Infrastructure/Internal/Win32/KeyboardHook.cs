using System.Runtime.InteropServices;
using Dockyard.Application;
using Dockyard.Domain;
using Dockyard.Infrastructure.Internal.Logging;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary>
/// Low-level keyboard hook for the shortcuts. A hook rather than RegisterHotKey, because a bound key may also
/// belong to Windows or another app (Ctrl+Win+←/→ switches virtual desktops) and must keep working when no group
/// has the focus: the receiver decides per key press whether to take it.
/// </summary>
internal sealed class KeyboardHook : IKeyboardSource, IDisposable
{
    private const int VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    /// <summary>
    /// Remote desktop clients that install a keyboard hook of their own while they have the focus, to send every
    /// key to the remote computer: mstsc and msrdc (the Windows App and Remote Desktop app client).
    /// </summary>
    private static readonly ProcessName[] RemoteDesktopClients = [new("mstsc"), new("msrdc")];

    private readonly MouseHookProc _proc; // kept alive so the GC does not collect the callback
    private nint _hook;
    private readonly HashSet<uint> _swallowed = [];
    // Modifiers a hook installed after ours took: Windows then does not update their key state.
    private readonly HashSet<uint> _heldElsewhere = [];
    private nint _foreground;
    private bool _remoteDesktopInFront;

    /// <summary>A key pressed with Ctrl, Alt or Win held, and the modifiers held. Returns true to take the key.</summary>
    public Func<int, KeyModifiers, bool>? KeyPressed { get; set; }

    public KeyboardHook()
    {
        _proc = OnKey;
        _hook = Install();
    }

    private nint Install()
    {
        var hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, 0, 0);
        if (hook == 0) Log.Error("Keyboard hook", new System.ComponentModel.Win32Exception());
        return hook;
    }

    /// <summary>
    /// Call on every foreground check, on the thread that created the hook. Low-level hooks run newest first, and
    /// a remote desktop client installs its hook whenever it gets the focus and takes every key there: while one
    /// is in front, the hook is installed again so the shortcuts reach this one before the client.
    /// </summary>
    public void StayAheadOf(nint foreground)
    {
        if (foreground != _foreground)
        {
            _foreground = foreground;
            GetWindowThreadProcessId(foreground, out var pid);
            _remoteDesktopInFront = foreground != 0 && RemoteDesktopClients.Contains(Win32WindowCatalog.ProcessNameOf(pid));
            // A release the client took while the secure desktop (Ctrl+Alt+Del) was up never reaches any hook.
            if (!_remoteDesktopInFront) _heldElsewhere.Clear();
        }
        if (!_remoteDesktopInFront) return;
        // Install before removing: no key goes by without a hook in between.
        var hook = Install();
        if (hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = hook;
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
                if (IsModifier(key.vkCode)) return PassModifier(key.vkCode, down, code, message, data);
            }
            // An exception escaping a native callback kills the process outright.
            catch (Exception ex) { Log.Error("Keyboard hook", ex); }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool IsModifier(uint key) => key is VK_LWIN or VK_RWIN or (>= VK_LSHIFT and <= VK_RMENU);

    /// <summary>
    /// Passes a modifier on and remembers it as held if the next hook took it (a remote desktop client sends it
    /// to the remote computer), since its key state then stays up.
    /// </summary>
    private nint PassModifier(uint key, bool down, int code, nint message, nint data)
    {
        var result = CallNextHookEx(_hook, code, message, data);
        if (!down) _heldElsewhere.Remove(key);
        else if (result != 0) _heldElsewhere.Add(key);
        return result;
    }

    private bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0 || _heldElsewhere.Contains((uint)key);

    /// <summary>
    /// The modifiers held. The right Alt key does not count, nor the left Ctrl key with it: on many layouts that
    /// is AltGr, which Windows reports as Ctrl+Alt, and AltGr with a key types a character (AltGr+8 is "[" on a
    /// German keyboard) that must not turn into a Ctrl+Alt shortcut.
    /// </summary>
    private KeyModifiers Modifiers()
    {
        var altGr = IsDown(VK_RMENU);
        return (IsDown(VK_RCONTROL) || (IsDown(VK_LCONTROL) && !altGr) ? KeyModifiers.Ctrl : 0) | (IsDown(VK_LMENU) ? KeyModifiers.Alt : 0) |
            (IsDown(VK_SHIFT) || IsDown(VK_LSHIFT) || IsDown(VK_RSHIFT) ? KeyModifiers.Shift : 0) | (IsDown(VK_LWIN) || IsDown(VK_RWIN) ? KeyModifiers.Win : 0);
    }


    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
    }
}
