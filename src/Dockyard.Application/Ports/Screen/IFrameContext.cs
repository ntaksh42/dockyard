using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>What a frame asks the workspace for while it is open.</summary>
public interface IFrameContext
{
    /// <summary>Whether a window already belongs to some group, so the picker can leave it out.</summary>
    bool IsGrouped(WindowHandle window);

    /// <summary>The saved layouts, read when a menu offering them opens.</summary>
    IReadOnlyList<SavedGroup> SavedLayouts();

    /// <summary>The current shortcut bindings, listed in the "+" menu.</summary>
    IReadOnlyDictionary<ShortcutAction, ShortcutKey> ShortcutBindings();

    /// <summary>The saved Remote Desktop connections, read when the "+" menu offers them.</summary>
    IReadOnlyList<RemoteDesktopConnection> RemoteDesktops();

    /// <summary>
    /// Adds the connection, or replaces the one named <paramref name="previousName"/>. A password, if given, is
    /// kept for it (an empty one forgets it); null leaves the saved password as it is.
    /// </summary>
    void SaveRemoteDesktop(RemoteDesktopConnection connection, string? previousName, string? password);

    void DeleteRemoteDesktop(string name);

    /// <summary>Opens a session window to add to a pane; <see cref="WindowHandle.None"/> (and a notice) if it cannot.</summary>
    WindowHandle OpenRemoteDesktop(RemoteDesktopConnection connection);
}
