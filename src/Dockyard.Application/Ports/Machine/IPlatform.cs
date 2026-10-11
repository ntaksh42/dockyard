namespace Dockyard.Application;

// What the application needs from the machine. Implemented by Dockyard.Infrastructure and handed over as one
// IPlatform; the application itself never touches Win32 or the file system.

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
