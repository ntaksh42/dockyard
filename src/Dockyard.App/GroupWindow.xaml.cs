using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dockyard.App.Controllers;
using Dockyard.Domain;
using Dockyard.Infrastructure;
using static Dockyard.Infrastructure.Native;
using DomainRect = Dockyard.Domain.Rect;
using DomainSize = Dockyard.Domain.Size;
using Orientation = Dockyard.Domain.Orientation;

namespace Dockyard.App;

/// <summary>
/// The group frame: draws tab bars, splitters and empty panes, and passes user input and window-state changes
/// to its <see cref="GroupController"/>, which decides where the member windows go.
/// </summary>
public partial class GroupWindow : Window, IGroupView
{
    private const double CaptionHeight = 30;
    private static readonly Brush TabBarBrush = Brush("#D1D5DB"), TabBrush = Brush("#E6E8EC"), ActiveTabBrush = Brushes.White,
        AccentBrush = Brush("#3B82F6"), PlaceholderBrush = Brush("#1F2328"), SplitterBrush = Brush("#2B2F36"), MutedTextBrush = Brush("#9CA3AF");

    private readonly Func<nint, bool> _isGrouped;
    private readonly IWindowSystem _windows;
    /// <summary>Live thumbnails of the active tabs, by window, drawn while the frame moves or resizes (see BeginLive).</summary>
    private readonly Dictionary<nint, nint> _thumbnails = [];
    /// <summary>Each pane's "+" button as last built, where the keyboard opens that pane's menu.</summary>
    private readonly Dictionary<Pane, Button> _addButtons = [];
    /// <summary>The tab each pane showed when its bar was last built, to tell UI Automation when that changes.</summary>
    private readonly Dictionary<Pane, nint> _shownTabs = [];
    /// <summary>Where each thumbnail was last put: moving the frame leaves it unchanged, and DWM need not hear it again.</summary>
    private readonly Dictionary<nint, (RECT Destination, RECT Source)> _thumbnailPlaces = [];
    /// <summary>Ends the live view once the frame has been still for a moment, so the apps have repainted at their final size.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _liveEnd = new() { Interval = TimeSpan.FromMilliseconds(200) };
    /// <summary>Between WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE: the user is dragging the frame or one of its borders.</summary>
    private bool _sizing;
    private SplitterLayout? _dragging;
    private nint _hwnd;
    private bool _closing;
    private nint _tabWindow;
    private Point _tabStart;
    private bool _tabDragging;
    internal event Action<GroupWindow, nint>? TabDragStarted;
    internal event Action<bool>? TabDragEnded;
    internal event Action? SettingsRequested;
    internal event Action<GroupWindow>? SaveLayoutRequested;
    internal event Action<GroupWindow, SavedGroup>? OpenLayoutRequested;
    /// <summary>The saved layouts, read when a menu offering them opens.</summary>
    internal Func<IReadOnlyList<SavedGroup>> SavedLayouts { get; init; } = () => [];
    /// <summary>The current shortcut bindings, listed in the "+" menu.</summary>
    internal Func<IReadOnlyDictionary<ShortcutAction, ShortcutKey>> ShortcutBindings { get; init; } = () => Shortcuts.Defaults;
    /// <summary>The open window picker. Tabs must stay below it, so raising starts there instead of at the top.</summary>
    private WindowPicker? _picker;
    /// <summary>Draws the tab bars above the member windows with tab bar placement B; null otherwise.</summary>
    private Window? _overlay;
    private Canvas? _overlaySurface;
    /// <summary>The elements drawn for the current layout and where each goes, so a resize can move them (see Render).</summary>
    private readonly List<(FrameworkElement Element, Func<LayoutResult, DomainRect> Bounds)> _visuals = [];
    private string? _visualKey;
    private List<Pane> _visualPanes = [];
    private List<SplitNode> _visualSplits = [];
    private LayoutResult? _rendered;

