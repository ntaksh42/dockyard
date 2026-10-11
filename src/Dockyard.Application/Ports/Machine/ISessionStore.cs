namespace Dockyard.Application;

/// <summary>What survives Dockyard going away: the open groups, kept to put windows back or reopen them.</summary>
public interface ISessionStore
{
    /// <summary>
    /// The open groups with their exact windows, kept current while Dockyard runs and removed when it puts the
    /// windows back. Finding it at startup means Dockyard went down without doing so.
    /// </summary>
    List<SavedGroup>? LoadSession();
    void SaveSession(IReadOnlyList<SavedGroup> groups);
    void ClearSession();

    /// <summary>The groups open at the last exit, kept like saved layouts to open them again at startup.</summary>
    List<SavedGroup>? LoadLastGroups();
    void SaveLastGroups(IReadOnlyList<SavedGroup> groups);
}
