using Dockyard.Domain;

namespace Dockyard.App.Controllers;

public enum ShortcutAction { PreviousTab, NextTab, FocusLeft, FocusRight, FocusUp, FocusDown, RemoveFromGroup }

/// <summary>
/// Keyboard shortcuts (Q7), all on Ctrl+Win: ←/→ switch tabs in the pane, Shift+arrows move to the next pane,
/// W takes the window out of its group.
/// </summary>
public static class Shortcuts
{
    private const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, W = 0x57;

    /// <summary>The action for a key pressed with Ctrl+Win held, or null if it is not a shortcut.</summary>
    public static ShortcutAction? Match(int virtualKey, bool shift) => (virtualKey, shift) switch
    {
        (Left, false) => ShortcutAction.PreviousTab,
        (Right, false) => ShortcutAction.NextTab,
        (Left, true) => ShortcutAction.FocusLeft,
        (Right, true) => ShortcutAction.FocusRight,
        (Up, true) => ShortcutAction.FocusUp,
        (Down, true) => ShortcutAction.FocusDown,
        (W, false) => ShortcutAction.RemoveFromGroup,
        _ => null,
    };

    internal static DockSide? Direction(ShortcutAction action) => action switch
    {
        ShortcutAction.FocusLeft => DockSide.Left,
        ShortcutAction.FocusRight => DockSide.Right,
        ShortcutAction.FocusUp => DockSide.Top,
        ShortcutAction.FocusDown => DockSide.Bottom,
        _ => null,
    };
}
