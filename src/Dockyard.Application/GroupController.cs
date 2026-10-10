using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>
/// Keeps a group's member windows placed over their panes and stacked around the frame. External windows are
/// not reparented (Q8): each one is moved to its pane's content area. Active tabs sit just above the frame in
/// z-order, inactive tabs just below it, so they stay listed by window switchers (Q11).
/// </summary>
public sealed class GroupController
{
    public static readonly LayoutMetrics Metrics = new(SplitterThickness: 4, TabBarHeight: 28);

    private sealed class Member(nint handle, PixelRect originalBounds)
    {
        public nint Handle { get; } = handle;
        public PixelRect OriginalBounds { get; } = originalBounds;
        public string Title { get; set; } = "";
        /// <summary>Smallest content size the app accepted, in layout units. Learned by observation.</summary>
        public Size MinSize { get; set; }
        /// <summary>Visible bounds last requested.</summary>
        public PixelRect? LastTarget { get; set; }
        /// <summary>Visible bounds when the user started dragging the window.</summary>
        public PixelRect? DragStart { get; set; }
        public SettleTracker Settle { get; } = new();
    }

    private readonly IGroupView _view;
    private readonly IWindowSystem _windows;
    private readonly IScheduler _scheduler;
    private readonly IRestartableTimer _settleTimer;
    private readonly LayoutTree _tree = new();
    private readonly Dictionary<nint, Member> _members = [];
    private LayoutResult? _layout;
    private nint _lastActive;
    /// <summary>Member the user is dragging or resizing, between move/size start and end.</summary>
    private nint _dragged;
    private bool _dragUpdatePending;
    /// <summary>True while we restack windows ourselves, so our own changes are not mistaken for the system's.</summary>
    private bool _raising;
    private bool _closing;

    public GroupController(IGroupView view, IWindowSystem windows, IScheduler scheduler)
    {
        _view = view;
        _windows = windows;
        _scheduler = scheduler;
        _settleTimer = scheduler.CreateTimer(TimeSpan.FromMilliseconds(300), CheckSettled);
    }

    public LayoutResult? Layout => _layout;

    public IEnumerable<nint> Members => _members.Keys;

    public bool Owns(nint window) => _members.ContainsKey(window);

    // ---- user commands ------------------------------------------------------------------------

    /// <summary>Adds a window as a tab of <paramref name="pane"/>, or in a new pane split off it on <paramref name="side"/>.</summary>
    public bool Adopt(nint window, Pane pane, DockSide? side)
    {
        // The picker is modal and pumps events: the window or its target pane may have gone away meanwhile.
        if (_closing || !_windows.Exists(window) || _members.ContainsKey(window) || !_tree.Panes.Contains(pane)) return false;
        if (_windows.IsMinimized(window) || _windows.IsMaximized(window)) _windows.Restore(window);
        _members[window] = new Member(window, _windows.GetBounds(window)) { Title = _windows.GetTitle(window) };
        if (side is { } s) _tree.Split(pane, s, window);
        else _tree.AddTab(pane, window);
        _lastActive = window;
        Refresh(raise: true, top: window);
        return true;
    }

    /// <summary>Takes a tab out of the group (its × button).</summary>
    public void Remove(nint window)
    {
        if (!_members.ContainsKey(window)) return;
        Release(window);
        Refresh(raise: true);
    }

    public void ActivateTab(nint window)
    {
        if (!_members.ContainsKey(window)) return;
        _tree.Activate(window);
        _lastActive = window;
        RenderVisuals();
        Raise(window);
    }

    /// <summary>The picker closed without adding anything: put the tabs back above the frame.</summary>
    public void PickerCancelled() => Raise(_lastActive);

