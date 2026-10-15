using System.Diagnostics;
using Dockyard.Application;
using Dockyard.Domain;
using static Dockyard.Infrastructure.Internal.Win32.Native;

namespace Dockyard.Infrastructure.Internal.Win32;

/// <summary><see cref="IWindowCatalog"/> on top of Win32.</summary>
internal sealed class Win32WindowCatalog : IWindowCatalog
{
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Top-level windows that would appear in Alt+Tab, excluding this process.</summary>
    public IReadOnlyList<WindowInfo> GetCandidates()
    {
        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (Describe(new WindowHandle(hwnd)) is { Title.Length: > 0 } info) result.Add(info);
            return true;
        }, 0);
        return result;
    }

    /// <summary>
    /// The window if it is an app window of another process, as Alt+Tab would list it once it has a title; null
    /// otherwise. The title may still be empty for a window that has just appeared.
    /// </summary>
    public WindowInfo? Describe(WindowHandle window)
    {
        var hwnd = window.Value;
        if (!IsWindowVisible(hwnd) || IsCloaked(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return null;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return null;
        if (ShellClasses.Contains(GetClassName(hwnd))) return null;
        var pid = GetAppProcessId(hwnd);
        if (pid == (uint)Environment.ProcessId) return null;
        return new WindowInfo(window, GetWindowText(hwnd), ProcessNameOf(pid), CanControl(hwnd));
    }

    /// <summary>
    /// A no-op SetWindowPos fails with access denied when User Interface Privilege Isolation blocks us, which
    /// is the case for windows of elevated processes while Dockyard runs unelevated.
    /// </summary>
    private static bool CanControl(nint hwnd) =>
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);

    internal static ProcessName ProcessNameOf(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return new ProcessName(process.ProcessName);
        }
        catch (ArgumentException) { return new ProcessName("?"); }
    }
}
