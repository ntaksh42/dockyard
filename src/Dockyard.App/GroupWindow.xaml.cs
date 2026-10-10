using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dockyard.Application;
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
        AccentBrush = Brush("#3B82F6"), PlaceholderBrush = Brush("#1F2328"), SplitterBrush = Brushes.Transparent;

    private readonly Func<nint, bool> _isGrouped;
    private SplitterLayout? _dragging;
    private nint _hwnd;
    private bool _closing;
    /// <summary>The open window picker. Tabs must stay below it, so raising starts there instead of at the top.</summary>
    private WindowPicker? _picker;

    /// <param name="isGrouped">Whether a window already belongs to some group, so the picker can leave it out.</param>
    internal GroupWindow(string name, IWindowSystem windows, Func<nint, bool> isGrouped)
    {
        InitializeComponent();
        _isGrouped = isGrouped;
        GroupName = name;
        GroupNameText.Text = name;
        Title = $"[{name}]";
        Controller = new GroupController(this, windows, new WpfScheduler(Dispatcher));

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        };
        LocationChanged += (_, _) => Controller.OnFrameMoved();
        Surface.SizeChanged += (_, _) => Controller.OnSurfaceResized();
        // The maximized window rect is only final after the state change; re-measure the overhang on resize.
        SizeChanged += (_, _) => UpdateMaximizedMargin();
        Activated += (_, _) => Controller.OnFrameActivated();
        StateChanged += (_, _) =>
        {
            UpdateMaximizedMargin();
            if (WindowState == WindowState.Minimized) Controller.OnFrameMinimized();
            else Controller.OnFrameRestored();
        };
        Closing += (_, _) =>
        {
            _closing = true;
            Controller.OnClosing();
        };
        Surface.MouseMove += (_, e) =>
        {
            if (_dragging is not { } s) return;
            var pos = e.GetPosition(Surface);
            Controller.DragSplitter(s, s.Split.Orientation == Orientation.Horizontal ? pos.X : pos.Y);
        };
        Surface.MouseLeftButtonUp += (_, _) => { _dragging = null; Surface.ReleaseMouseCapture(); };
        Surface.LostMouseCapture += (_, _) => _dragging = null;
    }

    public string GroupName { get; }

    internal GroupController Controller { get; }

    // ---- IGroupView ---------------------------------------------------------------------------

    public nint Handle => _hwnd;

    public bool IsOnScreen =>
        !_closing && _hwnd != 0 && WindowState != WindowState.Minimized && PresentationSource.FromVisual(Surface) is not null;

    public bool IsMinimized => WindowState == WindowState.Minimized;

    public DomainSize SurfaceSize => new(Surface.ActualWidth, Surface.ActualHeight);

    public double Scale => PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;

    public nint ZAnchor => _picker is { } p ? new WindowInteropHelper(p).Handle : 0;

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
        foreach (var p in snapshot.Layout.Panes)
        {
            Place(BuildTabBar(p.Pane, snapshot.Titles), p.TabBar);
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
        // Window switchers such as Waypoint list the frame under this title (Q5).
        Title = snapshot.ActiveTitle.Length == 0 ? $"[{GroupName}]" : $"[{GroupName}] {snapshot.ActiveTitle}";
    }

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

    // ---- drawing helpers ----------------------------------------------------------------------

    private FrameworkElement BuildTabBar(Pane pane, IReadOnlyDictionary<nint, string> titles)
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
            close.Click += (_, _) => Controller.Remove(h);
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
                        new TextBlock { Text = titles[h], MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                        close,
                    },
                },
            };
            tab.MouseLeftButtonDown += (_, _) => Controller.ActivateTab(h);
            DockPanel.SetDock(tab, Dock.Left);
            bar.Children.Add(tab);
        }
        return bar;
    }

    private void Place(FrameworkElement e, DomainRect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
        Surface.Children.Add(e);
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
