using static Dockyard.Infrastructure.Native;

namespace Dockyard.Infrastructure;

/// <summary><see cref="IWindowSystem"/> on top of Win32.</summary>
public sealed class Win32WindowSystem : IWindowSystem
{
    private const uint ZOrderFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    public bool Exists(nint window) => IsWindow(window);
    public bool IsVisible(nint window) => IsWindowVisible(window);
    public bool IsMinimized(nint window) => IsIconic(window);
    public bool IsMaximized(nint window) => IsZoomed(window);
    public string GetTitle(nint window) => GetWindowText(window);

    public string GetProcessName(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return WindowCatalog.ProcessName(pid);
    }

    public string? GetProcessPath(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return Native.GetProcessPath(pid);
    }

    public PixelRect GetVisibleBounds(nint window) => ToPixel(Native.GetVisibleBounds(window));

    public PixelRect GetBounds(nint window) => GetWindowRect(window, out var r) ? ToPixel(r) : default;

    public void Restore(nint window) => ShowWindow(window, SW_RESTORE);
    public void Show(nint window) => ShowWindow(window, SW_SHOWNOACTIVATE);
    public void Hide(nint window) => ShowWindow(window, SW_HIDE);

    public void Focus(nint window)
    {
        if (SetForegroundWindow(window)) return;
        // Windows only hands the foreground to the process that received the last input. A synthetic press of
        // an unassigned key makes that this process.
        SendDummyKey();
        SetForegroundWindow(window);
    }

    /// <summary>Compensates for the invisible resize border, and does not wait for a slow or hung app.</summary>
    public void PlaceVisible(nint window, PixelRect target)
    {
        GetWindowRect(window, out var wr);
        var vb = Native.GetVisibleBounds(window);
        int l = vb.Left - wr.Left, t = vb.Top - wr.Top, r = wr.Right - vb.Right, b = wr.Bottom - vb.Bottom;
        SetWindowPos(window, 0, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// Shows a released window where it was and on top. Asynchronous: this runs while Dockyard exits, and a hung
    /// app must not keep it from putting the other windows back. The requests reach the window's thread in order.
    /// </summary>
    public void SetBounds(nint window, PixelRect bounds)
    {
        const uint async = SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS;
        SetWindowPos(window, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW | async);
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | async);
        SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | async);
    }

    public void Stack(nint after, IReadOnlyList<nint> topDown)
    {
        foreach (var w in topDown)
        {
            if (after == 0)
            {
                BringToTop(w);
            }
            else
            {
                SetWindowPos(w, after, 0, 0, 0, 0, ZOrderFlags);
                var above = GetWindow(w, GW_HWNDPREV);
                if (above != after) Log.Trace($"stack: 0x{w:X} wanted below 0x{after:X} but is below 0x{above:X} (foreground 0x{GetForegroundWindow():X})");
            }
            after = w;
        }
    }

    /// <summary>
    /// Puts another app's window at the top of the z-order without activating it. SetWindowPos(HWND_TOP) is
    /// silently ignored for other processes' windows (seen with Notepad and Paint even while Dockyard had the
    /// foreground); passing through the topmost band is not restricted and ends at the top of the normal band.
    /// </summary>
    private static void BringToTop(nint window)
    {
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, ZOrderFlags);
        SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, ZOrderFlags);
    }

    private static PixelRect ToPixel(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
