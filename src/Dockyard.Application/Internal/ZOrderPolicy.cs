
namespace Dockyard.Application.Internal;

internal static class ZOrderPolicy
{
    /// <summary>
    /// Whether the active tabs should move along with the frame. Bottom, topmost and no-topmost placements
    /// and placements relative to a member are left alone; following them would push the tabs to the bottom
    /// or reorder them against each other.
    /// </summary>
    public static bool ShouldCarryTabs(FrameZChange change, IReadOnlyCollection<nint> members) =>
        !change.Hiding
        && members.Count > 0
        && (change.Placement == ZPlacement.Top || (change.Placement == ZPlacement.Below && !members.Contains(change.Below)));
}
