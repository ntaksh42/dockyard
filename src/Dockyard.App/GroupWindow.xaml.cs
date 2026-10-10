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
        AccentBrush = Brush("#3B82F6"), PlaceholderBrush = Brush("#1F2328"), SplitterBrush = Brushes.Transparent, MutedTextBrush = Brush("#9CA3AF");

    private readonly Func<nint, bool> _isGrouped;
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
    /// <summary>The open window picker. Tabs must stay below it, so raising starts there instead of at the top.</summary>
    private WindowPicker? _picker;
    /// <summary>Draws the tab bars above the member windows with tab bar placement B; null otherwise.</summary>
    private Window? _overlay;
    private Canvas? _overlaySurface;
    /// <summary>Hides the group name and title again once the pointer has left the caption.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _captionHide = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <param name="isGrouped">Whether a window already belongs to some group, so the picker can leave it out.</param>
    internal GroupWindow(string name, IWindowSystem windows, Func<nint, bool> isGrouped, TabBarPlacement placement)
    {
        InitializeComponent();
        _isGrouped = isGrouped;
        GroupName = name;
        Controller = new GroupController(this, windows, new WpfScheduler(Dispatcher));
        SetTabBarPlacement(placement);
        _captionHide.Tick += (_, _) => HideCaptionInfoIfLeft();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        };
        LocationChanged += (_, _) =>
        {
            PositionOverlay();
            Controller.OnFrameMoved();
        };
        Surface.SizeChanged += (_, _) => Controller.OnSurfaceResized();
        // The maximized window rect is only final after the state change; re-measure the overhang on resize.
        SizeChanged += (_, _) => UpdateMaximizedMargin();
        Activated += (_, _) => Controller.OnFrameActivated();
        StateChanged += (_, _) =>
        {
            UpdateMaximizedMargin();
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
        Surface.Children.Clear();
        _overlaySurface?.Children.Clear();
        PositionOverlay();
        foreach (var p in snapshot.Layout.Panes)
        {
            // Over a member window the tabs must be in the overlay; the frame is below the members.
            var overMember = _overlaySurface is not null && p.Pane.Windows.Count > 0;
            Place(BuildTabBar(p.Pane, snapshot.Titles, p.TabBar, overMember), p.TabBar, overMember ? _overlaySurface : null);
            if (p.Pane.Windows.Count == 0)
            {
                var add = new Button { Content = "Add Window…", Padding = new Thickness(12, 4, 12, 4) };
                add.Click += (_, _) => PickAndAdopt(p.Pane, null);
                var host = new Grid { Background = PlaceholderBrush };
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                // A restored tab whose app is not open: say what will fill the pane once it is.
                if (snapshot.Waiting.TryGetValue(p.Pane, out var waiting))
                    foreach (var tab in waiting)
                        content.Children.Add(new TextBlock { Text = $"Waiting for: {tab.ProcessName} — {tab.Title}", Foreground = MutedTextBrush,
                            FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center });
                content.Children.Add(add);
                host.Children.Add(content);
                Place(host, p.Content);
            }
            else
            {
                Place(new Border { Background = PlaceholderBrush }, p.Content);
            }
        }

        foreach (var s in snapshot.Layout.Splitters)
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
                if (Surface.CaptureMouse()) _dragging = s;
                e.Handled = true;
            };
            Place(bar, s.Bounds);
        }

        ActiveTitleText.Text = snapshot.ActiveTitle;
        UpdateTitle();
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
        var bar = new DockPanel { Background = overMember ? null : TabBarBrush, LastChildFill = false };

        var add = new Button { Content = "+", Width = vertical ? double.NaN : 28, Height = vertical ? 28 : double.NaN, BorderThickness = new Thickness(0),
            Background = overMember ? TabBarBrush : Brushes.Transparent, ToolTip = "Add Window" };
        // Built on click: tab bars are rebuilt on every layout change, including each step of a drag.
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(MenuItem("Add as Tab…", () => PickAndAdopt(pane, null)));
            menu.Items.Add(MenuItem("Split Right…", () => PickAndAdopt(pane, DockSide.Right)));
            menu.Items.Add(MenuItem("Split Down…", () => PickAndAdopt(pane, DockSide.Bottom)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Settings…", () => SettingsRequested?.Invoke()));

            menu.IsOpen = true;
        };
        // Added first so it keeps its room when the tabs overflow; over a member it follows the tabs instead.
        DockPanel.SetDock(add, vertical ? Dock.Bottom : overMember ? Dock.Left : Dock.Right);
        if (!overMember) bar.Children.Add(add);

        foreach (var h in pane.Windows)
        {
            var active = pane.ActiveWindow == h;
            var close = new Button { Content = "×", BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(4, 0, 4, 0), ToolTip = "Remove from Group" };
            close.Click += (_, _) => Controller.Remove(h);
            var tab = new Border
            {
                Background = active ? ActiveTabBrush : TabBrush,
                BorderBrush = AccentBrush,
                BorderThickness = vertical ? new Thickness(active ? 2 : 0, 0, 0, 0) : new Thickness(0, active ? 2 : 0, 0, 0),
                Padding = new Thickness(10, 0, 4, 0),
                Margin = vertical ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0),
                Height = vertical ? 28 : double.NaN,
                Child = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = titles[h], MaxWidth = vertical ? Math.Max(0, bounds.Width - 40) : 200, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
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
            DockPanel.SetDock(tab, vertical ? Dock.Top : Dock.Left);
            bar.Children.Add(tab);
        }
        if (overMember) bar.Children.Add(add);
        return bar;
    }

    private void Place(FrameworkElement e, DomainRect r, Canvas? canvas = null)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
        (canvas ?? Surface).Children.Add(e);
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
        // The caption is non-client area: WPF gets no mouse events there, only the window does.
        if (msg == 0x00A0 /* WM_NCMOUSEMOVE */) ShowCaptionInfo();
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

    private void GroupChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || GroupNameEdit.IsVisible) return;
        GroupNameEdit.Text = GroupName;
        GroupNameText.Visibility = Visibility.Hidden;
        GroupNameEdit.Visibility = Visibility.Visible;
        GroupNameEdit.Focus();
        GroupNameEdit.SelectAll();
        e.Handled = true;
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

    private void Caption_MouseEnter(object sender, MouseEventArgs e) => ShowCaptionInfo();

    /// <summary>The group name and active title are shown only while the pointer is over the caption.</summary>
    private void ShowCaptionInfo()
    {
        CaptionInfo.Opacity = 1;
        _captionHide.Start();
    }

    private void HideCaptionInfoIfLeft()
    {
        if (_closing || GroupNameEdit.IsVisible) return;
        GetCursorPos(out var cursor);
        var caption = ToScreenRect(Caption);
        if (cursor.X >= caption.Left && cursor.X < caption.Right && cursor.Y >= caption.Top && cursor.Y < caption.Bottom) return;
        CaptionInfo.Opacity = 0;
        _captionHide.Stop();
    }

    private static PixelRect ToScreenRect(FrameworkElement e)
    {
        var tl = e.PointToScreen(new Point(0, 0));
        var br = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
        return new PixelRect((int)tl.X, (int)tl.Y, (int)br.X, (int)br.Y);
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
        if (bounds is { } b) SetWindowPos(_hwnd, 0, b.Left, b.Top, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        // Lay out now, so a layout restored right after this is placed against the final size.
        UpdateLayout();
    }

    private void SaveLayout_Click(object sender, RoutedEventArgs e) => SaveLayoutRequested?.Invoke(this);

    private void Minimize_Click(
object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

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
