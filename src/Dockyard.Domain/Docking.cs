namespace Dockyard.Domain;

public sealed record DockTarget(PaneLayout Layout, DockSide? Side, Rect Preview);

public static class Docking
{
    public static DockTarget? HitTest(LayoutResult layout, double x, double y)
    {
        var pane = layout.Panes.FirstOrDefault(p => Contains(p.Bounds, x, y));
        if (pane is null) return null;
        var r = pane.Bounds;
        DockSide? side = null;
        // The tab bar always accepts tabs. Content edges split, the middle adds a tab.
        if (!Contains(pane.TabBar, x, y) && pane.Pane.Windows.Count > 0)
        {
            var distances = new[] { (x - r.X) / r.Width, (r.X + r.Width - x) / r.Width,
                (y - pane.Content.Y) / pane.Content.Height, (r.Y + r.Height - y) / pane.Content.Height };
            var nearest = Array.IndexOf(distances, distances.Min());
            if (distances[nearest] < .25) side = (DockSide)nearest;
        }
        var preview = side switch
        {
            DockSide.Left => r with { Width = r.Width / 2 },
            DockSide.Right => r with { X = r.X + r.Width / 2, Width = r.Width / 2 },
            DockSide.Top => r with { Height = r.Height / 2 },
            DockSide.Bottom => r with { Y = r.Y + r.Height / 2, Height = r.Height / 2 },
            _ => r,
        };
        return new DockTarget(pane, side, preview);
    }

    private static bool Contains(Rect r, double x, double y) =>
        x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height;
}
