using Dockyard.Domain;

namespace Dockyard.Application;

// The user interface as the application sees it. Implemented by Dockyard.Presentation.

/// <summary>What a frame asks the workspace for while it is open.</summary>
public interface IFrameContext
{
    /// <summary>Whether a window already belongs to some group, so the picker can leave it out.</summary>
    bool IsGrouped(nint window);

    /// <summary>The saved layouts, read when a menu offering them opens.</summary>
    IReadOnlyList<SavedGroup> SavedLayouts();

    /// <summary>The current shortcut bindings, listed in the "+" menu.</summary>
    IReadOnlyDictionary<ShortcutAction, ShortcutKey> ShortcutBindings();
}

/// <summary>
/// What a frame tells its group's controller: window-state changes and the user's actions on the frame.
/// Implemented by the group controller.
/// </summary>
public interface IGroupControl
{
    LayoutMetrics Metrics { get; }
    bool IsLive { get; }

    /// <summary>The tab the user last worked in, which keyboard shortcuts act on when the frame has the focus.</summary>
    nint? LastActive { get; }

    /// <param name="includeWindows">
    /// Whether tabs keep their window handle and pre-join bounds, which only mean something to the run that wrote
    /// them (the crash session). Left out of saved layouts: handles are reused and the place is stale by then.
    /// </param>
    SavedNode SaveLayout(bool includeWindows = true);
    int RestoreLayout(SavedNode layout, Func<SavedTab, nint> resolve);

    bool Adopt(nint window, Pane pane, DockSide? side, PixelRect? originalBounds = null);
    void PickerCancelled();
    void ActivateTab(nint window);

    /// <summary>Takes a window out of the group (a tab's x button).</summary>
    void Remove(nint window);
    void DragSplitter(SplitterLayout splitter, double pointer);
    void SetTabBarPlacement(TabBarPlacement placement);

    void BeginLive();
    void EndLive();

    void OnFrameMoved();
    void OnSurfaceResized();
    void OnFrameActivated();
    void OnFrameMinimized();
    void OnFrameRestored();
    void OnClosing();

    /// <summary>Called as the system is about to change the frame's z-order; returns the window to insert after instead, or null.</summary>
    nint? OnFrameZChanging(FrameZChange change);
}

/// <summary>A group's frame window: <see cref="IGroupView"/> plus what the workspace does with it.</summary>
public interface IGroupFrame : IGroupView
{
    string GroupName { get; set; }

    /// <summary>True while the frame is on screen and no dialog of its own is open: windows may be dropped on it.</summary>
    bool CanDock { get; }

    /// <summary>True while the keyboard focus is in a text box of the frame, which then takes the keys.</summary>
    bool IsEditingText { get; }

    /// <summary>The frame's window rectangle in screen pixels, for saving the group.</summary>
    PixelRect FrameBounds { get; }

    /// <summary>Connects the controller, which exists only once the frame does. Applies the tab bar placement the frame was created with.</summary>
    void Bind(IGroupControl control);

    /// <summary>Shows the frame, at <paramref name="bounds"/> (screen pixels) when given.</summary>
    void ShowAt(PixelRect? bounds);

    void SetTabBarPlacement(TabBarPlacement placement);

    /// <summary>Runs a shortcut that acts on the frame (see <see cref="Shortcuts.IsFrameAction"/>).</summary>
    void RunShortcut(ShortcutAction action);

    void Close();

    event Action? FrameActivated;
    event Action? FrameMoved;
    event Action? FrameClosed;
    event Action<IGroupFrame, nint>? TabDragStarted;
    event Action<bool>? TabDragEnded;
    event Action? SettingsRequested;
    event Action<IGroupFrame>? SaveLayoutRequested;
    event Action<IGroupFrame, SavedGroup>? OpenLayoutRequested;
}

/// <summary>Where a dragged window would land, drawn over the frame it is above.</summary>
public interface IDockGuide
{
    void Show(IGroupFrame group, DockTarget drop);
    void Hide();
}

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
    void ShowLayouts();

    /// <summary>Tells the user something briefly, e.g. with a tray balloon.</summary>
    void Notify(string message);

    /// <summary>Ends the program once the workspace has put everything back.</summary>
    void Exit();
}
