using Dockyard.Domain;

namespace Dockyard.Application;

// The user interface as the application sees it. Implemented by Dockyard.Presentation.

/// <summary>The windows and the tray of Dockyard, apart from what an <see cref="IPlatform"/> offers.</summary>
public interface IShell
{
    /// <summary>Deferred work on the UI thread.</summary>
    IScheduler Scheduler { get; }

    IDockGuide DockGuide { get; }

    /// <summary>Creates a frame and leaves it hidden; the workspace then binds its controller and shows it.</summary>
    IGroupFrame CreateFrame(string name, TabBarPlacement placement, IFrameContext context);

    /// <summary>
    /// True while the settings window waits for the keys of a shortcut and has the focus. The keyboard hook then
    /// sends key presses there instead: many shortcuts (anything with Win) never reach a WPF window.
    /// </summary>
    bool IsCapturingShortcut(nint foreground);

    void CaptureShortcut(ShortcutKey key);

    void ShowSettings();

    /// <summary>Shows the settings window on its Layouts tab, with the layout of that name chosen when given.</summary>
    void ShowLayouts(string? layoutName = null);

    /// <summary>
    /// Tells the user something briefly, e.g. with a tray balloon. <paramref name="onClick"/>, if given, runs when
    /// the user clicks the message.
    /// </summary>
    void Notify(string message, Action? onClick = null);

    /// <summary>Ends the program once the workspace has put everything back.</summary>
    void Exit();
}
