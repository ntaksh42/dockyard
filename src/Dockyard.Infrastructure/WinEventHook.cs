using static Dockyard.Infrastructure.Native;

namespace Dockyard.Infrastructure;

public enum WindowEvent { Foreground, MoveSizeStart, MoveSizeEnd, LocationChanged, TitleChanged, Destroyed }

/// <summary>
/// Out-of-context WinEvent hooks for top-level windows of other processes. Callbacks arrive on the creating
/// (UI) thread.
/// </summary>
public sealed class WinEventHook : IDisposable
{
    private static readonly Dictionary<uint, WindowEvent> Events = new()
    {
        [EVENT_SYSTEM_FOREGROUND] = WindowEvent.Foreground,
        [EVENT_SYSTEM_MOVESIZESTART] = WindowEvent.MoveSizeStart,
        [EVENT_SYSTEM_MOVESIZEEND] = WindowEvent.MoveSizeEnd,
        [EVENT_OBJECT_DESTROY] = WindowEvent.Destroyed,
        [EVENT_OBJECT_LOCATIONCHANGE] = WindowEvent.LocationChanged,
        [EVENT_OBJECT_NAMECHANGE] = WindowEvent.TitleChanged,
    };

    private readonly WinEventProc _proc; // kept alive so the GC does not collect the callback
    private readonly List<nint> _hooks = [];
    private nint _lastForeground;

    public event Action<WindowEvent, nint>? Received;

    public WinEventHook()
    {
        _proc = OnEvent;
        foreach (var e in Events.Keys)
        {
            // Foreground changes can be attributed to this process (e.g. when it is the one giving up the
            // foreground), and SKIPOWNPROCESS then drops them. Our own windows are filtered out by the receiver.
            var flags = e == EVENT_SYSTEM_FOREGROUND ? WINEVENT_OUTOFCONTEXT : WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
            _hooks.Add(SetWinEventHook(e, e, 0, _proc, 0, 0, flags));
        }
    }

    /// <summary>
    /// Safety net for foreground changes Windows does not report (seen when switching between console windows):
    /// call this periodically on the UI thread; a change since the last report is raised as a foreground event.
    /// </summary>
    public void CheckForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == _lastForeground) return;
        Raise(EVENT_SYSTEM_FOREGROUND, fg);
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND) Log.Trace($"raw foreground hwnd=0x{hwnd:X} obj={idObject} child={idChild}");
        // Object events fire for every child element too; only the window itself matters. System events
        // (foreground, move/size) always concern the window, and console windows report them with other ids.
        if (hwnd == 0 || (eventType >= 0x8000 && (idObject != OBJID_WINDOW || idChild != CHILDID_SELF))) return;
        Raise(eventType, hwnd);
    }

    private void Raise(uint eventType, nint hwnd)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND) _lastForeground = hwnd;
        // This may run inside a native callback: an exception escaping here kills the process outright,
        // without reaching WPF's unhandled-exception handler.
        try
        {
            Received?.Invoke(Events[eventType], hwnd);
        }
        catch (Exception ex)
        {
            Log.Error($"WinEvent 0x{eventType:X} hwnd=0x{hwnd:X}", ex);
        }
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
