using System.Runtime.InteropServices;

namespace Dockyard.Presentation.Internal;

/// <summary>The Win32 calls the frame windows make on themselves: z-order messages, thumbnails, window styles.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public int minX, minY, maxX, maxY;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    public const int WM_WINDOWPOSCHANGING = 0x0046;

    public static readonly nint HWND_TOPMOST = -1;

    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_HIDEWINDOW = 0x0080;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;

    public const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8,
        DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    /// <summary>
    /// The window's rectangle in screen pixels as it is, or will be again, when neither minimized nor maximized.
    /// <c>GetWindowRect</c> gives the icon's (-32000, -32000, ...) and the maximized rectangle instead.
    /// </summary>
    public static RECT GetNormalBounds(nint hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement))
        {
            GetWindowRect(hwnd, out var current);
            return current;
        }
        // The normal position is in workspace coordinates, which start at the work area's corner, not the screen's.
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        int dx = info.rcWork.Left - info.rcMonitor.Left, dy = info.rcWork.Top - info.rcMonitor.Top;
        var r = placement.rcNormalPosition;
        return new RECT { Left = r.Left + dx, Top = r.Top + dy, Right = r.Right + dx, Bottom = r.Bottom + dy };
    }

    /// <summary>Work area (screen minus taskbar) of the monitor the window is mostly on.</summary>
    public static RECT GetWorkArea(nint hwnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        return info.rcWork;
    }

    /// <summary>
    /// The icon the window shows in its caption, or 0. Owned by the app: not to be destroyed. The app is asked
    /// first, with a short wait so a hung one cannot stall the caller; the window class's icon is the fallback.
    /// </summary>
    public static nint GetWindowIcon(nint hwnd)
    {
        const uint WM_GETICON = 0x007F, SMTO_ABORTIFHUNG = 2;
        const nint ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
        foreach (var kind in new[] { ICON_SMALL2, ICON_SMALL, ICON_BIG })
            if (SendMessageTimeout(hwnd, WM_GETICON, kind, 0, SMTO_ABORTIFHUNG, 50, out var icon) != 0 && icon != 0)
                return icon;
        const int GCLP_HICON = -14, GCLP_HICONSM = -34;
        var fromClass = GetClassLongPtr(hwnd, GCLP_HICONSM);
        return fromClass != 0 ? fromClass : GetClassLongPtr(hwnd, GCLP_HICON);
    }
}
