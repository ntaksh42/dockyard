using Dockyard.Domain;

namespace Dockyard.Application.Internal;

/// <summary>
/// Keeps a group's member windows placed over their panes and stacked around the frame. External windows are
/// not reparented (Q8): each one is moved to its pane's content area. Active tabs sit just above the frame in
/// z-order, inactive tabs just below it, so they stay listed by window switchers (Q11).
/// </summary>
internal sealed class GroupController : IGroupControl
{
    public LayoutMetrics Metrics { get; private set; } = new(SplitterThickness: 4, TabBarHeight: 24);

    private sealed class Member(nint handle, PixelRect originalBounds)
    {
        public nint Handle { get; } = handle;
        public PixelRect OriginalBounds { get; } = originalBounds;
        public string Title { get; set; } = "";
        public string ProcessName { get; init; } = "";
        public string? ProcessPath { get; init; }
        /// <summary>Whether the window was always on top before it joined; stacking it in the group ends that, so it is given back on release.</summary>
        public bool Topmost { get; init; }
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
    /// <summary>Saved tabs whose windows were not open when the layout was restored, by the pane they belong in.</summary>
    private readonly Dictionary<Pane, List<SavedTab>> _waiting = [];
    private LayoutResult? _layout;
    private nint _lastActive;
    /// <summary>Member the user is dragging or resizing, between move/size start and end.</summary>
    private nint _dragged;
    private bool _dragUpdatePending;
    /// <summary>True while we restack windows ourselves, so our own changes are not mistaken for the system's.</summary>
    private bool _raising;
    private bool _closing;
    /// <summary>
    /// While the frame is dragged, resized or maximized, the view shows the members as live thumbnails drawn in
    /// the frame, which move with it exactly; the real windows wait behind the frame meanwhile (see BeginLive).
    /// </summary>
    private bool _live;

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

    /// <summary>The tab the user last worked in, which keyboard shortcuts act on when the frame has the focus.</summary>
    public nint? LastActive => _members.ContainsKey(_lastActive) ? _lastActive : null;

    /// <summary>Raised after the layout or the members change, so the host can keep the session file current.</summary>
    public event Action? Changed;

    // ---- saving and restoring -----------------------------------------------------------------

    public SavedNode SaveLayout(bool includeWindows = true) => Save(_tree.Root, includeWindows);

    /// <summary>
    /// Replaces an empty group's layout with a saved one. <paramref name="resolve"/> picks the window for each
    /// saved tab (0 for none); tabs whose windows are not found wait for one (see <see cref="FillWaiting"/>), and
    /// their panes stay empty until then (Q3). Returns how many windows joined.
    /// </summary>
    public int RestoreLayout(SavedNode layout, Func<SavedTab, nint> resolve)
    {
        if (_closing || _members.Count > 0) return 0;
        _waiting.Clear();
        _tree.Load(Build(layout, resolve));
        _lastActive = ActiveWindows().FirstOrDefault();
        Refresh(raise: true);
        return _members.Count;
    }

    private SavedNode Save(LayoutNode node, bool includeWindows) => node switch
    {
        SplitNode s => new SavedNode(s.Orientation, s.Ratio, Save(s.First, includeWindows), Save(s.Second, includeWindows)),
        Pane p => new SavedNode(
            Tabs: p.Windows.Select(h => includeWindows
                    ? new SavedTab(_members[h].ProcessName, _members[h].Title, h, _members[h].OriginalBounds, _members[h].ProcessPath)
                    : new SavedTab(_members[h].ProcessName, _members[h].Title, Path: _members[h].ProcessPath))
                .Concat(Waiting(p)).ToList(),
            ActiveIndex: p.ActiveIndex),
        _ => throw new InvalidOperationException(),
    };

    private LayoutNode Build(SavedNode node, Func<SavedTab, nint> resolve)
    {
        if (node is { Split: { } orientation, First: { } first, Second: { } second })
            return new SplitNode(orientation, Build(first, resolve), Build(second, resolve)) { Ratio = Math.Clamp(node.Ratio, 0, 1) };
        var pane = new Pane();
        var tabs = node.Tabs ?? [];
        for (var i = 0; i < tabs.Count; i++)
        {
            var h = resolve(tabs[i]);
            if (h == 0 || _members.ContainsKey(h) || !_windows.Exists(h))
            {
                // Not the window: a window of the same app may still come.
                if (!_waiting.TryGetValue(pane, out var waiting)) _waiting[pane] = waiting = [];
                waiting.Add(tabs[i] with { Handle = 0, OriginalBounds = null });
                continue;
            }
            if (_windows.IsMinimized(h) || _windows.IsMaximized(h)) _windows.Restore(h);
            // A group minimized when Dockyard went down left its windows hidden.
            _windows.Show(h);
            // The saved place to go back to belongs to the saved window: another window of the same app keeps its own.
            _members[h] = new Member(h, (h == tabs[i].Handle ? tabs[i].OriginalBounds : null) ?? _windows.GetBounds(h))
            {
                Title = _windows.GetTitle(h),
                ProcessName = _windows.GetProcessName(h),
                ProcessPath = _windows.GetProcessPath(h),
                Topmost = _windows.IsTopmost(h),
            };
            if (i == node.ActiveIndex) pane.ActiveIndex = pane.Windows.Count;
            pane.Windows.Add(h);
        }
        return pane;
    }

