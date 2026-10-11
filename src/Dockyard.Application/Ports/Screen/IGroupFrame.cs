using Dockyard.Domain;

namespace Dockyard.Application;

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

    /// <summary>The user gave the group another name.</summary>
    event Action? Renamed;
    event Action? FrameClosed;
    event Action<IGroupFrame, nint>? TabDragStarted;
    event Action<bool>? TabDragEnded;
    event Action? SettingsRequested;
    event Action<IGroupFrame>? SaveLayoutRequested;
    event Action<IGroupFrame, SavedGroup>? OpenLayoutRequested;
}
