namespace Dockyard.Domain;

public static class Navigation
{
    /// <summary>
    /// The pane next to <paramref name="from"/> on <paramref name="side"/>: the nearest one lying wholly on that
    /// side and overlapping it across, preferring the larger overlap. Null at the outer border.
    /// </summary>
    public static PaneLayout? FindNeighbor(LayoutResult layout, Pane from, DockSide side)
    {
        var origin = layout.Panes.First(p => p.Pane == from).Bounds;
        return layout.Panes
            .Where(p => p.Pane != from)
            .Select(p => (Pane: p, Gap: Gap(origin, p.Bounds, side), Overlap: Overlap(origin, p.Bounds, side)))
            .Where(c => c.Gap >= 0 && c.Overlap > 0)
            .OrderBy(c => c.Gap)
            .ThenByDescending(c => c.Overlap)
            .Select(c => c.Pane)
            .FirstOrDefault();
    }

    private static double Gap(Rect from, Rect to, DockSide side) => side switch
    {
        DockSide.Left => from.X - (to.X + to.Width),
        DockSide.Right => to.X - (from.X + from.Width),
        DockSide.Top => from.Y - (to.Y + to.Height),
        _ => to.Y - (from.Y + from.Height),
    };

    private static double Overlap(Rect a, Rect b, DockSide side) => side is DockSide.Left or DockSide.Right
        ? Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y)
        : Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
}
