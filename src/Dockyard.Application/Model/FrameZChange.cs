namespace Dockyard.Application;

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
