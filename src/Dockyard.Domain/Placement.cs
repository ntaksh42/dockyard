namespace Dockyard.Domain;

/// <summary>Where a window may sit on a screen.</summary>
public static class Placement
{
    /// <summary>
    /// Returns <paramref name="bounds"/> as they are while the window's caption can still be reached on the work
    /// area; otherwise moves it (and shrinks it, if it is larger than the work area) onto the work area. A
    /// monitor that went away leaves such windows behind, where nothing can drag them back.
    /// </summary>
    /// <param name="captionHeight">How much of the top must lie inside the work area for the window to be grabbed.</param>
    /// <param name="minVisibleWidth">How much of the width must lie inside it, or all of it for a narrower window.</param>
    /// <param name="tolerance">How far above the work area the top may be, as a maximized window's frame is.</param>
    public static Rect FitToWorkArea(Rect bounds, Rect work, double captionHeight = 24, double minVisibleWidth = 120, double tolerance = 8)
    {
        var shown = Math.Min(bounds.X + bounds.Width, work.X + work.Width) - Math.Max(bounds.X, work.X);
        var reachable = bounds.Y >= work.Y - tolerance
            && bounds.Y + captionHeight <= work.Y + work.Height
            && shown >= Math.Min(minVisibleWidth, bounds.Width);
        if (reachable) return bounds;

        var width = Math.Min(bounds.Width, work.Width);
        var height = Math.Min(bounds.Height, work.Height);
        return new Rect(
            Math.Clamp(bounds.X, work.X, work.X + work.Width - width),
            Math.Clamp(bounds.Y, work.Y, work.Y + work.Height - height),
            width, height);
    }
}
