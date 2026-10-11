using Dockyard.Domain;

namespace Dockyard.Application.Internal;

/// <summary>
/// One desktop drag session shared by all group frames: a tab or an outside window is dragged over the frames,
/// and on release it is docked where the guide shows, or taken out of its group when dropped outside it.
/// </summary>
internal sealed class DockDragSession
{
    private readonly IWindowSystem _windows;
    private readonly IInputState _input;
    private readonly IDockGuide _guide;
    private readonly Func<IEnumerable<OpenGroup>> _groups;
    private readonly IRestartableTimer _timer;
    private bool _running;
    private OpenGroup? _source, _target;
    private DockTarget? _drop;
    private WindowHandle _window;
    private bool _cancelled;
    private PixelRect _originalBounds;

    public DockDragSession(IWindowSystem windows, IInputState input, IScheduler scheduler, IDockGuide guide, Func<IEnumerable<OpenGroup>> groups)
    {
        _windows = windows;
        _input = input;
        _guide = guide;
        _groups = groups;
        _timer = scheduler.CreateTimer(TimeSpan.FromMilliseconds(30), Tick);
    }

    public void Begin(WindowHandle window, OpenGroup? source, PixelRect? originalBounds = null)
    {
        Finish(true);
        _window = window;
        _source = source;
        _originalBounds = originalBounds ?? _windows.GetBounds(window);
        _cancelled = false;
        source?.Controller.BeginDockDrag(window);
        _running = true;
        _timer.Restart();
        Update();
    }

    private void Tick()
    {
        if (!_running) return;
        if (_input.IsEscapeDown || !_windows.Exists(_window))
        {
            _cancelled = true;
            HideGuide();
        }
        else if (!_cancelled) Update();
        _timer.Restart();
    }

    private void Update()
    {
        if (!_input.TryGetCursor(out var x, out var y)) { HideGuide(); return; }
        _target = null;
        _drop = null;
        var frames = _groups().ToDictionary(g => g.Frame.Handle);
        // Topmost first, so a frame over another one wins.
        var ordered = new List<OpenGroup>();
        foreach (var handle in _windows.GetTopLevelWindows())
            if (frames.TryGetValue(handle, out var group)) ordered.Add(group);
        foreach (var group in ordered)
        {
            if (!group.Frame.CanDock || group.Controller.Layout is not { } layout) continue;
            var point = group.Frame.FromScreen(new PixelRect(x, y, x, y));
            if (Docking.HitTest(layout, point.X, point.Y) is not { } drop) continue;
            if (group == _source && drop.Layout.Pane.Windows.Count == 1 && drop.Layout.Pane.Windows.Contains(_window))
                drop = drop with { Side = null, Preview = drop.Layout.Bounds };
            _target = group;
            _drop = drop;
            _guide.Show(group.Frame, drop);
            return;
        }
        HideGuide();
    }

    public void Finish(bool cancel = false)
    {
        if (_window.IsNone) return;
        _running = false;
        _timer.Stop();
        if (!cancel && !_cancelled) Update();
        var window = _window;
        var source = _source;
        var target = _target;
        var drop = _drop;
        var cancelled = cancel || _cancelled;
        _window = WindowHandle.None;
        _source = null;
        HideGuide();
        if (!cancelled && target is not null && drop is not null)
            target.Controller.Dock(window, drop.Layout.Pane, drop.Side, source?.Controller, _originalBounds);
        else if (!cancelled && source is { Frame.CanDock: true } && _input.TryGetCursor(out var x, out var y))
        {
            // Release only outside the frame, not over the caption or a splitter.
            var frame = _windows.GetBounds(source.Frame.Handle);
            if (x < frame.Left || x >= frame.Right || y < frame.Top || y >= frame.Bottom)
                source.Controller.Remove(window);
        }
        source?.Controller.EndDockDrag();
    }

    private void HideGuide()
    {
        _guide.Hide();
        _target = null;
        _drop = null;
    }
}
