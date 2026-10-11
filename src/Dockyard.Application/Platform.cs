using Dockyard.Domain;

namespace Dockyard.Application;

// What the application needs from the machine. Implemented by Dockyard.Infrastructure and handed over as one
// IPlatform; the application itself never touches Win32 or the file system.

/// <summary>An app window the user could add to a group.</summary>
/// <param name="Controllable">False for windows Windows will not let this process move, such as elevated apps.</param>
public sealed record WindowInfo(nint Handle, string Title, string ProcessName, bool Controllable)
{
    public override string ToString() =>
        Controllable ? $"{Title}  —  {ProcessName}" : $"{Title}  —  {ProcessName}  (can't add apps running as administrator)";
}

public enum WindowEvent { Foreground, MoveSizeStart, MoveSizeEnd, LocationChanged, TitleChanged, Created, Shown, Destroyed }

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

/// <summary>The mouse and keyboard as they are right now.</summary>
public interface IInputState
{
    bool IsLeftButtonDown { get; }
    bool IsShiftDown { get; }
    bool IsEscapeDown { get; }
    bool TryGetCursor(out int x, out int y);
}

/// <summary>Events of other processes' top-level windows. Raised on the UI thread.</summary>
public interface IWindowEventSource
{
    event Action<WindowEvent, nint>? Received;

    /// <summary>The left button went down over a window while Shift was held; (0, default) when Shift was not held.</summary>
    event Action<nint, PixelRect>? ShiftMouseDown;

    /// <summary>Safety net for foreground changes Windows does not report: call it periodically.</summary>
    void CheckForeground();
}

/// <summary>Key presses with Ctrl, Alt or Win held, wherever the focus is.</summary>
public interface IKeyboardSource
{
    /// <summary>Gets the key and the modifiers held; returns true to take the key from the app that would get it.</summary>
    Func<int, KeyModifiers, bool>? KeyPressed { get; set; }
}

/// <summary>Saved layouts: the groups the user chose to keep, by name.</summary>
public interface ILayoutStore
{
    List<SavedGroup> Load();
    void Save(IReadOnlyList<SavedGroup> layouts);
}

/// <summary>What survives Dockyard going away: the open groups, kept to put windows back or reopen them.</summary>
public interface ISessionStore
{
    /// <summary>
    /// The open groups with their exact windows, kept current while Dockyard runs and removed when it puts the
    /// windows back. Finding it at startup means Dockyard went down without doing so.
    /// </summary>
    List<SavedGroup>? LoadSession();
    void SaveSession(IReadOnlyList<SavedGroup> groups);
    void ClearSession();

    /// <summary>The groups open at the last exit, kept like saved layouts to open them again at startup.</summary>
    List<SavedGroup>? LoadLastGroups();
    void SaveLastGroups(IReadOnlyList<SavedGroup> groups);
}

public interface ISettingsStore
{
    Settings Load();
    void Save(Settings settings);
}

/// <summary>Starts an app that a layout needs and that is not running.</summary>
public interface IAppLauncher
{
    void Launch(string exePath);
}

/// <summary>Shortcut files (.lnk) that start Dockyard with a saved layout; see <see cref="LaunchCommand"/>.</summary>
public interface ILayoutShortcuts
{
    /// <summary>Writes the shortcut on the desktop, replacing one for the same layout. Returns its path, or null if it could not be written.</summary>
    string? CreateOnDesktop(string layoutName);
}

/// <summary>Starting Dockyard with Windows.</summary>
public interface IAutostart
{
    Task<bool> IsEnabledAsync();

    /// <summary>Returns what the setting is afterwards: Windows may refuse (a policy, or the user turned it off in Task Manager).</summary>
    Task<bool> SetAsync(bool enable);
}

public interface ILog
{
    void Error(string context, Exception ex);

    /// <summary>Diagnostic trace, written only when tracing is on.</summary>
    void Trace(string message);
}

/// <summary>Everything the application takes from the machine, in one piece.</summary>
public interface IPlatform
{
    IWindowSystem Windows { get; }
    IWindowCatalog Catalog { get; }
    IInputState Input { get; }
    IWindowEventSource WindowEvents { get; }
    IKeyboardSource Keyboard { get; }
    ILayoutStore Layouts { get; }
    ISessionStore Sessions { get; }
    ISettingsStore Settings { get; }
    IAppLauncher Launcher { get; }
    ILayoutShortcuts LayoutShortcuts { get; }
    IAutostart Autostart { get; }
    ILog Log { get; }
}
