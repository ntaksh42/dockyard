using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dockyard.Core;
using static Dockyard.Interop.Native;
using CoreRect = Dockyard.Core.Rect;
using CoreSize = Dockyard.Core.Size;
using Orientation = Dockyard.Core.Orientation;

namespace Dockyard;

/// <summary>
/// A group frame. External windows are not reparented (Q8): each one is kept positioned over its pane's
/// content area. Active tabs sit just above the frame in z-order, inactive tabs just below it.
/// </summary>
public partial class GroupWindow : Window
{
    private sealed class Member(nint handle, RECT originalBounds)
    {
        public nint Handle { get; } = handle;
        public RECT OriginalBounds { get; } = originalBounds;
        public string Title { get; set; } = "";
        /// <summary>Smallest content size the app accepted, in DIPs. Learned by observation.</summary>
        public CoreSize MinSize { get; set; }
        /// <summary>Visible bounds last requested, in physical pixels.</summary>
        public RECT? LastTarget { get; set; }
    }

    private static readonly LayoutMetrics Metrics = new(SplitterThickness: 4, TabBarHeight: 28);
    private static readonly Brush TabBarBrush = Brush("#D1D5DB"), TabBrush = Brush("#E6E8EC"), ActiveTabBrush = Brushes.White,
        AccentBrush = Brush("#3B82F6"), PlaceholderBrush = Brush("#1F2328"), SplitterBrush = Brushes.Transparent;

    private readonly LayoutTree _tree = new();
    private readonly Dictionary<nint, Member> _members = [];
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private LayoutResult? _layout;
    private SplitterLayout? _dragging;
    private nint _hwnd;
    private nint _lastActive;
    /// <summary>Member the user is currently dragging or resizing (between MOVESIZESTART and MOVESIZEEND).</summary>
    private nint _dragged;
    private bool _dragUpdatePending;
    private bool _raising;

