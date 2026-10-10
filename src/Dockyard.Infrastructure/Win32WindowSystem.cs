using Dockyard.Application;
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

    public PixelRect GetVisibleBounds(nint window) => ToPixel(Native.GetVisibleBounds(window));

    public PixelRect GetBounds(nint window) => GetWindowRect(window, out var r) ? ToPixel(r) : default;

    public void Restore(nint window) => ShowWindow(window, SW_RESTORE);
    public void Show(nint window) => ShowWindow(window, SW_SHOWNOACTIVATE);
    public void Hide(nint window) => ShowWindow(window, SW_HIDE);

    /// <summary>Compensates for the invisible resize border, and does not wait for a slow or hung app.</summary>
    public void PlaceVisible(nint window, PixelRect target)
    {
        GetWindowRect(window, out var wr);
        var vb = Native.GetVisibleBounds(window);
        int l = vb.Left - wr.Left, t = vb.Top - wr.Top, r = wr.Right - vb.Right, b = wr.Bottom - vb.Bottom;
        SetWindowPos(window, 0, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    public void SetBounds(nint window, PixelRect bounds) =>
        SetWindowPos(window, HWND_TOP, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public void Stack(nint after, IReadOnlyList<nint> topDown)
    {
        foreach (var w in topDown)
        {
            SetWindowPos(w, after, 0, 0, 0, 0, ZOrderFlags);
            after = w;
        }
    }

    private static PixelRect ToPixel(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
