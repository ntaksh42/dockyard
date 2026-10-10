using System.Runtime.InteropServices;
using System.Text;

namespace Dockyard.UiTests;

internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public override readonly string ToString() => $"({Left},{Top} {Width}x{Height})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    public static RECT WorkArea(nint hwnd)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi);
        return mi.rcWork;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, nint extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint hwnd, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    public const uint MOUSEEVENTF_LEFTDOWN = 0x02, MOUSEEVENTF_LEFTUP = 0x04;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const int SW_RESTORE = 9;

    public static string Title(nint hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static uint Pid(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static RECT Visible(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, 9, out RECT r, Marshal.SizeOf<RECT>()) == 0 ? r : GetWindowRect(hwnd, out r) ? r : default;

    public static RECT NormalBounds(nint hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(hwnd, ref wp);
        return wp.rcNormalPosition;
    }

    public static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, 14, out int c, 4) == 0 && c != 0;

    /// <summary>Visible, uncloaked top-level windows from the top of the z-order down.</summary>
    public static List<nint> ZOrder()
    {
        var list = new List<nint>();
        for (var h = GetTopWindow(0); h != 0; h = GetWindow(h, 2))
            if (IsWindowVisible(h) && !IsCloaked(h)) list.Add(h);
        return list;
    }

    public static nint FindWindow(Func<nint, bool> match)
    {
        nint found = 0;
        EnumWindows((h, _) =>
        {
            if (!match(h)) return true;
            found = h;
            return false;
        }, 0);
        return found;
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    public static void Drag(int x1, int y1, int x2, int y2, int steps = 25)
    {
        SetCursorPos(x1, y1);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(120);
        for (var i = 1; i <= steps; i++)
        {
            SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
            Thread.Sleep(20);
        }
        Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    /// <summary>SetForegroundWindow only works for the foreground process; a tap of Alt lifts that restriction.</summary>
    public static void Activate(nint hwnd)
    {
        keybd_event(0x12, 0, 0, 0);
        keybd_event(0x12, 0, 2, 0);
        SetForegroundWindow(hwnd);
    }
}
