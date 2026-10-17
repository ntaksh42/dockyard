using System.ComponentModel;
using System.Runtime.InteropServices;
using Dockyard.Infrastructure.Internal.Logging;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary>
/// Reports windows that flash to get the user's attention, as the taskbar sees them. WinEvents do not report
/// flashing; shell hook messages do, but only to a window. This one is hidden and never shown. A message-only
/// window would not do: shell hook messages go to top-level windows only.
/// </summary>
internal sealed class ShellHook : IDisposable
{
    private const string ClassName = "Dockyard.ShellHook";
    // HSHELL_REDRAW with HSHELL_HIGHBIT: the shell's "flash" notification.
    private const nint HSHELL_FLASH = 0x8006;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x00000080;

    private readonly WindowProc _proc; // kept alive so the GC does not collect the callback
    private readonly Action<nint> _flashed;
    private readonly uint _shellMessage;
    private readonly nint _hwnd;

    /// <param name="flashed">Called on the creating thread with the window that flashed.</param>
    public ShellHook(Action<nint> flashed)
    {
        _flashed = flashed;
        _proc = OnMessage;
        _shellMessage = RegisterWindowMessageW("SHELLHOOK");
        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = instance,
            lpszClassName = ClassName,
        };
        // Tabs only lose their attention marks without it; the rest works, so log and go on.
        if (RegisterClassExW(ref windowClass) == 0)
        {
            Log.Error("Shell hook class", new Win32Exception());
            return;
        }
        _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, ClassName, ClassName, WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_hwnd == 0 || !RegisterShellHookWindow(_hwnd)) Log.Error("Shell hook", new Win32Exception());
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == _shellMessage && wParam == HSHELL_FLASH && lParam != 0)
        {
            // An exception escaping a window procedure called from native code would end the process.
            try { _flashed(lParam); }
            catch (Exception ex) { Log.Error($"Shell hook flash hwnd=0x{lParam:X}", ex); }
            return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == 0) return;
        DeregisterShellHookWindow(_hwnd);
        DestroyWindow(_hwnd);
    }
}
