namespace Dockyard.Core;

public sealed record LayoutMetrics(double SplitterThickness = 4, double TabBarHeight = 28, double MinPaneWidth = 80, double MinPaneHeight = 40);

public sealed record PaneLayout(Pane Pane, Rect Bounds, Rect TabBar, Rect Content);

/// <param name="SplitBounds">Area covered by the whole split, used to turn a pointer position into a ratio.</param>
public sealed record SplitterLayout(SplitNode Split, Rect Bounds, Rect SplitBounds);

public sealed record LayoutResult(IReadOnlyList<PaneLayout> Panes, IReadOnlyList<SplitterLayout> Splitters);

/// <summary>Pure geometry for the layout tree. All values are in the same unit (WPF DIPs in the app).</summary>
public static class LayoutEngine
{
    public static LayoutResult Arrange(LayoutNode root, Rect bounds, LayoutMetrics m)
    {
        var panes = new List<PaneLayout>();
        var splitters = new List<SplitterLayout>();
        Arrange(root, bounds, m, panes, splitters);
        return new LayoutResult(panes, splitters);
    }

    private static void Arrange(LayoutNode node, Rect r, LayoutMetrics m, List<PaneLayout> panes, List<SplitterLayout> splitters)
    {
        switch (node)
        {
            case Pane pane:
                var tab = Math.Min(m.TabBarHeight, r.Height);
                panes.Add(new PaneLayout(pane, r, r with { Height = tab }, r with { Y = r.Y + tab, Height = r.Height - tab }));
                break;
            case SplitNode s when s.Orientation == Orientation.Horizontal:
                var w = Math.Max(0, r.Width - m.SplitterThickness);
                var w1 = Math.Round(w * s.Ratio);
                Arrange(s.First, r with { Width = w1 }, m, panes, splitters);
                splitters.Add(new SplitterLayout(s, r with { X = r.X + w1, Width = m.SplitterThickness }, r));
                Arrange(s.Second, r with { X = r.X + w1 + m.SplitterThickness, Width = w - w1 }, m, panes, splitters);
                break;
            case SplitNode s:
                var h = Math.Max(0, r.Height - m.SplitterThickness);
                var h1 = Math.Round(h * s.Ratio);
                Arrange(s.First, r with { Height = h1 }, m, panes, splitters);
                splitters.Add(new SplitterLayout(s, r with { Y = r.Y + h1, Height = m.SplitterThickness }, r));
                Arrange(s.Second, r with { Y = r.Y + h1 + m.SplitterThickness, Height = h - h1 }, m, panes, splitters);
                break;
        }
    }

    /// <summary>Smallest size the node can take. <paramref name="windowMinSize"/> gives each window's minimum content size.</summary>
    public static Size MinSize(LayoutNode node, LayoutMetrics m, Func<nint, Size> windowMinSize)
    {
        switch (node)
        {
            case Pane pane:
                double w = m.MinPaneWidth, h = m.MinPaneHeight;
                foreach (var win in pane.Windows)
                {
                    var s = windowMinSize(win);
                    w = Math.Max(w, s.Width);
                    h = Math.Max(h, s.Height);
                }
                return new Size(w, h + m.TabBarHeight);
            case SplitNode split:
                var a = MinSize(split.First, m, windowMinSize);
                var b = MinSize(split.Second, m, windowMinSize);
                return split.Orientation == Orientation.Horizontal
                    ? new Size(a.Width + m.SplitterThickness + b.Width, Math.Max(a.Height, b.Height))
                    : new Size(Math.Max(a.Width, b.Width), a.Height + m.SplitterThickness + b.Height);
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Clamps a splitter ratio so neither side goes below its minimum size (Q9: the splitter stops).
    /// <paramref name="length"/> is the split's full length along its orientation, including the splitter.
    /// </summary>
    public static double ClampRatio(SplitNode split, double ratio, double length, LayoutMetrics m, Func<nint, Size> windowMinSize)
    {
        var available = length - m.SplitterThickness;
        if (!double.IsFinite(ratio) || !(available > 0)) return split.Ratio;
        var horizontal = split.Orientation == Orientation.Horizontal;
        var min1 = Along(MinSize(split.First, m, windowMinSize), horizontal);
        var min2 = Along(MinSize(split.Second, m, windowMinSize), horizontal);
        var lo = min1 / available;
        var hi = 1 - min2 / available;
        // When the minimums (nearly) fill the space, rounding can put lo a hair above hi; share proportionally.
        if (lo >= hi) return min1 / (min1 + min2);
        return Math.Clamp(ratio, lo, hi);
    }

    /// <summary>Re-clamps every split ratio top-down so each side fits its minimum within <paramref name="bounds"/>.</summary>
    public static void ClampAll(LayoutNode node, Rect bounds, LayoutMetrics m, Func<nint, Size> windowMinSize)
    {
        if (node is not SplitNode s) return;
        var horizontal = s.Orientation == Orientation.Horizontal;
        var length = horizontal ? bounds.Width : bounds.Height;
        s.Ratio = ClampRatio(s, s.Ratio, length, m, windowMinSize);
        var first = Math.Round(Math.Max(0, length - m.SplitterThickness) * s.Ratio);
        var rest = Math.Max(0, length - m.SplitterThickness) - first;
        if (horizontal)
        {
            ClampAll(s.First, bounds with { Width = first }, m, windowMinSize);
            ClampAll(s.Second, bounds with { X = bounds.X + first + m.SplitterThickness, Width = rest }, m, windowMinSize);
        }
        else
        {
            ClampAll(s.First, bounds with { Height = first }, m, windowMinSize);
            ClampAll(s.Second, bounds with { Y = bounds.Y + first + m.SplitterThickness, Height = rest }, m, windowMinSize);
        }
    }

    private static double Along(Size s, bool horizontal) => horizontal ? s.Width : s.Height;
}
