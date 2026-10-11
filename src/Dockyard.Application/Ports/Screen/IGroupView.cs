using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>The group frame as the controller sees it. Implemented by the WPF window in Presentation.</summary>
public interface IGroupView
{
    /// <summary>The frame's own window handle.</summary>
    nint Handle { get; }

    /// <summary>False while minimized, closing or not yet shown; screen mapping is not possible then.</summary>
    bool IsOnScreen { get; }

    bool IsMinimized { get; }

    /// <summary>Size of the area panes are laid out in, in the units layouts use (WPF DIPs).</summary>
    Size SurfaceSize { get; }

    /// <summary>Physical pixels per layout unit.</summary>
    double Scale { get; }

    /// <summary>0 when tabs may go to the top of the z-order; otherwise a window they must stay below, such as an open dialog.</summary>
    nint ZAnchor { get; }

    /// <summary>A window drawing tab bars over the member windows (tab bar placement B), or 0.</summary>
    nint TabOverlay { get; }

    PixelRect ToScreen(Rect surfaceRect);
    Rect FromScreen(PixelRect screenRect);

    void Render(GroupSnapshot snapshot);
    void MoveBy(double dx, double dy);
    void ResizeEdge(DockSide edge, double delta);

    /// <summary>Sets the smallest content size and grows the frame if it is currently smaller.</summary>
    void SetMinimumContentSize(Size min);

    void Minimize();
    void Restore();
}