    /// <summary>Drags a splitter to <paramref name="pointer"/> (surface coordinates along the split).</summary>
    public void DragSplitter(SplitterLayout splitter, double pointer)
    {
        var horizontal = splitter.Split.Orientation == Orientation.Horizontal;
        var length = horizontal ? splitter.SplitBounds.Width : splitter.SplitBounds.Height;
        var offset = pointer - (horizontal ? splitter.SplitBounds.X : splitter.SplitBounds.Y) - Metrics.SplitterThickness / 2;
        splitter.Split.Ratio = LayoutEngine.ClampRatio(splitter.Split, offset / (length - Metrics.SplitterThickness), length, Metrics, MinSizeOf);
        Refresh(raise: false);
    }

    /// <summary>Puts every member back where it was before it joined.</summary>
    public void ReleaseAll()
    {
        foreach (var h in _members.Keys.ToList()) Release(h);
    }

    // ---- frame events -------------------------------------------------------------------------

    public void OnFrameMoved() => SyncWindows(raise: false);

    public void OnSurfaceResized() => Refresh(raise: false);

    /// <summary>The system finishes bringing the frame forward after it is activated; raise the tabs once it has.</summary>
    public void OnFrameActivated() => _scheduler.Post(() => Raise(null));

    public void OnFrameMinimized()
    {
        foreach (var h in _members.Keys) _windows.Hide(h);
    }

    public void OnFrameRestored()
    {
        foreach (var h in _members.Keys) _windows.Show(h);
        Refresh(raise: true);
    }

    /// <summary>
    /// Called before the system changes the frame's z-order. When the frame comes forward (e.g. its tab bar is
    /// clicked), the active tabs are put there first and the returned window is where the frame should go
    /// instead: just below them. Raising them afterwards would let the frame cover them for a moment, which
    /// shows up as blank panes flashing. Returns null to leave the change as it is.
    /// </summary>
    public nint? OnFrameZChanging(FrameZChange change)
    {
        if (_raising || !_view.IsOnScreen) return null;
        Diagnostics.Trace?.Invoke($"frame z-change {change}");
        var actives = ActiveWindows();
        if (!ZOrderPolicy.ShouldCarryTabs(change, _members.Keys) || actives.Count == 0) return null;
        Stack(change.Placement == ZPlacement.Top ? 0 : change.Below, actives);
        return actives[^1];
    }

    public void OnClosing()
    {
        _closing = true;
        _settleTimer.Stop();
        ReleaseAll();
    }

    // ---- member window events -----------------------------------------------------------------

    public void OnForeground(nint window)
    {
        if (!Accepts(window, out _)) return;
        Diagnostics.Trace?.Invoke($"foreground {window:X}");
        if (_view.IsMinimized) _view.Restore();
        if (_tree.FindPane(window)!.ActiveWindow != window) _tree.Activate(window);
        _lastActive = window;
        RenderVisuals();
        Raise(window);
    }

    public void OnMoveSizeStart(nint window)
    {
        if (!Accepts(window, out var m)) return;
        _dragged = window;
        m.DragStart = _windows.GetVisibleBounds(window);
    }

    public void OnMoveSizeEnd(nint window)
    {
        if (!Accepts(window, out var m)) return;
        _dragged = 0;
        ApplyMemberDrag(m);
        m.DragStart = null;
        Refresh(raise: false);
    }

    public void OnLocationChanged(nint window)
    {
        if (!Accepts(window, out var m)) return;
        if (window == _dragged)
        {
            // Follow the drag live so the frame and the other panes do not jump on release. Location changes
            // arrive faster than we can re-layout, so coalesce them into one update.
            if (_dragUpdatePending) return;
            _dragUpdatePending = true;
            _scheduler.Post(() =>
            {
                _dragUpdatePending = false;
                // The drag may have ended, the window gone or the frame closed while this was queued.
                if (_dragged != window || _closing || !_members.ContainsKey(window)) return;
                ApplyMemberDrag(m);
                Refresh(raise: false);
            });
        }
        else if (_windows.IsMaximized(window))
        {
            _windows.Restore(window);
            SyncWindows(raise: false);
        }
        else if (_windows.IsMinimized(window) && !_view.IsMinimized)
        {
            _view.Minimize();
        }
    }

