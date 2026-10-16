using Dockyard.Domain;
using Dockyard.Application.Internal;

namespace Dockyard.Application.Tests;

/// <summary>
/// In-memory desktop. Windows have an invisible resize border (7px left, right and bottom, like Windows 10/11)
/// and can refuse to shrink below a minimum size. The z-order is a list, top first.
/// </summary>
internal sealed class FakeWindowSystem : IWindowSystem
{
    public const int Border = 7;

    public sealed class Window
    {
        public PixelRect Bounds;
        public string Title = "", ProcessName = "app";
        public string? ProcessPath;
        public bool Visible = true, Minimized, Maximized, Topmost;
        public bool HasTitleBar = true, TitleBarVisible = true;
        public int MinWidth, MinHeight;
    }

    public Dictionary<WindowHandle, Window> Windows { get; } = [];
    public List<WindowHandle> ZOrder { get; } = [];
    public bool IsStacking { get; set; }

    public WindowHandle Add(WindowHandle handle, PixelRect bounds, string title = "")
    {
        Windows[handle] = new Window { Bounds = bounds, Title = title };
        ZOrder.Insert(0, handle);
        return handle;
    }

    /// <summary>Puts a window (e.g. the frame) in the z-order without tracking anything else about it.</summary>
    public void AddToZOrder(WindowHandle handle) => ZOrder.Insert(0, handle);

    public void Close(WindowHandle handle)
    {
        Windows.Remove(handle);
        ZOrder.Remove(handle);
    }

    /// <summary>What the user sees: the window rectangle minus its invisible border.</summary>
    public static PixelRect VisibleOf(PixelRect b) => new(b.Left + Border, b.Top, b.Right - Border, b.Bottom - Border);

    public static PixelRect BoundsFor(PixelRect visible) => new(visible.Left - Border, visible.Top, visible.Right + Border, visible.Bottom + Border);

    public bool Exists(WindowHandle window) => Windows.ContainsKey(window);
    public bool IsVisible(WindowHandle window) => Windows[window].Visible;
    public bool IsMinimized(WindowHandle window) => Windows[window].Minimized;
    public bool IsMaximized(WindowHandle window) => Windows[window].Maximized;
    public bool IsTopmost(WindowHandle window) => Windows[window].Topmost;
    public string GetTitle(WindowHandle window) => Windows[window].Title;
    public WindowHandle GetForeground() => Focused;
    public WindowHandle GetWindowAt(int x, int y) => ZOrder.FirstOrDefault(h => Windows.TryGetValue(h, out var w) && w.Visible
        && x >= w.Bounds.Left && x < w.Bounds.Right && y >= w.Bounds.Top && y < w.Bounds.Bottom);
    public bool SetTitleBarVisible(WindowHandle window, bool visible)
    {
        var w = Windows[window];
        var changed = w.HasTitleBar && w.TitleBarVisible != visible;
        if (w.HasTitleBar) w.TitleBarVisible = visible;
        return changed;
    }
    public bool IsTopLevel(WindowHandle window) => Windows.ContainsKey(window);
    public bool IsCaptionDrag(WindowHandle window) => false;
    public IReadOnlyList<WindowHandle> GetTopLevelWindows() => ZOrder.ToList();
    public ProcessName GetProcessName(WindowHandle window) => new(Windows[window].ProcessName);
    public string? GetProcessPath(WindowHandle window) => Windows[window].ProcessPath;
    public PixelRect GetVisibleBounds(WindowHandle window) => VisibleOf(Windows[window].Bounds);
    public PixelRect GetBounds(WindowHandle window) => Windows[window].Bounds;

    public void Restore(WindowHandle window)
    {
        Windows[window].Minimized = false;
        Windows[window].Maximized = false;
    }

    public void Show(WindowHandle window) => Windows[window].Visible = true;
    public void Hide(WindowHandle window) => Windows[window].Visible = false;

    public WindowHandle Focused { get; private set; }

    public void Focus(WindowHandle window)
    {
        Focused = window;
        BringToTop(window);
    }

    public void PlaceVisible(WindowHandle window, PixelRect target)
    {
        var w = Windows[window];
        var visible = new PixelRect(target.Left, target.Top,
            target.Left + Math.Max(target.Width, w.MinWidth), target.Top + Math.Max(target.Height, w.MinHeight));
        w.Bounds = BoundsFor(visible);
    }

    public void SetBounds(WindowHandle window, PixelRect bounds)
    {
        Windows[window].Visible = true;
        Windows[window].Bounds = bounds;
        // Like the real one, which passes through the topmost band and ends in the normal one.
        Windows[window].Topmost = false;
        ZOrder.Remove(window);
        ZOrder.Insert(0, window);
    }

