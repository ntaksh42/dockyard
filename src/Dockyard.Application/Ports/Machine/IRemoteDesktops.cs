using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>
/// Remote Desktop sessions that Dockyard opens itself. Each one is a top-level window without a title bar or
/// border, which a group takes like any other window: it fills its pane, and the remote screen follows the
/// pane's size. The window reports closing and title changes through <see cref="IWindowEventSource"/>.
/// </summary>
public interface IRemoteDesktops
{
    /// <summary>
    /// Opens a window that connects to the computer once it has been placed. Returns
    /// <see cref="WindowHandle.None"/> if Remote Desktop is not available on this PC.
    /// </summary>
    WindowHandle Open(RemoteDesktopConnection connection);

    /// <summary>The connection the window shows, or null for any other window.</summary>
    RemoteDesktopConnection? ConnectionOf(WindowHandle window);

    /// <summary>Disconnects and closes the window.</summary>
    void Close(WindowHandle window);

    /// <summary>Keeps the password for the connection of that name; null or empty forgets it.</summary>
    void SavePassword(string connectionName, string? password);

    /// <summary>Keeps the password of a renamed connection under its new name.</summary>
    void RenamePassword(string oldName, string newName);
}