    public void OnTitleChanged(nint window)
    {
        if (!Accepts(window, out var m)) return;
        var title = _windows.GetTitle(window);
        if (title == m.Title) return;
        m.Title = title;
        RenderVisuals();
    }

    public void OnDestroyed(nint window)
    {
        if (!Accepts(window, out _)) return;
        if (_dragged == window) _dragged = 0;
        _members.Remove(window);
        _tree.Remove(window);
        Refresh(raise: false);
        RestackAtFrame();
    }

    private bool Accepts(nint window, out Member member) => _members.TryGetValue(window, out member!) && !_closing;

    // ---- layout and placement -----------------------------------------------------------------

    private void Release(nint window)
    {
        var m = _members[window];
        _members.Remove(window);
        _tree.Remove(window);
        if (!_windows.Exists(window)) return;
        _windows.Show(window);
        _windows.SetBounds(window, m.OriginalBounds);
    }

    private void Refresh(bool raise, nint? top = null)
    {
        if (_closing) return;
        RenderVisuals();
        SyncWindows(raise, top);
        _view.SetMinimumContentSize(LayoutEngine.MinSize(_tree.Root, Metrics, MinSizeOf));
    }

    private void RenderVisuals()
    {
        var size = _view.SurfaceSize;
        var bounds = new Rect(0, 0, size.Width, size.Height);
        LayoutEngine.ClampAll(_tree.Root, bounds, Metrics, MinSizeOf);
        _layout = LayoutEngine.Arrange(_tree.Root, bounds, Metrics);

        if (!_members.ContainsKey(_lastActive)) _lastActive = ActiveWindows().FirstOrDefault();
        var active = _members.TryGetValue(_lastActive, out var m) ? m.Title : "";
        _view.Render(new GroupSnapshot(_layout, _members.ToDictionary(p => p.Key, p => p.Value.Title), active));
    }

    private void SyncWindows(bool raise, nint? top = null)
    {
        if (_layout is null || !_view.IsOnScreen) return;
        foreach (var p in _layout.Panes)
        {
            var target = _view.ToScreen(p.Content);
            foreach (var h in p.Pane.Windows)
            {
                // The window being dragged belongs to the user until the drag ends; moving it would fight the drag.
                if (h == _dragged) continue;
                _members[h].LastTarget = target;
                _windows.PlaceVisible(h, target);
            }
        }
        if (raise) Raise(top);
        _settleTimer.Restart();
    }

    /// <summary>Z-order: [top, other active tabs..., frame, inactive tabs...].</summary>
    private void Raise(nint? top)
    {
        Diagnostics.Trace?.Invoke($"raise top={top:X} onScreen={_view.IsOnScreen}");
        if (!_view.IsOnScreen) return;
        var actives = ActiveWindows();
        if (top is { } t && actives.Remove(t)) actives.Insert(0, t);
        var order = actives.Append(_view.Handle).Concat(_tree.Windows.Except(actives)).ToList();
        Stack(_view.ZAnchor, order);
    }

    /// <summary>Exposes replacement tabs at the frame's current position without raising a background group.</summary>
    private void RestackAtFrame()
    {
        if (!_view.IsOnScreen) return;
        var actives = ActiveWindows();
        if (actives.Count == 0) return;
        Stack(_view.Handle, actives);
        Stack(actives[^1], [_view.Handle]);
    }

    private void Stack(nint after, IReadOnlyList<nint> topDown)
    {
        _raising = true;
        try
        {
            _windows.Stack(after, topDown);
        }
        finally
        {
            _raising = false;
        }
    }

    private List<nint> ActiveWindows() => _tree.Panes.Select(p => p.ActiveWindow).OfType<nint>().ToList();

    private Size MinSizeOf(nint window) => _members[window].MinSize;

