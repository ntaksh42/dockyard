using System.Runtime.InteropServices;
using Dockyard.Domain;
using Dockyard.Infrastructure.Internal.Logging;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary>Changes only standard caption bits, keeping a recovery marker on the target window.</summary>
internal static class TitleBarVisibility
{
    private const string CaptionProperty = "Dockyard.OriginalCaption";
    private const int StyleIndex = -16;
    private const long CaptionStyle = 0x00C00000;
    private static readonly Dictionary<WindowHandle, (bool Visible, Task<bool> Change)> s_pending = [];

    public static Task<bool> SetVisibleAsync(WindowHandle window, bool visible)
    {
        lock (s_pending)
        {
            s_pending.TryGetValue(window, out var previous);
            if (previous.Change is not null && previous.Visible == visible) return previous.Change;
            if (previous.Change is null && !NeedsChange(window.Value, visible)) return Task.FromResult(false);
            var thread = GetWindowThreadProcessId(window.Value, out var process);
            var change = Task.Run(async () =>
            {
                if (previous.Change is not null) await previous.Change.ConfigureAwait(false);
                try
                {
                    // An older request may outlive a destroyed window. Do not change a handle reused by another app.
                    if (GetWindowThreadProcessId(window.Value, out var current) != thread || current != process) return false;
                    return SetVisible(window, visible);
                }
                catch (Exception ex)
                {
                    Log.Error("Change title bar", ex);
                    return false;
                }
            });
            s_pending[window] = (visible, change);
            _ = change.ContinueWith(_ =>
            {
                lock (s_pending)
                {
                    if (s_pending.TryGetValue(window, out var latest) && latest.Change == change) s_pending.Remove(window);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return change;
        }
    }

    private static bool NeedsChange(nint hwnd, bool visible)
    {
        if (visible || GetClassName(hwnd) is "WinUIDesktopWin32WindowClass" or "Chrome_WidgetWin_1")
        {
            return GetProp(hwnd, CaptionProperty) != 0;
        }
        return ((long)GetWindowLongPtr(hwnd, StyleIndex) & CaptionStyle) == CaptionStyle;
    }

    public static bool SetVisible(WindowHandle window, bool visible)
    {
        var hwnd = window.Value;
        var saved = GetProp(hwnd, CaptionProperty);
        var style = (long)GetWindowLongPtr(hwnd, StyleIndex);
        // WinUI and Chromium/Electron draw their own headers. Removing WS_CAPTION can expose a resize
        // strip or change their client insets. Also undo a caption change made by an earlier build.
        if (visible || GetClassName(hwnd) is "WinUIDesktopWin32WindowClass" or "Chrome_WidgetWin_1")
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
