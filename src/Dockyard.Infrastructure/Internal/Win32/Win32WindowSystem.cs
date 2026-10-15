using System.Diagnostics;
using Dockyard.Application;
using Dockyard.Domain;
using Dockyard.Infrastructure.Internal.Logging;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary><see cref="IWindowSystem"/> on top of Win32.</summary>
internal sealed class Win32WindowSystem : IWindowSystem
{
    // Without SWP_NOOWNERZORDER, a window's owned windows (dialogs) move with it and stay above it: with the flag
    // the owner would come out above its own modal dialog and hide it.
    private const uint ZOrderFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;

    private const uint VisibilityFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;

    // Waits at least this long are traced, to find out which apps make Dockyard wait.
    private const long SlowMs = 50;

    public bool Exists(WindowHandle window) => IsWindow(window.Value);
    public bool IsVisible(WindowHandle window) => IsWindowVisible(window.Value);
    public bool IsMinimized(WindowHandle window) => IsIconic(window.Value);
    public bool IsMaximized(WindowHandle window) => IsZoomed(window.Value);
    public bool IsTopmost(WindowHandle window) => ((long)GetWindowLongPtr(window.Value, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
    public string GetTitle(WindowHandle window) => GetWindowText(window.Value);
    public WindowHandle GetForeground() => new(GetForegroundWindow());
    public bool IsTopLevel(WindowHandle window) => GetAncestor(window.Value, 2 /* GA_ROOT */) == window.Value;
    public bool IsCaptionDrag(WindowHandle window) => Native.IsCaptionDrag(window.Value);

    public IReadOnlyList<WindowHandle> GetTopLevelWindows()
    {
        var windows = new List<WindowHandle>();
        EnumWindows((window, _) =>
        {
            windows.Add(new WindowHandle(window));
            return true;
        }, 0);
        return windows;
    }

    public ProcessName GetProcessName(WindowHandle window) => Win32WindowCatalog.ProcessNameOf(GetAppProcessId(window.Value));

    public string? GetProcessPath(WindowHandle window) => Native.GetProcessPath(GetAppProcessId(window.Value));

    public PixelRect GetVisibleBounds(WindowHandle window) => ToPixel(Native.GetVisibleBounds(window.Value));

    public PixelRect GetBounds(WindowHandle window) => GetWindowRect(window.Value, out var r) ? ToPixel(r) : default;

    public void Restore(WindowHandle window) => ShowWindowAsync(window.Value, SW_RESTORE);

    public void Show(WindowHandle window) => Request(window, "show", flags =>
        SetWindowPos(window.Value, 0, 0, 0, 0, 0, SWP_SHOWWINDOW | VisibilityFlags | flags));

    public void Hide(WindowHandle window) => Request(window, "hide", flags =>
        SetWindowPos(window.Value, 0, 0, 0, 0, 0, SWP_HIDEWINDOW | VisibilityFlags | flags));

    public void Focus(WindowHandle window)
    {
        var watch = Stopwatch.StartNew();
        if (!SetForegroundWindow(window.Value))
        {
            // Windows only hands the foreground to the process that received the last input. A synthetic press of
            // an unassigned key makes that this process.
            SendDummyKey();
            SetForegroundWindow(window.Value);
        }
        TraceIfSlow("focus", window, watch.ElapsedMilliseconds);
    }

    /// <summary>Compensates for the invisible resize border, and does not wait for a slow or hung app.</summary>
    public void PlaceVisible(WindowHandle window, PixelRect target)
    {
        GetWindowRect(window.Value, out var wr);
        var vb = Native.GetVisibleBounds(window.Value);
        int l = vb.Left - wr.Left, t = vb.Top - wr.Top, r = wr.Right - vb.Right, b = wr.Bottom - vb.Bottom;
        SetWindowPos(window.Value, 0, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// Shows a released window where it was and on top. Asynchronous: this runs while Dockyard exits, and a hung
    /// app must not keep it from putting the other windows back. The requests reach the window's thread in order.
    /// A position on a monitor that is gone is brought onto the nearest one, or the window could not be found.
    /// </summary>
    public void SetBounds(WindowHandle window, PixelRect bounds)
    {
        const uint async = SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
        var area = Native.GetWorkArea(new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom });
        var fit = Placement.FitToWorkArea(
            bounds.ToRect(), new Rect(area.Left, area.Top, area.Width, area.Height));
        bounds = new PixelRect((int)fit.X, (int)fit.Y, (int)(fit.X + fit.Width), (int)(fit.Y + fit.Height));
        SetWindowPos(window.Value, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW | async);
        SetWindowPos(window.Value, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | async);
        SetWindowPos(window.Value, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | async);
    }

    /// <summary>Asynchronous like <see cref="SetBounds"/>, whose requests it follows in order.</summary>
    public void SetTopmost(WindowHandle window) =>
        SetWindowPos(window.Value, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

    public void Stack(WindowHandle after, IReadOnlyList<WindowHandle> topDown)
    {
        var watch = Stopwatch.StartNew();
        foreach (var w in topDown)
        {
            if (after.IsNone)
            {
                BringToTop(w);
            }
            else
            {
                Request(w, "stack", flags => SetWindowPos(w.Value, after.Value, 0, 0, 0, 0, ZOrderFlags | flags));
            }
            after = w;
        }
        if (watch.ElapsedMilliseconds >= SlowMs) Log.Trace($"stack of {topDown.Count} windows took {watch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Puts another app's window at the top of the z-order without activating it. SetWindowPos(HWND_TOP) is
    /// silently ignored for other processes' windows (seen with Notepad and Paint even while Dockyard had the
    /// foreground); passing through the topmost band is not restricted and ends at the top of the normal band.
    /// </summary>
    private static void BringToTop(WindowHandle window) => Request(window, "raise", flags =>
    {
        // Both requests go the same way, so they reach the window's thread in this order.
        SetWindowPos(window.Value, HWND_TOPMOST, 0, 0, 0, 0, ZOrderFlags | flags);
        SetWindowPos(window.Value, HWND_NOTOPMOST, 0, 0, 0, 0, ZOrderFlags | flags);
    });

    /// <summary>
    /// Posts requests without probing the target first: an app can become busy after answering a probe.
    /// This thread also runs the input hooks, so waiting here delays input in every app.
    /// Requests to each window's thread retain their order; completion across threads is asynchronous.
    /// </summary>
    private static void Request(WindowHandle window, string operation, Action<uint> send)
    {
        var watch = Stopwatch.StartNew();
        send(SWP_ASYNCWINDOWPOS);
        TraceIfSlow(operation, window, watch.ElapsedMilliseconds);
    }

    private static void TraceIfSlow(string operation, WindowHandle window, long milliseconds)
    {
        if (milliseconds >= SlowMs) Log.Trace($"{operation}: 0x{window:X} ({ProcessNameOf(window)}) took {milliseconds}ms");
    }

    private static string ProcessNameOf(WindowHandle window) => Win32WindowCatalog.ProcessNameOf(GetAppProcessId(window.Value)).Value;

    private static PixelRect ToPixel(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
