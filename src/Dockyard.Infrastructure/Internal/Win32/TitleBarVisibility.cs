using System.Runtime.InteropServices;
using Dockyard.Domain;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary>Changes only standard caption bits, keeping a recovery marker on the target window.</summary>
internal static class TitleBarVisibility
{
    private const string CaptionProperty = "Dockyard.OriginalCaption";
    private const int StyleIndex = -16;
    private const long CaptionStyle = 0x00C00000;

    public static bool SetVisible(WindowHandle window, bool visible)
    {
        var hwnd = window.Value;
        var saved = GetProp(hwnd, CaptionProperty);
        var style = (long)GetWindowLongPtr(hwnd, StyleIndex);
        if (visible)
        {
            if (saved == 0) return false;
            var restored = style | (long)saved;
            if (restored != style && !SetStyle(hwnd, restored)) return false;
            RemoveProp(hwnd, CaptionProperty);
            return restored != style;
        }

        if ((style & CaptionStyle) != CaptionStyle) return false;
        // Do not change an app unless its original caption can be recovered, including after a restart.
        if (saved == 0 && !SetProp(hwnd, CaptionProperty, (nint)(style & CaptionStyle))) return false;
        if (SetStyle(hwnd, style & ~CaptionStyle)) return true;
        if (saved == 0) RemoveProp(hwnd, CaptionProperty);
        return false;
    }

    private static bool SetStyle(nint hwnd, long style)
    {
        SetWindowLongPtr(hwnd, StyleIndex, (nint)style);
        if ((long)GetWindowLongPtr(hwnd, StyleIndex) != style) return false;
        SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | 0x0020 /* SWP_FRAMECHANGED */);
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint hwnd, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint hwnd, string name);
}
