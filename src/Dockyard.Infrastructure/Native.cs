using System.Runtime.InteropServices;
using System.Text;

namespace Dockyard.Infrastructure;

/// <summary>Win32 declarations shared by the Infrastructure and the App's own frame window.</summary>
public static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT point;
        public uint mouseData, flags, time;
        public nuint extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nuint extraInfo;
    }

    /// <summary>Low-level hook callback, for both the mouse and the keyboard hook.</summary>
    public delegate nint MouseHookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    public const int WM_WINDOWPOSCHANGING = 0x0046;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    public static readonly nint HWND_TOP = 0, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
        SWP_HIDEWINDOW = 0x0080, SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;

    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_RESTORE = 9;

    public const uint GW_OWNER = 4, GW_HWNDPREV = 3;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B,
        EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public const int OBJID_WINDOW = 0, CHILDID_SELF = 0;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, nuint extraInfo);

    /// <summary>Marks the key presses Dockyard sends itself, so its keyboard hook can tell them apart.</summary>
    public const nuint OwnInput = 0x444B5944; // "DKYD"

    /// <summary>
    /// A press of an unassigned key: makes this process the one that received the last input, and keeps a
    /// release of Win from opening the Start menu.
    /// </summary>
    public static void SendDummyKey()
    {
        keybd_event(0xE8, 0, 0, OwnInput);
        keybd_event(0xE8, 0, 2 /* KEYEVENTF_KEYUP */, OwnInput);
    }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int hook, MouseHookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    public static string GetWindowText(nint hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsCaptionDrag(nint hwnd)
    {
        if (!GetCursorPos(out var point)) return false;
        var coordinates = (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
        return SendMessageTimeout(hwnd, 0x0084 /* WM_NCHITTEST */, 0, coordinates,
            2 /* SMTO_ABORTIFHUNG */, 50, out var hit) != 0 && hit == 2 /* HTCAPTION */;
    }

    public static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Work area (screen minus taskbar) of the monitor the window is mostly on.</summary>
    public static RECT GetWorkArea(nint hwnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        return info.rcWork;
    }

    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Visible bounds without the invisible resize border Windows 10/11 adds around most windows.</summary>
    public static RECT GetVisibleBounds(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0
            ? r
            : GetWindowRect(hwnd, out r) ? r : default;
}
