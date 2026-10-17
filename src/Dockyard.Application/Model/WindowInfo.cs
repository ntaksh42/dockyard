using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>An app window the user could add to a group.</summary>
/// <param name="Controllable">False for windows Windows will not let this process move, such as elevated apps.</param>
public sealed record WindowInfo(WindowHandle Handle, string Title, ProcessName ProcessName, bool Controllable)
{
    /// <summary>
    /// Whether the window matches what the user typed to narrow the picker: every word, in any order, in the
    /// title or the exe name, ignoring case. Nothing typed matches every window.
    /// </summary>
    public bool Matches(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(word =>
            Title.Contains(word, StringComparison.OrdinalIgnoreCase) || ProcessName.Value.Contains(word, StringComparison.OrdinalIgnoreCase));

    public override string ToString() =>
        Controllable ? $"{Title}  —  {ProcessName}" : $"{Title}  —  {ProcessName}  (can't add apps running as administrator)";
}
