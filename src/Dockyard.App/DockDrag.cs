using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dockyard.Domain;
using Dockyard.App.Controllers;
using Dockyard.Infrastructure;
using static Dockyard.Infrastructure.Native;

namespace Dockyard.App;

/// <summary>One desktop drag session shared by all group frames.</summary>
internal sealed class DockDrag(Func<IEnumerable<GroupWindow>> groups)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private Window? _overlay;
    private GroupWindow? _source, _target;
    private DockTarget? _drop;
    private nint _window;
    private bool _cancelled;
    private PixelRect _originalBounds;
    private DockGuide? _guide;

    private sealed record DockGuide(GroupWindow Group, DockTarget Drop, PixelRect Bounds, double Scale);

    public void Begin(nint window, GroupWindow? source, PixelRect? originalBounds = null)
    {
        Finish(true);
        _window = window;
        _source = source;
        GetWindowRect(window, out var original);
        _originalBounds = originalBounds ?? new PixelRect(original.Left, original.Top, original.Right, original.Bottom);
        _cancelled = false;
        source?.Controller.BeginDockDrag(window);
        _timer.Tick -= Tick;
        _timer.Tick += Tick;
        _timer.Start();
        Update();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0 || !IsWindow(_window))
        {
            _cancelled = true;
            HideGuide();
            return;
        }
        if (!_cancelled) Update();
    }

    private void Update()
    {
        if (!GetCursorPos(out var cursor)) { HideGuide(); return; }
        _target = null;
        _drop = null;
        var frames = groups().ToDictionary(g => g.Handle);
        var ordered = new List<GroupWindow>();
        EnumWindows((handle, _) =>
        {
            if (frames.TryGetValue(handle, out var frame)) ordered.Add(frame);
            return true;
        }, 0);
        foreach (var group in ordered)
        {
            if (!group.CanDock || group.Controller.Layout is not { } layout) continue;
            var point = group.FromScreen(new(cursor.X, cursor.Y, cursor.X, cursor.Y));
            if (Docking.HitTest(layout, point.X, point.Y) is not { } drop) continue;
            if (group == _source && drop.Layout.Pane.Windows.Count == 1 && drop.Layout.Pane.Windows.Contains(_window))
                drop = drop with { Side = null, Preview = drop.Layout.Bounds };
            _target = group;
            _drop = drop;
            ShowGuide(group, drop);
            return;
        }
        HideGuide();
    }

    public void Finish(bool cancel = false)
    {
        if (_window == 0) return;
        _timer.Stop();
        if (!cancel && !_cancelled) Update();
        var window = _window;
        var source = _source;
        var target = _target;
        var drop = _drop;
        var cancelled = cancel || _cancelled;
        _window = 0;
        _source = null;
        HideGuide();
        if (!cancelled && target is not null && drop is not null)
            target.Controller.Dock(window, drop.Layout.Pane, drop.Side, source?.Controller, _originalBounds);
        else if (!cancelled && source is { CanDock: true } && GetCursorPos(out var cursor))
        {
            // Release only outside the frame, not over the caption or a splitter.
            GetWindowRect(source.Handle, out var frame);
            if (cursor.X < frame.Left || cursor.X >= frame.Right || cursor.Y < frame.Top || cursor.Y >= frame.Bottom)
                source.Controller.Remove(window);
        }
        source?.Controller.EndDockDrag();
    }

    private void HideGuide()
    {
        _overlay?.Close();
        _overlay = null;
        _guide = null;
        _target = null;
        _drop = null;
    }

    private void ShowGuide(GroupWindow group, DockTarget drop)
    {
        var bounds = group.ToScreen(drop.Layout.Bounds);
        var scale = group.Scale;
        var state = new DockGuide(group, drop, bounds, scale);
        if (_guide == state) return;
        _guide = state;
        if (_overlay is null)
        {
            _overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                IsHitTestVisible = false,
            };
            _overlay.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(_overlay).Handle;
                SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)((long)GetWindowLongPtr(handle, GWL_EXSTYLE) | 0x20 | 0x08000000 | WS_EX_TOOLWINDOW));
            };
            _overlay.Show();
        }
        var canvas = new Canvas();
        var preview = drop.Preview;
        var highlight = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(2),
            Width = preview.Width,
            Height = preview.Height,
        };
        Canvas.SetLeft(highlight, preview.X - drop.Layout.Bounds.X);
        Canvas.SetTop(highlight, preview.Y - drop.Layout.Bounds.Y);
        canvas.Children.Add(highlight);
        var r = drop.Layout.Bounds;
        foreach (var (text, x, y) in new[] { ("Tab", .5, .5), ("←", .125, .5), ("→", .875, .5), ("↑", .5, .125), ("↓", .5, .875) })
        {
            var guide = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Background = Brushes.DodgerBlue,
                TextAlignment = TextAlignment.Center,
                Width = 38,
                Height = 28,
                Padding = new Thickness(2, 5, 2, 0)
            };
            Canvas.SetLeft(guide, Math.Max(0, r.Width * x - 19));
            Canvas.SetTop(guide, Math.Max(0, r.Height * y - 14));
            canvas.Children.Add(guide);
        }
        _overlay.Content = canvas;
        // Position in physical pixels so cross-monitor drags do not inherit the source monitor's DPI.
        SetWindowPos(new WindowInteropHelper(_overlay).Handle, HWND_TOPMOST, bounds.Left, bounds.Top,
            bounds.Width, bounds.Height, SWP_NOACTIVATE);
        var overlayScale = VisualTreeHelper.GetDpi(_overlay).DpiScaleX;
        canvas.LayoutTransform = new ScaleTransform(scale / overlayScale, scale / overlayScale);
    }
}