    public GroupWindow(string name)
    {
        InitializeComponent();
        GroupName = name;
        GroupNameText.Text = name;
        UpdateTitle();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        };
        LocationChanged += (_, _) => SyncWindows(raise: false);
        Surface.SizeChanged += (_, _) => Refresh(raise: false);
        // Windows finishes bringing the frame to the top after Activated fires; raise the tabs once it has.
        Activated += (_, _) => Dispatcher.BeginInvoke(() => Raise(null), DispatcherPriority.Input);
        StateChanged += OnStateChanged;
        Closing += (_, _) => ReleaseAll();
        Surface.MouseMove += Surface_MouseMove;
        Surface.MouseLeftButtonUp += (_, _) => { _dragging = null; Surface.ReleaseMouseCapture(); };
        _settleTimer.Tick += (_, _) => { _settleTimer.Stop(); CheckSettled(); };
    }

    public string GroupName { get; }

    public bool Owns(nint hwnd) => _members.ContainsKey(hwnd);

    public void OnMemberEvent(uint evt, nint hwnd)
    {
        var m = _members[hwnd];
        switch (evt)
        {
            case EVENT_SYSTEM_FOREGROUND:
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (_tree.FindPane(hwnd)!.ActiveWindow != hwnd)
                {
                    _tree.Activate(hwnd);
                    RenderVisuals();
                }
                _lastActive = hwnd;
                UpdateTitle();
                Raise(hwnd);
                break;
            case EVENT_SYSTEM_MOVESIZESTART:
                _dragged = hwnd;
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                _dragged = 0;
                ApplyMemberDrag(m);
                Refresh(raise: false);
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (hwnd == _dragged)
                {
                    // Follow the drag live so the frame and the other panes do not jump on release. Location
                    // events arrive faster than we can re-layout, so coalesce them to one update per render.
                    if (_dragUpdatePending) break;
                    _dragUpdatePending = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _dragUpdatePending = false;
                        if (_dragged != hwnd) return;
                        ApplyMemberDrag(m);
                        Refresh(raise: false);
                    }, DispatcherPriority.Render);
                }
                else if (IsZoomed(hwnd))
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SyncWindows(raise: false);
                }
                else if (IsIconic(hwnd) && WindowState != WindowState.Minimized)
                {
                    WindowState = WindowState.Minimized;
                }
                break;
            case EVENT_OBJECT_NAMECHANGE:
                var title = GetWindowText(hwnd);
                if (title == m.Title) break;
                m.Title = title;
                RenderVisuals();
                UpdateTitle();
                break;
            case EVENT_OBJECT_DESTROY:
                _members.Remove(hwnd);
                _tree.Remove(hwnd);
                Refresh(raise: false);
                break;
        }
    }

    public void ReleaseAll()
    {
        foreach (var h in _members.Keys.ToList()) Release(h);
    }

    private void Adopt(nint hwnd, Pane pane, DockSide? side)
    {
        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        GetWindowRect(hwnd, out var original);
        _members[hwnd] = new Member(hwnd, original) { Title = GetWindowText(hwnd) };
        if (side is { } s) _tree.Split(pane, s, hwnd);
        else _tree.AddTab(pane, hwnd);
        _lastActive = hwnd;
        Refresh(raise: true, top: hwnd);
    }

    /// <summary>Takes the window out of the group and puts it back where it was before it joined.</summary>
    private void Release(nint hwnd)
    {
        var m = _members[hwnd];
        _members.Remove(hwnd);
        _tree.Remove(hwnd);
        if (!IsWindow(hwnd)) return;
        var r = m.OriginalBounds;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(hwnd, HWND_TOP, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void Refresh(bool raise, nint? top = null)
    {
        RenderVisuals();
        SyncWindows(raise, top);
        UpdateTitle();
        UpdateMinSize();
    }

    // ---- frame visuals -------------------------------------------------------------------------

    private void RenderVisuals()
    {
        Surface.Children.Clear();
        _layout = LayoutEngine.Arrange(_tree.Root, new CoreRect(0, 0, Surface.ActualWidth, Surface.ActualHeight), Metrics);

        foreach (var p in _layout.Panes)
        {
            Place(BuildTabBar(p.Pane), p.TabBar);
            if (p.Pane.Windows.Count == 0)
            {
                var add = new Button { Content = "ウィンドウを追加…", Padding = new Thickness(12, 4, 12, 4) };
                add.Click += (_, _) => PickAndAdopt(p.Pane, null);
                var host = new Grid { Background = PlaceholderBrush };
                host.Children.Add(new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { add } });
                Place(host, p.Content);
            }
            else
            {
                Place(new Border { Background = PlaceholderBrush }, p.Content);
            }
        }

        foreach (var s in _layout.Splitters)
        {
            var bar = new Border
            {
                Background = SplitterBrush,
                Cursor = s.Split.Orientation == Orientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            };
            bar.MouseEnter += (_, _) => bar.Background = AccentBrush;
            bar.MouseLeave += (_, _) => bar.Background = SplitterBrush;
            bar.MouseLeftButtonDown += (_, e) =>
            {
                _dragging = s;
                Surface.CaptureMouse();
                e.Handled = true;
            };
            Place(bar, s.Bounds);
        }
    }

    private FrameworkElement BuildTabBar(Pane pane)
    {
        var bar = new DockPanel { Background = TabBarBrush, LastChildFill = false };

        var add = new Button { Content = "+", Width = 28, BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = "ウィンドウを追加" };
        // Built on click: tab bars are rebuilt on every layout change, including each step of a drag.
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add };
            menu.Items.Add(MenuItem("タブとして追加…", () => PickAndAdopt(pane, null)));
            menu.Items.Add(MenuItem("右に分割して追加…", () => PickAndAdopt(pane, DockSide.Right)));
            menu.Items.Add(MenuItem("下に分割して追加…", () => PickAndAdopt(pane, DockSide.Bottom)));
            menu.IsOpen = true;
        };
        DockPanel.SetDock(add, Dock.Right);
        bar.Children.Add(add);

        foreach (var h in pane.Windows)
        {
            var active = pane.ActiveWindow == h;
            var close = new Button { Content = "×", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(4, 0, 4, 0), ToolTip = "グループから外す" };
            close.Click += (_, _) => { Release(h); Refresh(raise: true); };
            var tab = new Border
            {
                Background = active ? ActiveTabBrush : TabBrush,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(0, active ? 2 : 0, 0, 0),
                Padding = new Thickness(10, 0, 4, 0),
                Margin = new Thickness(0, 0, 1, 0),
                Child = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = _members[h].Title, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                        close,
                    },
                },
            };
            tab.MouseLeftButtonDown += (_, _) => ActivateTab(h);
            DockPanel.SetDock(tab, Dock.Left);
            bar.Children.Add(tab);
        }
        return bar;
    }

    private void ActivateTab(nint hwnd)
    {
        _tree.Activate(hwnd);
        _lastActive = hwnd;
        RenderVisuals();
        UpdateTitle();
        Raise(hwnd);
    }

    private void PickAndAdopt(Pane pane, DockSide? side)
    {
        var picker = new WindowPicker { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is { } w) Adopt(w.Handle, pane, side);
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is not { } s) return;
        var pos = e.GetPosition(Surface);
        var horizontal = s.Split.Orientation == Orientation.Horizontal;
        var length = horizontal ? s.SplitBounds.Width : s.SplitBounds.Height;
        var offset = (horizontal ? pos.X - s.SplitBounds.X : pos.Y - s.SplitBounds.Y) - Metrics.SplitterThickness / 2;
        s.Split.Ratio = LayoutEngine.ClampRatio(s.Split, offset / (length - Metrics.SplitterThickness), length, Metrics, MinSizeOf);
        Refresh(raise: false);
    }

    /// <summary>
    /// A member's invisible resize border covers the splitters and the frame edges, so dragging a member is how
    /// the user changes the layout: an edge drag moves that divider (or resizes the frame at an outer edge), and
    /// a title-bar drag moves the whole group.
    /// </summary>
    private void ApplyMemberDrag(Member m)
    {
        if (m.LastTarget is not { } t || _layout is null) return;
        var pane = _tree.FindPane(m.Handle)!;
        var content = _layout.Panes.First(p => p.Pane == pane).Content;
        var vb = GetVisibleBounds(m.Handle);
        var tl = Surface.PointFromScreen(new Point(vb.Left, vb.Top));
        var br = Surface.PointFromScreen(new Point(vb.Right, vb.Bottom));

        if (vb.Width == t.Width && vb.Height == t.Height)
        {
            Left += tl.X - content.X;
            Top += tl.Y - content.Y;
            return;
        }
        // An edge counts as dragged only when the opposite edge stayed put.
        if (vb.Left == t.Left && vb.Right != t.Right) MoveEdge(pane, DockSide.Right, br.X, br.X - (content.X + content.Width));
        if (vb.Right == t.Right && vb.Left != t.Left) MoveEdge(pane, DockSide.Left, tl.X - Metrics.SplitterThickness, tl.X - content.X);
        if (vb.Top == t.Top && vb.Bottom != t.Bottom) MoveEdge(pane, DockSide.Bottom, br.Y, br.Y - (content.Y + content.Height));
        if (vb.Bottom == t.Bottom && vb.Top != t.Top) MoveEdge(pane, DockSide.Top, tl.Y - Metrics.TabBarHeight - Metrics.SplitterThickness, tl.Y - content.Y);
    }

    /// <param name="boundary">Where the divider's leading side should go, in Surface coordinates.</param>
    /// <param name="delta">How far the edge moved, used when the edge is the frame's own border.</param>
    private void MoveEdge(Pane pane, DockSide edge, double boundary, double delta)
    {
        if (LayoutTree.FindDivider(pane, edge) is not { } split)
        {
            switch (edge)
            {
                case DockSide.Right: Width += delta; break;
                case DockSide.Bottom: Height += delta; break;
                case DockSide.Left: Left += delta; Width -= delta; break;
                case DockSide.Top: Top += delta; Height -= delta; break;
            }
            return;
        }
        var bounds = _layout!.Splitters.First(s => s.Split == split).SplitBounds;
        var horizontal = split.Orientation == Orientation.Horizontal;
        var length = horizontal ? bounds.Width : bounds.Height;
        var origin = horizontal ? bounds.X : bounds.Y;
        split.Ratio = LayoutEngine.ClampRatio(split, (boundary - origin) / (length - Metrics.SplitterThickness), length, Metrics, MinSizeOf);
    }

    private void Place(FrameworkElement e, CoreRect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
        Surface.Children.Add(e);
    }

    private void UpdateTitle()
    {
        if (!_members.ContainsKey(_lastActive))
            _lastActive = _tree.Panes.Select(p => p.ActiveWindow).OfType<nint>().FirstOrDefault();
        var title = _members.TryGetValue(_lastActive, out var m) ? m.Title : "";
        ActiveTitleText.Text = title;
        // Window switchers such as Waypoint list the frame under this title (Q5).
        Title = title.Length == 0 ? $"[{GroupName}]" : $"[{GroupName}] {title}";
    }

    private void UpdateMinSize()
    {
        var min = LayoutEngine.MinSize(_tree.Root, Metrics, MinSizeOf);
        MinWidth = min.Width + Surface.Margin.Left + Surface.Margin.Right;
        MinHeight = min.Height + 30 + Surface.Margin.Bottom;
    }

    private CoreSize MinSizeOf(nint hwnd) => _members[hwnd].MinSize;

    // ---- external window sync ------------------------------------------------------------------

    private void SyncWindows(bool raise, nint? top = null)
    {
        if (_layout is null || _hwnd == 0 || WindowState == WindowState.Minimized) return;
        foreach (var p in _layout.Panes)
        {
            var target = ToScreen(p.Content);
            foreach (var h in p.Pane.Windows)
            {
                // The window being dragged belongs to the user until the drag ends; moving it would fight the drag.
                if (h != _dragged) Move(_members[h], target);
            }
        }
        if (raise) Raise(top);
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>Moves the window so its visible bounds (not its invisible resize border) match <paramref name="target"/>.</summary>
    private static void Move(Member m, RECT target)
    {
        GetWindowRect(m.Handle, out var wr);
        var vb = GetVisibleBounds(m.Handle);
        int l = vb.Left - wr.Left, t = vb.Top - wr.Top, r = wr.Right - vb.Right, b = wr.Bottom - vb.Bottom;
        m.LastTarget = target;
        SetWindowPos(m.Handle, 0, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// Z-order: [top, other active tabs..., frame, inactive tabs...]. Inactive tabs stay visible (so window
    /// switchers still list them, Q11) but are hidden behind the frame.
    /// </summary>
    private void Raise(nint? top)
    {
        if (_hwnd == 0 || WindowState == WindowState.Minimized) return;
        var actives = _tree.Panes.Select(p => p.ActiveWindow).OfType<nint>().ToList();
        if (top is { } t && actives.Remove(t)) actives.Insert(0, t);
        var order = actives.Append(_hwnd).Concat(_tree.Windows.Except(actives)).ToList();

        _raising = true;
        SetWindowPos(order[0], HWND_TOP, 0, 0, 0, 0, ZOrderFlags);
        for (var i = 1; i < order.Count; i++) SetWindowPos(order[i], order[i - 1], 0, 0, 0, 0, ZOrderFlags);
        _raising = false;
    }

    private const uint ZOrderFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    /// <summary>
    /// When Windows moves the frame up in z-order (e.g. the user clicks its tab bar), put the active tabs there
    /// first and slot the frame in just below them. Raising them afterwards would let the frame cover them for
    /// a frame or two, which shows up as blank panes flashing.
    /// </summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_WINDOWPOSCHANGING || _raising) return 0;
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOZORDER) != 0) return 0;
        var actives = _tree.Panes.Select(p => p.ActiveWindow).OfType<nint>().ToList();
        if (actives.Count == 0) return 0;

        _raising = true;
        var after = pos.hwndInsertAfter;
        foreach (var a in actives)
        {
            SetWindowPos(a, after, 0, 0, 0, 0, ZOrderFlags);
            after = a;
        }
        _raising = false;
        pos.hwndInsertAfter = after;
        Marshal.StructureToPtr(pos, lParam, false);
        return 0;
    }

    /// <summary>
    /// Runs once moves have settled. A window larger than requested has a minimum size (Q9): remember it so
    /// splitters stop there. A window elsewhere was moved by its app: put it back once.
    /// </summary>
    private void CheckSettled()
    {
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;
        bool moved = false, learned = false;
        foreach (var m in _members.Values)
        {
            if (m.LastTarget is not { } t || IsIconic(m.Handle) || !IsWindowVisible(m.Handle)) continue;
            var vb = GetVisibleBounds(m.Handle);
            if (vb.Width > t.Width + 1 || vb.Height > t.Height + 1)
            {
                var min = new CoreSize(
                    vb.Width > t.Width + 1 ? Math.Max(m.MinSize.Width, vb.Width / scale) : m.MinSize.Width,
                    vb.Height > t.Height + 1 ? Math.Max(m.MinSize.Height, vb.Height / scale) : m.MinSize.Height);
                // Only a larger minimum is news; an unmet known minimum (frame cannot grow further) must not loop.
                learned |= min != m.MinSize;
                m.MinSize = min;
            }
            else if (vb.Left != t.Left || vb.Top != t.Top)
            {
                moved = true;
            }
        }
        if (learned)
        {
            // Make room for the new minimum: grow the frame if needed, then move dividers off the small panes.
            UpdateMinSize();
            if (Width < MinWidth) Width = MinWidth;
            if (Height < MinHeight) Height = MinHeight;
            UpdateLayout();
            LayoutEngine.ClampAll(_tree.Root, new CoreRect(0, 0, Surface.ActualWidth, Surface.ActualHeight), Metrics, MinSizeOf);
            Refresh(raise: false);
        }
        else if (moved)
        {
            SyncWindows(raise: false);
        }
    }

    private RECT ToScreen(CoreRect r)
    {
        var tl = Surface.PointToScreen(new Point(r.X, r.Y));
        var br = Surface.PointToScreen(new Point(r.X + r.Width, r.Y + r.Height));
        return new RECT { Left = (int)Math.Round(tl.X), Top = (int)Math.Round(tl.Y), Right = (int)Math.Round(br.X), Bottom = (int)Math.Round(br.Y) };
    }

    // ---- frame state ---------------------------------------------------------------------------

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // With WindowChrome a maximized window overhangs the screen by its resize border.
        Root.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        if (WindowState == WindowState.Minimized)
        {
            foreach (var h in _members.Keys) ShowWindow(h, SW_HIDE);
        }
        else
        {
            foreach (var h in _members.Keys) ShowWindow(h, SW_SHOWNOACTIVATE);
            Refresh(raise: true);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
