using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>Operations on other applications' top-level windows. Implemented with Win32 in Infrastructure.</summary>
public interface IWindowSystem
{
    bool Exists(WindowHandle window);
    bool IsVisible(WindowHandle window);
    bool IsMinimized(WindowHandle window);
    bool IsMaximized(WindowHandle window);

    /// <summary>Whether the window is always on top.</summary>
    bool IsTopmost(WindowHandle window);

    string GetTitle(WindowHandle window);

    /// <summary>The window that has the keyboard focus, or <see cref="WindowHandle.None"/>.</summary>
    WindowHandle GetForeground();

    /// <summary>The top-level window under a screen position, including overlays.</summary>
    WindowHandle GetWindowAt(int x, int y);

    /// <summary>Hides a standard title bar, or restores only the caption bits Dockyard removed. Returns whether the style changed.</summary>
    bool SetTitleBarVisible(WindowHandle window, bool visible);

    /// <summary>Whether the window is a top-level window rather than a child of another.</summary>
    bool IsTopLevel(WindowHandle window);

    /// <summary>Whether the window is being moved by its caption, as opposed to being resized.</summary>
    bool IsCaptionDrag(WindowHandle window);

    /// <summary>All top-level windows, topmost first.</summary>
    IReadOnlyList<WindowHandle> GetTopLevelWindows();

    /// <summary>The process that owns the window.</summary>
    ProcessName GetProcessName(WindowHandle window);

    /// <summary>Full path of the owning process's exe, or null if it cannot be read.</summary>
    string? GetProcessPath(WindowHandle window);

    /// <summary>What the user sees, without the invisible resize border most windows have.</summary>
    PixelRect GetVisibleBounds(WindowHandle window);

    /// <summary>The full window rectangle, including the invisible resize border.</summary>
    PixelRect GetBounds(WindowHandle window);

    void Restore(WindowHandle window);
    void Show(WindowHandle window);
    void Hide(WindowHandle window);

    /// <summary>Asks the app to close the window, as its own close button does: it may ask to save first.</summary>
    void RequestClose(WindowHandle window);

    /// <summary>Gives the window the keyboard focus (makes it the foreground window).</summary>
    void Focus(WindowHandle window);

    /// <summary>Moves the window so its visible bounds match <paramref name="target"/>. Does not wait for the app.</summary>
    void PlaceVisible(WindowHandle window, PixelRect target);

    /// <summary>Shows the window, sets its full rectangle and brings it to the top without activating it.</summary>
    void SetBounds(WindowHandle window, PixelRect bounds);

    /// <summary>Makes the window always on top again: <see cref="Stack"/> and <see cref="SetBounds"/> end in the normal band.</summary>
    void SetTopmost(WindowHandle window);

    /// <summary>
    /// Orders windows top to bottom: the first goes just below <paramref name="after"/> (<see cref="WindowHandle.None"/> for the very top of
    /// the z-order), each following one just below the previous.
    /// </summary>
    void Stack(WindowHandle after, IReadOnlyList<WindowHandle> topDown);

    /// <summary>Whether queued z-order changes are still being applied, including changes to the frame itself.</summary>
    bool IsStacking => false;
}