    /// <summary>
    /// A member's invisible resize border covers the splitters and the frame edges, so dragging a member is how
    /// the user changes the layout: an edge drag moves that divider (or resizes the frame at an outer edge), and
    /// a title-bar drag moves the whole group.
    /// </summary>
    private void ApplyMemberDrag(Member m)
    {
        // Compare with where the drag started, not with where we last put the window: a window that keeps a
        // minimum size larger than its pane never matches its target, and a plain move would look like a resize.
        if ((m.DragStart ?? m.LastTarget) is not { } start || _layout is null || !_view.IsOnScreen) return;
        if (_tree.FindPane(m.Handle) is not { } pane) return;
        var content = _layout.Panes.First(p => p.Pane == pane).Content;
        var now = _windows.GetVisibleBounds(m.Handle);
        var r = _view.FromScreen(now);
        double left = r.X, top = r.Y, right = r.X + r.Width, bottom = r.Y + r.Height;

        var change = DragInterpreter.Interpret(start.ToRect(), now.ToRect());
        if (change.Moved)
        {
            _view.MoveBy(left - content.X, top - content.Y);
            return;
        }
        if (change.Resized.HasFlag(Edges.Right)) MoveEdge(pane, DockSide.Right, right, right - (content.X + content.Width));
        if (change.Resized.HasFlag(Edges.Left)) MoveEdge(pane, DockSide.Left, left - Metrics.SplitterThickness, left - content.X);
        if (change.Resized.HasFlag(Edges.Bottom)) MoveEdge(pane, DockSide.Bottom, bottom, bottom - (content.Y + content.Height));
        if (change.Resized.HasFlag(Edges.Top)) MoveEdge(pane, DockSide.Top, top - Metrics.TabBarHeight - Metrics.SplitterThickness, top - content.Y);
    }

    /// <param name="boundary">Where the divider's leading side should go, in surface coordinates.</param>
    /// <param name="delta">How far the edge moved, used when the edge is the frame's own border.</param>
    private void MoveEdge(Pane pane, DockSide edge, double boundary, double delta)
    {
        if (LayoutTree.FindDivider(pane, edge) is not { } split)
        {
            _view.ResizeEdge(edge, delta);
            return;
        }
        var bounds = _layout!.Splitters.First(s => s.Split == split).SplitBounds;
        var horizontal = split.Orientation == Orientation.Horizontal;
        var length = horizontal ? bounds.Width : bounds.Height;
        var origin = horizontal ? bounds.X : bounds.Y;
        split.Ratio = LayoutEngine.ClampRatio(split, (boundary - origin) / (length - Metrics.SplitterThickness), length, Metrics, MinSizeOf);
    }

    /// <summary>
    /// Runs once moves have settled. A window that stays larger than requested has a minimum size (Q9):
    /// remember it so splitters stop there. A window that ended up elsewhere gets its target again, a few
    /// times at most (see <see cref="SettleTracker"/>).
    /// </summary>
    private void CheckSettled()
    {
        if (_closing || !_view.IsOnScreen) return;
        bool retry = false, learned = false;
        foreach (var m in _members.Values)
        {
            if (m.LastTarget is not { } t || m.Handle == _dragged || _windows.IsMinimized(m.Handle) || !_windows.IsVisible(m.Handle)) continue;
            var now = _windows.GetVisibleBounds(m.Handle);
            switch (m.Settle.Evaluate(t.ToRect(), now.ToRect()))
            {
                case SettleAction.Retry:
                    retry = true;
                    break;
                case SettleAction.LearnMinimum:
                    m.MinSize = new Size(
                        now.Width > t.Width ? Math.Max(m.MinSize.Width, now.Width / _view.Scale) : m.MinSize.Width,
                        now.Height > t.Height ? Math.Max(m.MinSize.Height, now.Height / _view.Scale) : m.MinSize.Height);
                    learned = true;
                    break;
            }
        }
        if (learned)
        {
            // Make room for the new minimum: the frame grows if needed and the dividers move off the small panes
            // (RenderVisuals re-clamps every split against the new surface size).
            _view.SetMinimumContentSize(LayoutEngine.MinSize(_tree.Root, Metrics, MinSizeOf));
            Refresh(raise: false);
        }
        else if (retry)
        {
            SyncWindows(raise: false);
        }
    }
}
