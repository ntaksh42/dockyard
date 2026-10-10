using System.Diagnostics;
using static Dockyard.App.Interop.Native;

namespace Dockyard.App.Interop;

/// <param name="Controllable">False for windows Windows will not let this process move, such as elevated apps.</param>
internal sealed record WindowInfo(nint Handle, string Title, string ProcessName, bool Controllable)
{
    public override string ToString() =>
        Controllable ? $"{Title}  —  {ProcessName}" : $"{Title}  —  {ProcessName}（管理者権限のアプリは追加できません）";
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
            result.Add(new WindowInfo(hwnd, title, ProcessName(pid), CanControl(hwnd)));
            return true;
        }, 0);
        return result;
    }

    /// <summary>
    /// A no-op SetWindowPos fails with access denied when User Interface Privilege Isolation blocks us, which
    /// is the case for windows of elevated processes while Dockyard runs unelevated.
    /// </summary>
    private static bool CanControl(nint hwnd) =>
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    private static string ProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { return "?"; }
    }
}
