namespace Dockyard.Application;

/// <summary>Shortcut files (.lnk) that start Dockyard with a saved layout; see <see cref="LaunchCommand"/>.</summary>
public interface ILayoutShortcuts
{
    /// <summary>Writes the shortcut on the desktop, replacing one for the same layout. Returns its path, or null if it could not be written.</summary>
    string? CreateOnDesktop(string layoutName);
}
