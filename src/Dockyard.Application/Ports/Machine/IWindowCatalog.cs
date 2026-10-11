namespace Dockyard.Application;

/// <summary>Windows that would appear in Alt+Tab, excluding this process.</summary>
public interface IWindowCatalog
{
    IReadOnlyList<WindowInfo> GetCandidates();

    /// <summary>
    /// The window if it is an app window of another process; null otherwise. The title may still be empty for
    /// a window that has just appeared.
    /// </summary>
    WindowInfo? Describe(nint window);
}
