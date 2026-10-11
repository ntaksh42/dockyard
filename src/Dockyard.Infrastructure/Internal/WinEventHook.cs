using Dockyard.Application;
using static Dockyard.Infrastructure.Internal.Native;
using System.Runtime.InteropServices;

namespace Dockyard.Infrastructure.Internal;

/// <summary>
/// Out-of-context WinEvent hooks for top-level windows of other processes. Callbacks arrive on the creating
/// (UI) thread.
/// </summary>
internal sealed class WinEventHook : IWindowEventSource, IDisposable
{
    private static readonly Dictionary<uint, WindowEvent> Events = new()
    {
        [EVENT_SYSTEM_FOREGROUND] = WindowEvent.Foreground,
        [EVENT_SYSTEM_MOVESIZESTART] = WindowEvent.MoveSizeStart,
        [EVENT_SYSTEM_MOVESIZEEND] = WindowEvent.MoveSizeEnd,
        [EVENT_OBJECT_DESTROY] = WindowEvent.Destroyed,
        [EVENT_OBJECT_CREATE] = WindowEvent.Created,
        [EVENT_OBJECT_SHOW] = WindowEvent.Shown,
        [EVENT_OBJECT_LOCATIONCHANGE] = WindowEvent.LocationChanged,
        [EVENT_OBJECT_NAMECHANGE] = WindowEvent.TitleChanged,
    };

    private readonly WinEventProc _proc; // kept alive so the GC does not collect the callback
    private readonly List<nint> _hooks = [];
    private nint _lastForeground;
    private readonly MouseHookProc _mouseProc;
    private readonly nint _mouseHook;

    public event Action<WindowEvent, nint>? Received;
    public event Action<nint, PixelRect>? ShiftMouseDown;

    public WinEventHook()
    {
        _proc = OnEvent;
        _mouseProc = OnMouse;
        _mouseHook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, _mouseProc, 0, 0);
        // Without it Shift-dragging a title bar does nothing; the rest still works, so log and go on.
        if (_mouseHook == 0) Log.Error("Mouse hook", new System.ComponentModel.Win32Exception());
        foreach (var e in Events.Keys)
        {
            // Foreground changes can be attributed to this process (e.g. when it is the one giving up the
            // foreground), and SKIPOWNPROCESS then drops them. Our own windows are filtered out by the receiver.
            var flags = e == EVENT_SYSTEM_FOREGROUND ? WINEVENT_OUTOFCONTEXT : WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
            _hooks.Add(SetWinEventHook(e, e, 0, _proc, 0, 0, flags));
        }
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        // Capture before Windows moves the caption: out-of-context move-start events can arrive late.
        if (code >= 0 && message == 0x0201)
        {
            try
            {
                if ((GetAsyncKeyState(0x10) & 0x8000) == 0) ShiftMouseDown?.Invoke(0, default);
                else
                {
                    var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
                    var window = GetAncestor(WindowFromPoint(mouse.point), 2 /* GA_ROOT */);
                    if (window != 0 && GetWindowRect(window, out var r))
                        ShiftMouseDown?.Invoke(window, new PixelRect(r.Left, r.Top, r.Right, r.Bottom));
                }
            }
            catch (Exception ex) { Log.Error("Mouse hook", ex); }
        }
        return CallNextHookEx(_mouseHook, code, message, data);
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
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            // The owner chain shows whether an owned popup or dialog of a member took the foreground, which the
            // receiver does not map back to the member.
            GetWindowThreadProcessId(hwnd, out var pid);
            Log.Trace($"raw foreground hwnd=0x{hwnd:X} obj={idObject} child={idChild} owner=0x{GetWindow(hwnd, GW_OWNER):X} " +
                $"rootOwner=0x{GetAncestor(hwnd, 3 /* GA_ROOTOWNER */):X} pid={pid} title=\"{GetWindowText(hwnd)}\"");
        }
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
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