    public void SetTopmost(WindowHandle window) => Windows[window].Topmost = true;

    public void Stack(WindowHandle after, IReadOnlyList<WindowHandle> topDown)
    {
        foreach (var w in topDown)
        {
            if (Windows.TryGetValue(w, out var window)) window.Topmost = false;
            ZOrder.Remove(w);
            ZOrder.Insert(after.IsNone ? 0 : ZOrder.IndexOf(after) + 1, w);
            after = w;
        }
    }

    /// <summary>The user brings a window to the top (e.g. by clicking it).</summary>
    public void BringToTop(WindowHandle window)
    {
        ZOrder.Remove(window);
        ZOrder.Insert(0, window);
    }

    public int Z(WindowHandle window) => ZOrder.IndexOf(window);
}

/// <summary>
/// The frame. Its layout surface sits at <see cref="Origin"/> on the screen, one pixel per layout unit.
/// Like the WPF window, it reports its own moves and resizes back (see <see cref="Connect"/>).
/// </summary>
internal sealed class FakeView(WindowHandle handle) : IGroupView
{
    private GroupController? _controller;
    private IScheduler? _scheduler;

    /// <summary>
    /// Reports frame changes the controller causes, the way WPF does: a move right away (LocationChanged), an
    /// edge resize after the current message (SizeChanged at the next layout pass), and growth to a new minimum
    /// right away (the window forces a layout pass).
    /// </summary>
    public void Connect(GroupController controller, IScheduler scheduler)
    {
        _controller = controller;
        _scheduler = scheduler;
    }

    public (double X, double Y) Origin = (100, 200);
    public Size Surface = new(1000, 600);
    public Size MinimumContent;
    public GroupSnapshot? LastRender;
    public bool Minimized;

    public WindowHandle Handle { get; } = handle;
    public bool IsOnScreen => !Minimized;
    public bool IsMinimized => Minimized;
    public Size SurfaceSize => Surface;
    public double Scale => 1;
    public WindowHandle ZAnchor { get; set; }
    public WindowHandle TabOverlay { get; set; }

    public PixelRect ToScreen(Rect r) => new(
        (int)Math.Round(Origin.X + r.X), (int)Math.Round(Origin.Y + r.Y),
        (int)Math.Round(Origin.X + r.X + r.Width), (int)Math.Round(Origin.Y + r.Y + r.Height));

    public Rect FromScreen(PixelRect r) => new(r.Left - Origin.X, r.Top - Origin.Y, r.Width, r.Height);

    public void Render(GroupSnapshot snapshot) => LastRender = snapshot;

    public void MoveBy(double dx, double dy)
    {
        Origin = (Origin.X + dx, Origin.Y + dy);
        _controller?.OnFrameMoved();
    }

    public void ResizeEdge(DockSide edge, double delta)
    {
        switch (edge)
        {
            case DockSide.Right: Surface = Surface with { Width = Surface.Width + delta }; break;
            case DockSide.Bottom: Surface = Surface with { Height = Surface.Height + delta }; break;
            case DockSide.Left: Origin.X += delta; Surface = Surface with { Width = Surface.Width - delta }; break;
            case DockSide.Top: Origin.Y += delta; Surface = Surface with { Height = Surface.Height - delta }; break;
        }
        _scheduler?.Post(() => _controller?.OnSurfaceResized());
    }

    public void SetMinimumContentSize(Size min)
    {
        MinimumContent = min;
        var grown = new Size(Math.Max(Surface.Width, min.Width), Math.Max(Surface.Height, min.Height));
        if (grown == Surface) return;
        Surface = grown;
        _controller?.OnSurfaceResized();
    }

    public void Minimize() => Minimized = true;
    public void Restore() => Minimized = false;
}

/// <summary>Runs posted work and timers only when the test says so.</summary>
internal sealed class ManualScheduler : IScheduler
{
    private readonly Queue<Action> _posted = [];
    private readonly List<Timer> _timers = [];

    public void Post(Action action) => _posted.Enqueue(action);

    public IRestartableTimer CreateTimer(TimeSpan delay, Action tick)
    {
        var t = new Timer(tick);
        _timers.Add(t);
        return t;
    }

    public void RunPosted()
    {
        while (_posted.TryDequeue(out var a)) a();
    }

    /// <summary>Fires every timer that is due, once.</summary>
    public void FireTimers()
    {
        foreach (var t in _timers.Where(t => t.Pending).ToList()) t.Fire();
    }

    private sealed class Timer(Action tick) : IRestartableTimer
    {
        public bool Pending { get; private set; }
        public void Restart() => Pending = true;
        public void Stop() => Pending = false;

        public void Fire()
        {
            Pending = false;
            tick();
        }
    }
}
