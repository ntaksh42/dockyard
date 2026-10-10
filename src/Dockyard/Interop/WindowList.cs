using System.Diagnostics;
using static Dockyard.Interop.Native;

namespace Dockyard.Interop;

internal sealed record WindowInfo(nint Handle, string Title, string ProcessName)
{
    public override string ToString() => $"{Title}  —  {ProcessName}";
}

internal static class WindowList
{
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Top-level windows that would appear in Alt+Tab, excluding this process.</summary>
    public static List<WindowInfo> GetCandidates()
    {
        var ownPid = (uint)Environment.ProcessId;
        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsCloaked(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return true;
            var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            var title = GetWindowText(hwnd);
            if (title.Length == 0 || ShellClasses.Contains(GetClassName(hwnd))) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid) return true;
            result.Add(new WindowInfo(hwnd, title, ProcessName(pid)));
            return true;
        }, 0);
        return result;
    }

    private static string ProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { return "?"; }
    }
}