    /// <param name="isGrouped">Whether a window already belongs to some group, so the picker can leave it out.</param>
    internal GroupWindow(string name, IWindowSystem windows, Func<nint, bool> isGrouped, TabBarPlacement placement)
    {
        InitializeComponent();
        _isGrouped = isGrouped;
        GroupName = name;
        _windows = windows;
        Controller = new GroupController(this, windows, new WpfScheduler(Dispatcher));
        _liveEnd.Tick += (_, _) => EndLive();
        SetTabBarPlacement(placement);

        LocationChanged += (_, _) =>
        {
            PositionOverlay();
            Controller.OnFrameMoved();
        };
        Surface.SizeChanged += (_, _) => Controller.OnSurfaceResized();
        // The maximized window rect is only final after the state change; re-measure the overhang on resize.
        SizeChanged += (_, _) =>
        {
            UpdateMaximizedMargin();
            // Snapping or maximizing goes on after the user lets go: stay live until the size stops changing.
            if (Controller.IsLive && !_sizing) EndLiveSoon();
        };
        Activated += (_, _) => Controller.OnFrameActivated();
        StateChanged += (_, _) =>
        {
            UpdateMaximizedMargin();
            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "" : "";
            MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, (string)MaximizeButton.ToolTip);
            if (WindowState == WindowState.Minimized)
            {
                _overlay?.Hide();
                Controller.OnFrameMinimized();
            }
            else
            {
                PositionOverlay();
                Controller.OnFrameRestored();
            }
        };
        Closing += (_, _) =>
        {
            EndLive();
            _closing = true;
            Controller.OnClosing();
            _overlay?.Close();
        };
        Surface.MouseMove += (_, e) =>
        {
            if (_tabWindow != 0 && !_tabDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var point = e.GetPosition(Surface);
                if (Math.Abs(point.X - _tabStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(point.Y - _tabStart.Y) >= SystemParameters.MinimumVerticalDragDistance)
                {
                    _tabDragging = true;
                    TabDragStarted?.Invoke(this, _tabWindow);
                }
            }
            if (_dragging is not { } s) return;
            var pos = e.GetPosition(Surface);
            Controller.DragSplitter(s, s.Split.Orientation == Orientation.Horizontal ? pos.X : pos.Y);
        };
        Surface.MouseLeftButtonUp += (_, _) =>
        {
            var window = _tabWindow;
            var dragged = _tabDragging;
            _tabWindow = 0;
            _tabDragging = false;
            if (dragged) TabDragEnded?.Invoke(false);
            else if (window != 0) Controller.ActivateTab(window);
            _dragging = null;
            Surface.ReleaseMouseCapture();
        };
        Surface.LostMouseCapture += (_, _) =>
        {
            if (_tabDragging) TabDragEnded?.Invoke(true);
            _tabWindow = 0;
            _tabDragging = false;
            _dragging = null;
        };
    }

