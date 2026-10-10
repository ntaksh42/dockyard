namespace Dockyard.App.Controllers;

public enum ZPlacement
{
    /// <summary>To the top of the z-order.</summary>
    Top,
    /// <summary>Just below a given window.</summary>
    Below,
    Bottom,
    Topmost,
    NoTopmost,
}

/// <summary>A z-order change the system is about to make to the frame.</summary>
public readonly record struct FrameZChange(ZPlacement Placement, nint Below = 0, bool Hiding = false);

public static class ZOrderPolicy
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
