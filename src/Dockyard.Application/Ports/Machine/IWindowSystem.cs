namespace Dockyard.Application;

/// <summary>Operations on other applications' top-level windows. Implemented with Win32 in Infrastructure.</summary>
public interface IWindowSystem
{
    bool Exists(nint window);
    bool IsVisible(nint window);
    bool IsMinimized(nint window);
    bool IsMaximized(nint window);

    /// <summary>Whether the window is always on top.</summary>
    bool IsTopmost(nint window);

    string GetTitle(nint window);

    /// <summary>The window that has the keyboard focus, or 0.</summary>
    nint GetForeground();

    /// <summary>Whether the window is a top-level window rather than a child of another.</summary>
    bool IsTopLevel(nint window);

    /// <summary>Whether the window is being moved by its caption, as opposed to being resized.</summary>
    bool IsCaptionDrag(nint window);

    /// <summary>All top-level windows, topmost first.</summary>
    IReadOnlyList<nint> GetTopLevelWindows();

    /// <summary>Name of the process that owns the window, e.g. "notepad".</summary>
    string GetProcessName(nint window);

    /// <summary>Full path of the owning process's exe, or null if it cannot be read.</summary>
    string? GetProcessPath(nint window);

    /// <summary>What the user sees, without the invisible resize border most windows have.</summary>
    PixelRect GetVisibleBounds(nint window);

    /// <summary>The full window rectangle, including the invisible resize border.</summary>
    PixelRect GetBounds(nint window);

    void Restore(nint window);
    void Show(nint window);
    void Hide(nint window);

    /// <summary>Gives the window the keyboard focus (makes it the foreground window).</summary>
    void Focus(nint window);

    /// <summary>Moves the window so its visible bounds match <paramref name="target"/>. Does not wait for the app.</summary>
    void PlaceVisible(nint window, PixelRect target);

    /// <summary>Shows the window, sets its full rectangle and brings it to the top without activating it.</summary>
    void SetBounds(nint window, PixelRect bounds);

    /// <summary>Makes the window always on top again: <see cref="Stack"/> and <see cref="SetBounds"/> end in the normal band.</summary>
    void SetTopmost(nint window);

    /// <summary>
    /// Orders windows top to bottom: the first goes just below <paramref name="after"/> (0 for the very top of
    /// the z-order), each following one just below the previous.
    /// </summary>
    void Stack(nint after, IReadOnlyList<nint> topDown);
}
