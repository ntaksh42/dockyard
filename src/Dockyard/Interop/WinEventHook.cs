using static Dockyard.Interop.Native;

namespace Dockyard.Interop;

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
            _hooks.Add(SetWinEventHook(e, e, 0, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS));
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        WindowEvent?.Invoke(eventType, hwnd);
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