    /// <summary>
    /// Hooks the window procedure after the base has let WindowChrome add its own hook. The hook added last runs
    /// first, so ours can take right-clicks on the caption, which WindowChrome would answer with the system menu.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
    }

    public string GroupName
    {
        get => GroupNameText.Text;
        set
        {
            GroupNameText.Text = value;
            UpdateTitle();
        }
    }

    internal GroupController Controller { get; }

    // ---- IGroupView ---------------------------------------------------------------------------

    public nint Handle => _hwnd;

    public bool IsOnScreen =>
        !_closing && _hwnd != 0 && WindowState != WindowState.Minimized && PresentationSource.FromVisual(Surface) is not null;

    public bool IsMinimized => WindowState == WindowState.Minimized;

    public DomainSize SurfaceSize => new(Surface.ActualWidth, Surface.ActualHeight);

    public double Scale => PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;

    public nint ZAnchor => _picker is { } p ? new WindowInteropHelper(p).Handle : 0;
    public nint TabOverlay => _overlay is { IsVisible: true } o ? new WindowInteropHelper(o).Handle : 0;
    internal bool CanDock => IsOnScreen && _picker is null;

    public PixelRect ToScreen(DomainRect r)
    {
        var tl = Surface.PointToScreen(new Point(r.X, r.Y));
        var br = Surface.PointToScreen(new Point(r.X + r.Width, r.Y + r.Height));
        return new PixelRect((int)Math.Round(tl.X), (int)Math.Round(tl.Y), (int)Math.Round(br.X), (int)Math.Round(br.Y));
    }

    public DomainRect FromScreen(PixelRect r)
    {
        var tl = Surface.PointFromScreen(new Point(r.Left, r.Top));
        var br = Surface.PointFromScreen(new Point(r.Right, r.Bottom));
        return new DomainRect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    public void Render(GroupSnapshot snapshot)
    {
        PositionOverlay();
        ActiveTitleText.Text = snapshot.ActiveTitle;
        UpdateTitle();
        // Dragging a splitter or resizing the frame changes only sizes: move what is there instead of rebuilding
        // every tab bar and button on each step.
        var key = VisualKey(snapshot);
        if (key == _visualKey && _visualPanes.SequenceEqual(snapshot.Layout.Panes.Select(p => p.Pane)) &&
            _visualSplits.SequenceEqual(snapshot.Layout.Splitters.Select(s => s.Split)))
        {
            _rendered = snapshot.Layout;
            foreach (var (element, bounds) in _visuals) Arrange(element, bounds(snapshot.Layout));
            return;
        }
        _visualKey = key;
        _visualPanes = snapshot.Layout.Panes.Select(p => p.Pane).ToList();
        _visualSplits = snapshot.Layout.Splitters.Select(s => s.Split).ToList();
        _rendered = snapshot.Layout;
        _visuals.Clear();
        _addButtons.Clear();
        Surface.Children.Clear();
        _overlaySurface?.Children.Clear();
        for (var i = 0; i < snapshot.Layout.Panes.Count; i++)
        {
            var index = i;
            var p = snapshot.Layout.Panes[i];
            // Over a member window the tabs must be in the overlay; the frame is below the members.
            var overMember = _overlaySurface is not null && p.Pane.Windows.Count > 0 && p.Placement == TabBarPlacement.TitleBar;
            Place(BuildTabBar(p.Pane, snapshot.Titles, p.TabBar, overMember), l => l.Panes[index].TabBar, overMember ? _overlaySurface : null);
            if (p.Pane.Windows.Count == 0)
            {
                var add = new Button { Content = "Add Window…", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Center };
                add.Click += (_, _) => PickAndAdopt(p.Pane, null);
                var host = new Grid { Background = PlaceholderBrush };
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                // A restored tab whose app is not open: say what will fill the pane once it is.
                if (snapshot.Waiting.TryGetValue(p.Pane, out var waiting))
                    foreach (var tab in waiting)
                        content.Children.Add(new TextBlock { Text = $"Waiting for: {tab.ProcessName} — {tab.Title}", Foreground = MutedTextBrush,
                            FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center });
                content.Children.Add(add);
                content.Children.Add(new TextBlock { Text = "or hold Shift and drag a window's title bar here", Foreground = MutedTextBrush,
                    FontSize = 12, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
                host.Children.Add(content);
                Place(host, l => l.Panes[index].Content);
            }
        }

        for (var i = 0; i < snapshot.Layout.Splitters.Count; i++)
        {
            var index = i;
            var s = snapshot.Layout.Splitters[i];
            var bar = new Border
            {
                Background = SplitterBrush,
                Cursor = s.Split.Orientation == Orientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            };
            bar.MouseEnter += (_, _) => bar.Background = AccentBrush;
            bar.MouseLeave += (_, _) => bar.Background = SplitterBrush;
            bar.MouseLeftButtonDown += (_, e) =>
            {
                // The splitter as last laid out: the element outlives the layout it was made for.
                if (Surface.CaptureMouse()) _dragging = _rendered!.Splitters[index];
                e.Handled = true;
            };
            Place(bar, l => l.Splitters[index].Bounds);
        }
    }

    /// <summary>Everything the visuals show apart from sizes: equal keys mean the same elements can be reused.</summary>
    private string VisualKey(GroupSnapshot snapshot)
    {
        var vertical = Controller.Metrics.TabBarPlacement == TabBarPlacement.Left;
        return string.Join("|", snapshot.Layout.Panes.Select(p =>
                $"{p.Placement}/{p.Pane.ActiveIndex}/{(vertical ? p.TabBar.Width : 0)}/{TabSize(p.TabBar, p.Pane.Windows.Count, vertical)}/" +
                string.Join(",", p.Pane.Windows.Select(h => $"{h}={snapshot.Titles[h]}")) + "/" +
                (snapshot.Waiting.TryGetValue(p.Pane, out var waiting) ? string.Join(",", waiting.Select(t => $"{t.ProcessName}={t.Title}")) : ""))) +
            $"#{snapshot.Layout.Splitters.Count}#{_overlaySurface is not null}";
    }

    private const double TabTitleWidth = 200, TabChrome = 36, TabHeight = 28, TabBarButton = 28;

    /// <summary>
    /// How much room each tab gets along the bar: the title's width in a horizontal bar, the tab's height in a
    /// vertical one. Tabs shrink evenly once they no longer all fit, so none is pushed out of sight. Rounded, so
    /// that resizing a frame whose tabs fit anyway rebuilds nothing.
    /// </summary>
    private static double TabSize(DomainRect bar, int tabs, bool vertical)
    {
        if (tabs == 0) return 0;
        var room = (vertical ? bar.Height : bar.Width) - 2 * TabBarButton;
        return vertical
            ? Math.Clamp(Math.Floor(room / tabs) - 1, 18, TabHeight)
            : Math.Clamp(Math.Floor((room / tabs - TabChrome) / 8) * 8, 24, TabTitleWidth);
    }

    /// <summary>Window switchers such as Waypoint list the frame under this title (Q5).</summary>
    private void UpdateTitle() =>
        Title = ActiveTitleText.Text.Length == 0 ? $"[{GroupName}]" : $"[{GroupName}] {ActiveTitleText.Text}";

    public void MoveBy(double dx, double dy)
    {
        Left += dx;
        Top += dy;
    }

    public void ResizeEdge(DockSide edge, double delta)
    {
        switch (edge)
        {
            case DockSide.Right: Width += delta; break;
            case DockSide.Bottom: Height += delta; break;
            case DockSide.Left: Left += delta; Width -= delta; break;
            case DockSide.Top: Top += delta; Height -= delta; break;
        }
    }

    public void SetMinimumContentSize(DomainSize min)
    {
        MinWidth = min.Width + Surface.Margin.Left + Surface.Margin.Right;
        MinHeight = min.Height + CaptionHeight + Surface.Margin.Bottom;
        if (WindowState != WindowState.Normal || (Width >= MinWidth && Height >= MinHeight)) return;
        // Grow now rather than at the next layout pass, so the caller lays out against the new size.
        Width = Math.Max(Width, MinWidth);
        Height = Math.Max(Height, MinHeight);
        UpdateLayout();
    }

    public void Minimize() => WindowState = WindowState.Minimized;

    public void Restore() => WindowState = WindowState.Normal;

    internal void SetTabBarPlacement(TabBarPlacement placement)
    {
        if (placement == TabBarPlacement.TitleBar && _overlay is null) CreateOverlay();
        else if (placement != TabBarPlacement.TitleBar && _overlay is not null)
        {
            _overlay.Close();
            _overlay = null;
            _overlaySurface = null;
        }
        Controller.SetTabBarPlacement(placement);
    }

    // ---- drawing helpers ----------------------------------------------------------------------

    /// <param name="overMember">
    /// Drawn over a member's title bar (placement B): only the tabs themselves, so the app's own caption buttons
    /// stay visible and clickable through the transparent rest of the overlay.
    /// </param>
    private FrameworkElement BuildTabBar(Pane pane, IReadOnlyDictionary<nint, string> titles, DomainRect bounds, bool overMember)
    {
        var vertical = Controller.Metrics.TabBarPlacement == TabBarPlacement.Left;
        // Over a member the bar ends after its buttons, leaving the app's own caption buttons uncovered.
        var bar = new DockPanel { Background = overMember ? null : TabBarBrush, LastChildFill = !overMember };
        // Tabs shrink first (TabSize); past their smallest the strip scrolls, with the wheel, to the active tab.
        var strip = new TabStrip { Orientation = vertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal };
        var scroller = new ScrollViewer
        {
            Content = strip,
            Focusable = false,
            HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled,
        };
        // Over a member the buttons follow the tabs: keep them room as the bar is resized (it is moved, not rebuilt).
        if (overMember) bar.SizeChanged += (_, e) => scroller.MaxWidth = Math.Max(0, e.NewSize.Width - 2 * TabBarButton);
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (vertical) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 3.0);
            else scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        };
        DockPanel.SetDock(scroller, vertical ? Dock.Top : Dock.Left);

        var tabSize = TabSize(bounds, pane.Windows.Count, vertical);
        var add = new Button { Content = "+", FontSize = 16, Width = vertical ? double.NaN : 28, Height = vertical ? 28 : double.NaN, BorderThickness = new Thickness(0),
            Background = overMember ? TabBarBrush : Brushes.Transparent, ToolTip = "Add Window" };
        System.Windows.Automation.AutomationProperties.SetName(add, "Add Window or Layout");
        add.Click += (_, _) => OpenPaneMenu(pane, add);
        _addButtons[pane] = add;
        var settings = new Button { Content = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
            Width = vertical ? double.NaN : 28, Height = vertical ? 28 : double.NaN, BorderThickness = new Thickness(0),
            Background = overMember ? TabBarBrush : Brushes.Transparent, ToolTip = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        System.Windows.Automation.AutomationProperties.SetName(settings, "Settings");
        // Added before the tabs so they keep their room; over a member they follow the tabs instead.
        DockPanel.SetDock(add, vertical ? Dock.Bottom : overMember ? Dock.Left : Dock.Right);
        DockPanel.SetDock(settings, DockPanel.GetDock(add));
        if (!overMember) { bar.Children.Add(settings); bar.Children.Add(add); }

        foreach (var h in pane.Windows)
        {
            var active = pane.ActiveWindow == h;
            var close = new Button { Content = "×", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(4, 0, 4, 0), ToolTip = "Remove from Group" };
            close.Click += (_, _) => Controller.Remove(h);
            // Selecting it from UI Automation does what a click does.
            var announce = active && _shownTabs.TryGetValue(pane, out var shown) && shown != h;
            if (active) _shownTabs[pane] = h;
            var tab = new TabElement(titles[h], active, announce, () => Controller.ActivateTab(h))
            {
                Background = active ? ActiveTabBrush : TabBrush,
                BorderBrush = AccentBrush,
                BorderThickness = vertical ? new Thickness(active ? 2 : 0, 0, 0, 0) : new Thickness(0, active ? 2 : 0, 0, 0),
                Padding = new Thickness(10, 0, 4, 0),
                Margin = vertical ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0),
                Height = vertical ? tabSize : double.NaN,
                // Titles are cut short in the tab: the whole one on hover.
                ToolTip = titles[h],
                Child = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = titles[h], MaxWidth = vertical ? Math.Max(0, bounds.Width - 40) : tabSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                        close,
                    },
                },
            };
            tab.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source)
                    for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
                        if (current is Button) return;
                _tabStart = e.GetPosition(Surface);
                if (Surface.CaptureMouse()) _tabWindow = h;
                e.Handled = true;
            };
            strip.Children.Add(tab);
            if (active) tab.Loaded += (_, _) => tab.BringIntoView();
        }
        bar.Children.Add(scroller);
        if (overMember) { bar.Children.Add(add); bar.Children.Add(settings); }
        return bar;
    }

    private void Place(FrameworkElement e, Func<LayoutResult, DomainRect> bounds, Canvas? canvas = null)
    {
        Arrange(e, bounds(_rendered!));
        (canvas ?? Surface).Children.Add(e);
        _visuals.Add((e, bounds));
    }

    private static void Arrange(FrameworkElement e, DomainRect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
    }

    /// <summary>
    /// A transparent window over the surface. Its empty areas let clicks through to the member windows; it never
    /// takes the foreground, so clicking a tab leaves the focus with the apps.
    /// </summary>
    private void CreateOverlay()
    {
        _overlaySurface = new Canvas { ClipToBounds = true };
        _overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = _overlaySurface,
            Title = "Dockyard Tabs",
        };
        _overlay.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(_overlay).Handle;
            SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)((long)GetWindowLongPtr(handle, GWL_EXSTYLE) | 0x08000000 /* WS_EX_NOACTIVATE */ | WS_EX_TOOLWINDOW));
        };
        // Moving to a monitor with another DPI makes WPF resize the overlay: put it back over the surface.
        _overlay.DpiChanged += (_, _) => PositionOverlay();
    }

    /// <summary>Lays the overlay over the surface, showing it once the frame is on screen.</summary>
    private void PositionOverlay()
    {
        if (_overlay is null || !IsOnScreen) return;
        if (!_overlay.IsVisible) _overlay.Show();
        var r = ToScreen(new DomainRect(0, 0, Surface.ActualWidth, Surface.ActualHeight));
        SetWindowPos(new WindowInteropHelper(_overlay).Handle, 0, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        // The overlay takes the DPI of the monitor it is on, which can differ from the frame's when the group
        // spans monitors. The tabs are laid out in the frame's units: scale them onto the same pixels.
        var factor = Scale / VisualTreeHelper.GetDpi(_overlay).DpiScaleX;
        _overlaySurface!.LayoutTransform = Math.Abs(factor - 1) < 0.001 ? Transform.Identity : new ScaleTransform(factor, factor);
    }


    /// <summary>The "+" menu of the pane. Built when opened: tab bars are rebuilt on every layout change, including each step of a drag.</summary>
    private void OpenPaneMenu(Pane pane, Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(MenuItem("Add as Tab…", () => PickAndAdopt(pane, null)));
        menu.Items.Add(MenuItem("Split Right…", () => PickAndAdopt(pane, DockSide.Right)));
        menu.Items.Add(MenuItem("Split Down…", () => PickAndAdopt(pane, DockSide.Bottom)));
        menu.Items.Add(new Separator());
        menu.Items.Add(OpenLayoutMenu());
        menu.Items.Add(ShortcutsMenu());
        menu.Items.Add(MenuItem("Settings…", () => SettingsRequested?.Invoke()));
        menu.IsOpen = true;
    }

    /// <summary>Runs a shortcut that acts on the frame (see <see cref="Shortcuts.IsFrameAction"/>).</summary>
    internal void RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.SaveLayout:
                SaveLayoutRequested?.Invoke(this);
                break;
            case ShortcutAction.RenameGroup:
                BeginRename();
                break;
            case ShortcutAction.ChangeTabBar:
                SetTabBarPlacement(Controller.Metrics.TabBarPlacement switch
                {
                    TabBarPlacement.Top => TabBarPlacement.TitleBar,
                    TabBarPlacement.TitleBar => TabBarPlacement.Left,
                    _ => TabBarPlacement.Top,
                });
                break;
            case ShortcutAction.MaximizeGroup:
                Maximize_Click(this, new RoutedEventArgs());
                break;
            case ShortcutAction.MinimizeGroup:
                WindowState = WindowState.Minimized;
                break;
            case ShortcutAction.CloseGroup:
                Close();
                break;
            case ShortcutAction.OpenMenu:
                // The pane of the window in use; the menu takes the keyboard, so the frame has to come forward.
                var pane = _rendered?.Panes.FirstOrDefault(p => Controller.LastActive is { } w && p.Pane.Windows.Contains(w))?.Pane
                    ?? _rendered?.Panes.FirstOrDefault()?.Pane;
                if (pane is null || !_addButtons.TryGetValue(pane, out var add)) break;
                Activate();
                OpenPaneMenu(pane, add);
                break;
        }
    }

    private void PickAndAdopt(Pane pane, DockSide? side)
    {
        var picker = new WindowPicker(_isGrouped) { Owner = this };
        _picker = picker;
        bool picked;
        try
        {
            picked = picker.ShowDialog() == true;
        }
        finally
        {
            // Cleared before adopting: the dialog's window is gone now and cannot anchor the z-order.
            _picker = null;
        }
        if (!(picked && picker.Selected is { } w && !_isGrouped(w.Handle) && Controller.Adopt(w.Handle, pane, side)))
            Controller.PickerCancelled();
    }

    // ---- frame window -------------------------------------------------------------------------

    /// <summary>Hands z-order changes the system is about to make to the frame to the controller (see OnFrameZChanging).</summary>
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0231: // WM_ENTERSIZEMOVE
                _sizing = true;
                BeginLive();
                break;
            case 0x0232: // WM_EXITSIZEMOVE
                _sizing = false;
                EndLiveSoon();
                break;
            case 0x0112 when (wParam & 0xFFF0) is 0xF030 or 0xF120: // WM_SYSCOMMAND SC_MAXIMIZE, SC_RESTORE (e.g. a caption double-click)
                BeginLive();
                EndLiveSoon();
                break;
        }
        if (msg == 0x00A5 /* WM_NCRBUTTONUP */ && wParam == 2 /* HTCAPTION */)
        {
            ShowCaptionMenu();
            handled = true;
            return 0;
        }
        if (msg != WM_WINDOWPOSCHANGING) return 0;
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOZORDER) != 0) return 0;
        var change = new FrameZChange(
            pos.hwndInsertAfter switch
            {
                0 => ZPlacement.Top,
                1 => ZPlacement.Bottom,
                -1 => ZPlacement.Topmost,
                -2 => ZPlacement.NoTopmost,
                _ => ZPlacement.Below,
            },
            pos.hwndInsertAfter,
            (pos.flags & SWP_HIDEWINDOW) != 0);
        if (Controller.OnFrameZChanging(change) is not { } after) return 0;
        pos.hwndInsertAfter = after;
        Marshal.StructureToPtr(pos, lParam, false);
        return 0;
    }

    /// <summary>
    /// With WindowChrome a maximized window overhangs the monitor's work area. Pad the content by exactly that
    /// overhang; SystemParameters.WindowResizeBorderThickness misses the padded border and is off by a few pixels.
    /// </summary>
    private void UpdateMaximizedMargin()
    {
        if (WindowState != WindowState.Maximized || _hwnd == 0)
        {
            Root.Margin = new Thickness(0);
            return;
        }
        GetWindowRect(_hwnd, out var w);
        var work = GetWorkArea(_hwnd);
        var scale = Scale;
        Root.Margin = new Thickness(
            Math.Max(0, work.Left - w.Left) / scale, Math.Max(0, work.Top - w.Top) / scale,
            Math.Max(0, w.Right - work.Right) / scale, Math.Max(0, w.Bottom - work.Bottom) / scale);
    }

    /// <summary>The caption's right-click menu, so renaming and saving can be found without knowing the double-click.</summary>
    private void ShowCaptionMenu()
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        // After the menu has closed: it takes the keyboard focus back as it goes, which would end the rename.
        menu.Items.Add(MenuItem("Rename Group", () => Dispatcher.BeginInvoke(BeginRename, System.Windows.Threading.DispatcherPriority.Input)));
        menu.Items.Add(MenuItem("Save Layout", () => SaveLayoutRequested?.Invoke(this)));
        menu.Items.Add(OpenLayoutMenu());
        var tabBar = new MenuItem { Header = "Tab Bar" };
        foreach (var (placement, label) in new[] { (TabBarPlacement.Top, "Above the Window"), (TabBarPlacement.TitleBar, "Over the Title Bar"), (TabBarPlacement.Left, "Vertical, Left") })
        {
            var item = MenuItem(label, () => SetTabBarPlacement(placement));
            item.IsChecked = Controller.Metrics.TabBarPlacement == placement;
            tabBar.Items.Add(item);
        }
        menu.Items.Add(tabBar);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Settings…", () => SettingsRequested?.Invoke()));
        menu.IsOpen = true;
    }

    private void GroupChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || GroupNameEdit.IsVisible) return;
        BeginRename();
        e.Handled = true;
    }

    private void BeginRename()
    {
        if (_closing || GroupNameEdit.IsVisible) return;
        Activate();
        GroupNameEdit.Text = GroupName;
        GroupNameText.Visibility = Visibility.Hidden;
        GroupNameEdit.Visibility = Visibility.Visible;
        GroupNameEdit.Focus();
        GroupNameEdit.SelectAll();
    }

    private void GroupNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) EndRename(e.Key == Key.Enter);
    }

    private void GroupNameEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndRename(true);

    private void EndRename(bool commit)
    {
        if (!GroupNameEdit.IsVisible) return;
        var name = GroupNameEdit.Text.Trim();
        if (commit && name.Length > 0) GroupName = name;
        GroupNameEdit.Visibility = Visibility.Collapsed;
        GroupNameText.Visibility = Visibility.Visible;
    }

    internal SavedGroup Save() => new(GroupName, FrameBounds, Controller.SaveLayout(), Controller.Metrics.TabBarPlacement);

    /// <summary>Takes a saved group's name, tab bar placement and layout (see <see cref="GroupController.RestoreLayout"/>).</summary>
    internal int Restore(SavedGroup saved, Func<SavedTab, nint> resolve)
    {
        GroupName = saved.Name;
        if (saved.TabBarPlacement is { } placement) SetTabBarPlacement(placement);
        return Controller.RestoreLayout(saved.Layout, resolve);
    }

    /// <summary>The frame's window rectangle in screen pixels, for saving the group.</summary>
    internal PixelRect FrameBounds
    {
        get
        {
            GetWindowRect(_hwnd, out var r);
            return new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    /// <summary>Shows the frame, at <paramref name="bounds"/> (screen pixels) when given.</summary>
    internal void ShowAt(PixelRect? bounds)
    {
        Show();
        if (bounds is { } b)
        {
            SetWindowPos(_hwnd, 0, b.Left, b.Top, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            // Saved on a monitor that is gone or has moved: without enough of its caption on screen the frame
            // cannot be dragged back, so bring it onto the nearest monitor.
            var work = GetWorkArea(_hwnd);
            var captionShown = Math.Min(b.Right, work.Right) - Math.Max(b.Left, work.Left);
            if (b.Top < work.Top || b.Top + CaptionHeight > work.Bottom || captionShown < Math.Min(120, b.Width))
            {
                int width = Math.Min(b.Width, work.Width), height = Math.Min(b.Height, work.Height);
                SetWindowPos(_hwnd, 0, Math.Clamp(b.Left, work.Left, work.Right - width), Math.Clamp(b.Top, work.Top, work.Bottom - height),
                    width, height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
        // Lay out now, so a layout restored right after this is placed against the final size.
        UpdateLayout();
    }

    private void SaveLayout_Click(object sender, RoutedEventArgs e) => SaveLayoutRequested?.Invoke(this);

    private void Minimize_Click(
object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        BeginLive();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        EndLiveSoon();
    }

    // ---- live view while the frame moves -------------------------------------------------------

    /// <summary>
    /// Shows the active tabs as DWM thumbnails in the frame and lets the controller keep the real windows behind
    /// it. DWM composes a thumbnail with its destination window, so it moves and resizes with the frame on the
    /// very same screen refresh, which the real windows cannot do (see GroupController.BeginLive).
    /// </summary>
    private void BeginLive()
    {
        if (_closing || _picker is not null || !IsOnScreen || Controller.IsLive) return;
        // Thumbnails first, so the panes are never blank while the windows go behind the frame.
        UpdateThumbnails(null, EventArgs.Empty);
        Controller.BeginLive();
        CompositionTarget.Rendering += UpdateThumbnails;
    }

    private void EndLiveSoon()
    {
        if (!Controller.IsLive) return;
        _liveEnd.Stop();
        _liveEnd.Start();
    }

    private void EndLive()
    {
        _liveEnd.Stop();
        CompositionTarget.Rendering -= UpdateThumbnails;
        // The windows come forward before the thumbnails go, so nothing blinks in between.
        Controller.EndLive();
        foreach (var thumbnail in _thumbnails.Values) DwmUnregisterThumbnail(thumbnail);
        _thumbnails.Clear();
        _thumbnailPlaces.Clear();
    }

    /// <summary>
    /// Keeps a thumbnail of each active tab over its pane, at the window's own size: the real window catches up
    /// with a resize a moment later, and scaling its picture meanwhile would make the text swim.
    /// </summary>
    private void UpdateThumbnails(object? sender, EventArgs e)
    {
        if (_rendered is null || _hwnd == 0) return;
        var client = PointToScreen(new Point(0, 0));
        var shown = new HashSet<nint>();
        foreach (var p in _rendered.Panes)
        {
            if (p.Pane.ActiveWindow is not { } window || !_windows.Exists(window)) continue;
            if (!_thumbnails.TryGetValue(window, out var thumbnail))
            {
                if (DwmRegisterThumbnail(_hwnd, window, out thumbnail) != 0) continue;
                _thumbnails[window] = thumbnail;
            }
            shown.Add(window);
            var pane = ToScreen(p.Content);
            var full = _windows.GetBounds(window);
            var visible = _windows.GetVisibleBounds(window);
            int width = Math.Min(pane.Width, visible.Width), height = Math.Min(pane.Height, visible.Height);
            int left = pane.Left - (int)Math.Round(client.X), top = pane.Top - (int)Math.Round(client.Y);
            int sourceLeft = visible.Left - full.Left, sourceTop = visible.Top - full.Top;
            (RECT Destination, RECT Source) place = (new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height },
                new RECT { Left = sourceLeft, Top = sourceTop, Right = sourceLeft + width, Bottom = sourceTop + height });
            if (_thumbnailPlaces.TryGetValue(window, out var last) && last.Equals(place)) continue;
            _thumbnailPlaces[window] = place;
            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = place.Destination,
                rcSource = place.Source,
                opacity = 255,
                fVisible = true,
            };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
        foreach (var gone in _thumbnails.Keys.Where(w => !shown.Contains(w)).ToList())
        {
            DwmUnregisterThumbnail(_thumbnails[gone]);
            _thumbnails.Remove(gone);
            _thumbnailPlaces.Remove(gone);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Saved layouts to open: into this group while it has no windows, otherwise as a new group.</summary>
    private MenuItem OpenLayoutMenu()
    {
        var open = new MenuItem { Header = "Open Layout" };
        foreach (var layout in SavedLayouts().OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
            open.Items.Add(MenuItem(layout.Name, () => OpenLayoutRequested?.Invoke(this, layout)));
        if (open.Items.Count == 0) open.Items.Add(new MenuItem { Header = "(No saved layouts)", IsEnabled = false });
        return open;
    }

    /// <summary>A cheat sheet of the shortcuts; choosing one opens the settings to change it.</summary>
    private MenuItem ShortcutsMenu()
    {
        var list = new MenuItem { Header = "Keyboard Shortcuts" };
        var bindings = ShortcutBindings();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            var item = MenuItem(Shortcuts.Names[action], () => SettingsRequested?.Invoke());
            item.InputGestureText = bindings.TryGetValue(action, out var key) ? key.ToString() : "(none)";
            list.Items.Add(item);
        }
        return list;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
