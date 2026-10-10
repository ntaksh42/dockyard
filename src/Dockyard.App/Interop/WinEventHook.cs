using static Dockyard.App.Interop.Native;

namespace Dockyard.App.Interop;

/// <summary>Out-of-context WinEvent hooks for top-level windows of other processes. Callbacks arrive on the creating (UI) thread.</summary>
internal sealed class WinEventHook : IDisposable
{
    private static readonly uint[] Events =
        [EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, EVENT_OBJECT_DESTROY, EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_NAMECHANGE];

    private readonly WinEventProc _proc; // kept alive so the GC does not collect the callback
    private readonly List<nint> _hooks = [];

    public event Action<uint, nint>? WindowEvent;

    public WinEventHook()
    {
        _proc = OnEvent;
        foreach (var e in Events)
        {
            // Foreground changes can be attributed to this process (e.g. when it is the one giving up the
            // foreground), and SKIPOWNPROCESS then drops them. Our own windows are filtered out by the receiver.
            var flags = e == EVENT_SYSTEM_FOREGROUND ? WINEVENT_OUTOFCONTEXT : WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
            _hooks.Add(SetWinEventHook(e, e, 0, _proc, 0, 0, flags));
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND) Log.Trace($"raw foreground hwnd=0x{hwnd:X} obj={idObject} child={idChild}");
        // Object events fire for every child element too; only the window itself matters. System events
        // (foreground, move/size) always concern the window, and console windows report them with other ids.
        if (hwnd == 0 || (eventType >= 0x8000 && (idObject != OBJID_WINDOW || idChild != CHILDID_SELF))) return;
        // This runs inside a native callback: an exception escaping here kills the process outright,
        // without reaching WPF's unhandled-exception handler.
        try
        {
            WindowEvent?.Invoke(eventType, hwnd);
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
