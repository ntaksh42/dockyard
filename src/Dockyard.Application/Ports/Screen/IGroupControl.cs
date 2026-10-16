using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>
/// What a frame tells its group's controller: window-state changes and the user's actions on the frame.
/// Implemented by the group controller.
/// </summary>
public interface IGroupControl
{
    LayoutMetrics Metrics { get; }
    bool IsLive { get; }
    bool AutoHideTitleBars { get; }

    /// <summary>The tab the user last worked in, which keyboard shortcuts act on when the frame has the focus.</summary>
    WindowHandle? LastActive { get; }

    /// <param name="includeWindows">
    /// Whether tabs keep their window handle and pre-join bounds, which only mean something to the run that wrote
    /// them (the crash session). Left out of saved layouts: handles are reused and the place is stale by then.
    /// </param>
    SavedNode SaveLayout(bool includeWindows = true);
    int RestoreLayout(SavedNode layout, Func<SavedTab, WindowHandle> resolve);

    bool Adopt(WindowHandle window, Pane pane, DockSide? side, PixelRect? originalBounds = null);
    void PickerCancelled();
    void ActivateTab(WindowHandle window);

    /// <summary>Takes a window out of the group (a tab's x button).</summary>
    void Remove(WindowHandle window);
    void DragSplitter(SplitterLayout splitter, double pointer);
    void SetTabBarPlacement(TabBarPlacement placement);
    void SetAutoHideTitleBars(bool enabled);
    void UpdateTitleBarHover(int x, int y, WindowHandle hitWindow, bool interacting = false);
    void RevealTitleBar(WindowHandle window);
    bool IsTitleBarVisible(WindowHandle window);

    void BeginLive();
    void EndLive();

    void OnFrameMoved();
    void OnSurfaceResized();
    void OnFrameActivated();
    void OnFrameMinimized();
    void OnFrameRestored();
    void OnClosing();

    /// <summary>Called as the system is about to change the frame's z-order; returns the window to insert after instead, or null.</summary>
    WindowHandle? OnFrameZChanging(FrameZChange change);
}