    /// <summary>Restored tabs still waiting for a window of their app.</summary>
    public IReadOnlyList<SavedTab> WaitingTabs => _tree.Panes.SelectMany(Waiting).ToList();

    private IEnumerable<SavedTab> Waiting(Pane pane) => _waiting.TryGetValue(pane, out var tabs) ? tabs : [];

    /// <summary>Takes the window out of the tree, keeping its pane while restored tabs still wait there.</summary>
    private void Detach(nint window) => _tree.Remove(window, collapse: !Waiting(_tree.FindPane(window)!).Any());

    /// <summary>
    /// A window just opened: if a restored tab waits for its app, the window takes that tab's place, preferring
    /// the tab with the same title. Returns false if no tab waits for it.
    /// </summary>
    public bool FillWaiting(nint window, string processName, string title)
    {
        if (_closing) return false;
        var candidates = _tree.Panes
            .SelectMany(p => Waiting(p).Select(t => (Pane: p, Tab: t)))
            .Where(w => w.Tab.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return false;
        var (pane, tab) = candidates.FirstOrDefault(w => w.Tab.Title == title) is { Pane: not null } same ? same : candidates[0];
        _waiting[pane].Remove(tab);
        if (Adopt(window, pane, null)) return true;
        _waiting[pane].Add(tab);
        return false;
    }

    // ---- user commands ------------------------------------------------------------------------

    /// <summary>Adds a window as a tab of <paramref name="pane"/>, or in a new pane split off it on <paramref name="side"/>.</summary>
    public bool Adopt(nint window, Pane pane, DockSide? side, PixelRect? originalBounds = null)
    {
        // The picker is modal and pumps events: the window or its target pane may have gone away meanwhile.
        if (_closing || !_windows.Exists(window) || _members.ContainsKey(window) || !_tree.Panes.Contains(pane)) return false;
        if (_windows.IsMinimized(window) || _windows.IsMaximized(window)) _windows.Restore(window);
        _members[window] = new Member(window, originalBounds ?? _windows.GetBounds(window))
        {
            Title = _windows.GetTitle(window),
            ProcessName = _windows.GetProcessName(window),
            ProcessPath = _windows.GetProcessPath(window),
            Topmost = _windows.IsTopmost(window),
        };
        if (side is { } s) _tree.Split(pane, s, window);
        else _tree.AddTab(pane, window);
        _lastActive = window;
        // The group's other windows are hidden while it is minimized: this one is shown again, placed, with them.
        if (_view.IsMinimized) _windows.Hide(window);
        Refresh(raise: true, top: window);
        return true;
    }

    /// <summary>Moves a tab without losing its original bounds or learned minimum size.</summary>
    public bool Dock(nint window, Pane pane, DockSide? side, GroupController? source = null, PixelRect? originalBounds = null)
    {
        source ??= this;
        if (!source.Owns(window)) return Adopt(window, pane, side, originalBounds);
        if (_closing || source._closing || !_windows.Exists(window) || !_tree.Panes.Contains(pane)) return false;
        var oldPane = source._tree.FindPane(window)!;
        if (source == this && oldPane == pane)
        {
            if (side is null || pane.Windows.Count == 1)
            {
                ActivateTab(window);
                return true;
            }
        }
        var member = source._members[window];
        source.Detach(window);
        source._members.Remove(window);
        source._dragged = 0;
        member.DragStart = null;
        _members.Add(window, member);
        if (side is { } s) _tree.Split(pane, s, window);
        else _tree.AddTab(pane, window);
        _lastActive = window;
        if (source != this)
        {
            source.Refresh(raise: false);
            source.RestackAtFrame();
        }
        Refresh(raise: true, top: window);
        return true;
    }

    public void BeginDockDrag(nint window)
    {
        if (Owns(window)) _dragged = window;
    }

    public void EndDockDrag()
    {
        _dragged = 0;
        Refresh(raise: true);
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
        Diagnostics.Trace?.Invoke($"activate tab {window:X}");
        _tree.Activate(window);
        _lastActive = window;
        RenderVisuals();
        Raise(window);
    }

    /// <summary>Runs a keyboard shortcut on behalf of <paramref name="window"/>, a member that has the focus.</summary>
    public void Execute(ShortcutAction action, nint window)
    {
        if (!Accepts(window, out _) || _tree.FindPane(window) is not { } pane) return;
        if (action == ShortcutAction.RemoveFromGroup)
        {
            Remove(window);
            _windows.Focus(window);
            return;
        }
        if (action is ShortcutAction.MoveTabLeft or ShortcutAction.MoveTabRight or ShortcutAction.MoveTabUp or ShortcutAction.MoveTabDown)
        {
            MoveTab(window, pane, Shortcuts.Direction(action)!.Value);
            return;
        }
        if (action is ShortcutAction.GrowPane or ShortcutAction.ShrinkPane)
        {
            ResizePane(pane, action == ShortcutAction.GrowPane ? PaneStep : -PaneStep);
            return;
        }
        nint? target = null;
        if (action is >= ShortcutAction.Tab1 and <= ShortcutAction.Tab9)
        {
            // Like browsers, the last number goes to the last tab however many there are.
            var index = action == ShortcutAction.Tab9 ? pane.Windows.Count - 1 : action - ShortcutAction.Tab1;
            if (index < pane.Windows.Count) target = pane.Windows[index];
        }
        else if (action is ShortcutAction.PreviousTab or ShortcutAction.NextTab)
        {
            var step = action == ShortcutAction.NextTab ? 1 : -1;
            var count = pane.Windows.Count;
            target = pane.Windows[(pane.Windows.IndexOf(window) + step + count) % count];
        }
        else if (Shortcuts.Direction(action) is { } side && _layout is not null)
        {
            target = Navigation.FindNeighbor(_layout, pane, side)?.Pane.ActiveWindow;
        }
        if (target is not { } t) return;
        ActivateTab(t);
        _windows.Focus(t);
    }

    /// <summary>How far a pane grows or shrinks per key press.</summary>
    private const double PaneStep = 40;

    /// <summary>
    /// Moves the tab into the neighbouring pane on <paramref name="side"/>; without one there, splits its own pane
    /// so the tab gets a pane of its own on that side (if it shares the pane with other tabs).
    /// </summary>
    private void MoveTab(nint window, Pane pane, DockSide side)
    {
        if (_layout is not null && Navigation.FindNeighbor(_layout, pane, side) is { } neighbour) Dock(window, neighbour.Pane, null);
        else if (pane.Windows.Count > 1) Dock(window, pane, side);
        else return;
        _windows.Focus(window);
    }

    /// <summary>Moves the divider nearest to the pane so that the pane grows by <paramref name="delta"/> (shrinks if negative).</summary>
    private void ResizePane(Pane pane, double delta)
    {
        if (pane.Parent is not { } split || _layout?.Splitters.FirstOrDefault(s => s.Split == split) is not { } splitter) return;
        var horizontal = split.Orientation == Orientation.Horizontal;
        var length = horizontal ? splitter.SplitBounds.Width : splitter.SplitBounds.Height;
        var step = delta / Math.Max(1, length - Metrics.SplitterThickness);
        split.Ratio = LayoutEngine.ClampRatio(split, split.Ratio + (split.First == pane ? step : -step), length, Metrics, MinSizeOf);
        Refresh(raise: false);
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

    public void SetTabBarPlacement(TabBarPlacement placement)
    {
        if (Metrics.TabBarPlacement == placement) return;
        Metrics = Metrics with { TabBarPlacement = placement };
        Refresh(raise: true);
    }

    /// <summary>
    /// Apps (exe names) that draw controls in their own title bar, such as browsers with tabs there: with
    /// placement B, panes holding one of them keep their tabs above the content instead of covering it.
    /// </summary>
    public void SetTitleBarApps(IEnumerable<string> processNames)
    {
        var apps = processNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Metrics = Metrics with { KeepsTitleBar = pane => pane.Windows.Any(h => _members.TryGetValue(h, out var m) && apps.Contains(m.ProcessName)) };
        Refresh(raise: true);
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

    /// <summary>
    /// The frame starts moving or changing size. The members trail a frame they follow (each move reaches them
    /// a little after the frame's), which shows as blank or overlapping panes; until <see cref="EndLive"/> the
    /// view draws them as thumbnails instead, and the real windows keep following behind the frame.
    /// </summary>
    public void BeginLive()
    {
        if (_live || _closing || !_view.IsOnScreen) return;
        _live = true;
        Stack(_view.Handle, ActiveWindows());
    }

    /// <summary>The frame has settled: the members, in place by now, come back in front of it.</summary>
    public void EndLive()
    {
        if (!_live) return;
        _live = false;
        SyncWindows(raise: true);
    }

    public bool IsLive => _live;

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
        if (_raising || _live || !_view.IsOnScreen) return null;
        Diagnostics.Trace?.Invoke($"frame z-change {change}");
        var actives = ActiveWindows();
        if (!ZOrderPolicy.ShouldCarryTabs(change, [.. _members.Keys, _view.TabOverlay]) || actives.Count == 0) return null;
        Stack(change.Placement == ZPlacement.Top ? 0 : change.Below, [.. Overlay(), .. actives]);
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
        Detach(window);
        Refresh(raise: false);
        RestackAtFrame();
    }

    private bool Accepts(nint window, out Member member) => _members.TryGetValue(window, out member!) && !_closing;

    // ---- layout and placement -----------------------------------------------------------------

    private void Release(nint window)
    {
        var m = _members[window];
        _members.Remove(window);
        Detach(window);
        if (!_windows.Exists(window)) return;
        _windows.SetBounds(window, m.OriginalBounds);
        if (m.Topmost) _windows.SetTopmost(window);
    }

    private void Refresh(bool raise, nint? top = null)
    {
        if (_closing) return;
        Changed?.Invoke();

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
        var waiting = _tree.Panes.Where(p => Waiting(p).Any()).ToDictionary(p => p, p => (IReadOnlyList<SavedTab>)Waiting(p).ToList());
        _view.Render(new GroupSnapshot(_layout, _members.ToDictionary(p => p.Key, p => p.Value.Title), active, waiting));
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
        Diagnostics.Trace?.Invoke(
            $"raise top={top:X} actives={string.Join(",", ActiveWindows().Select(h => h.ToString("X")))} onScreen={_view.IsOnScreen} live={_live}");
        if (!_view.IsOnScreen || _live) return;
        var actives = ActiveWindows();
        if (top is { } t && actives.Remove(t)) actives.Insert(0, t);
        var order = Overlay().Concat(actives).Append(_view.Handle).Concat(_tree.Windows.Except(actives)).ToList();
        Stack(_view.ZAnchor, order);
    }

    /// <summary>Exposes replacement tabs at the frame's current position without raising a background group.</summary>
    private void RestackAtFrame()
    {
        if (!_view.IsOnScreen || _live) return;
        var actives = ActiveWindows();
        if (actives.Count == 0) return;
        Stack(_view.Handle, [.. Overlay(), .. actives]);
        Stack(actives[^1], [_view.Handle]);
    }

    /// <summary>The window drawing tab bars over the members (placement B), which goes above the active tabs.</summary>
    private nint[] Overlay() => _view.TabOverlay == 0 ? [] : [_view.TabOverlay];

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
        var (paneBounds, content) = _layout.Panes.Where(p => p.Pane == pane).Select(p => (p.Bounds, p.Content)).First();
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
        // The content may be inset from the pane by its tabs (above it, or left of it with placement C).
        if (change.Resized.HasFlag(Edges.Left)) MoveEdge(pane, DockSide.Left, left - (content.X - paneBounds.X) - Metrics.SplitterThickness, left - content.X);
        if (change.Resized.HasFlag(Edges.Bottom)) MoveEdge(pane, DockSide.Bottom, bottom, bottom - (content.Y + content.Height));
        if (change.Resized.HasFlag(Edges.Top)) MoveEdge(pane, DockSide.Top, top - (content.Y - paneBounds.Y) - Metrics.SplitterThickness, top - content.Y);
    }

    /// <param name="boundary">Where the divider's leading side should go, in surface coordinates.</param>
    /// <param name="delta">How far the edge moved, used when the edge is the frame's own border.</param>
    private void MoveEdge(Pane pane, DockSide edge, double boundary, double delta)
    {
        if (LayoutTree.FindDivider(pane, edge) is not { } split)
        {
            KeepOtherPanes(pane, edge, delta);
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
    /// The frame's <paramref name="edge"/> is about to move by <paramref name="delta"/> because the user drags
    /// that edge of <paramref name="pane"/>'s window: give all of it to that pane. Shared out by the ratios, the
    /// other panes would grow under the dragged window, which the user holds where it is until the drag ends.
    /// </summary>
    private void KeepOtherPanes(Pane pane, DockSide edge, double delta)
    {
        var orientation = edge is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
        var growth = edge is DockSide.Left or DockSide.Top ? -delta : delta;
        for (LayoutNode child = pane; child.Parent is { } split; child = split)
        {
            if (split.Orientation != orientation) continue;
            var bounds = _layout!.Splitters.First(s => s.Split == split).SplitBounds;
            var available = (orientation == Orientation.Horizontal ? bounds.Width : bounds.Height) - Metrics.SplitterThickness;
            if (available + growth <= 0) continue;
            // An outer edge: the pane is on that edge's side of every split along it. The other side keeps its size.
            split.Ratio = split.Second == child
                ? split.Ratio * available / (available + growth)
                : 1 - (1 - split.Ratio) * available / (available + growth);
        }
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
