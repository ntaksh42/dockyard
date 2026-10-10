using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>A rectangle in physical screen pixels, as Windows reports window bounds.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public Rect ToRect() => new(Left, Top, Width, Height);
}

/// <summary>Operations on other applications' top-level windows. Implemented with Win32 in Infrastructure.</summary>
public interface IWindowSystem
{
    bool Exists(nint window);
    bool IsVisible(nint window);
    bool IsMinimized(nint window);
    bool IsMaximized(nint window);
    string GetTitle(nint window);

    /// <summary>What the user sees, without the invisible resize border most windows have.</summary>
    PixelRect GetVisibleBounds(nint window);

    /// <summary>The full window rectangle, including the invisible resize border.</summary>
    PixelRect GetBounds(nint window);

    void Restore(nint window);
    void Show(nint window);
    void Hide(nint window);

    /// <summary>Gives the window the keyboard focus (makes it the foreground window).</summary>
    void Focus(nint window);

    /// <summary>Moves the window so its visible bounds match <paramref name="target"/>. Does not wait for the app.</summary>
    void PlaceVisible(nint window, PixelRect target);

    /// <summary>Sets the full window rectangle and brings the window to the top without activating it.</summary>
    void SetBounds(nint window, PixelRect bounds);

    /// <summary>
    /// Orders windows top to bottom: the first goes just below <paramref name="after"/> (0 for the very top of
    /// the z-order), each following one just below the previous.
    /// </summary>
    void Stack(nint after, IReadOnlyList<nint> topDown);
}

/// <summary>The group frame as the controller sees it. Implemented by the WPF window in the App.</summary>
public interface IGroupView
{
    /// <summary>The frame's own window handle.</summary>
    nint Handle { get; }

    /// <summary>False while minimized, closing or not yet shown; screen mapping is not possible then.</summary>
    bool IsOnScreen { get; }

    bool IsMinimized { get; }

    /// <summary>Size of the area panes are laid out in, in the units layouts use (WPF DIPs).</summary>
    Size SurfaceSize { get; }

    /// <summary>Physical pixels per layout unit.</summary>
    double Scale { get; }

    /// <summary>0 when tabs may go to the top of the z-order; otherwise a window they must stay below, such as an open dialog.</summary>
    nint ZAnchor { get; }

    /// <summary>A window drawing tab bars over the member windows (tab bar placement B), or 0.</summary>
    nint TabOverlay { get; }

    PixelRect ToScreen(Rect surfaceRect);
    Rect FromScreen(PixelRect screenRect);

    void Render(GroupSnapshot snapshot);
    void MoveBy(double dx, double dy);
    void ResizeEdge(DockSide edge, double delta);

    /// <summary>Sets the smallest content size and grows the frame if it is currently smaller.</summary>
    void SetMinimumContentSize(Size min);

    void Minimize();
    void Restore();
}

/// <summary>What the frame has to draw.</summary>
public sealed record GroupSnapshot(LayoutResult Layout, IReadOnlyDictionary<nint, string> Titles, string ActiveTitle);

/// <summary>Deferred work on the UI thread.</summary>
public interface IScheduler
{
    /// <summary>Runs <paramref name="action"/> after the current message has been processed.</summary>
    void Post(Action action);

    IRestartableTimer CreateTimer(TimeSpan delay, Action tick);
}

/// <summary>A one-shot timer; restarting it pushes the tick back.</summary>
public interface IRestartableTimer
{
    void Restart();
    void Stop();
}
