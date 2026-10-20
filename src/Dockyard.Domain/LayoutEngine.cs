namespace Dockyard.Domain;

/// <summary>Where each pane's tabs go (Q2).</summary>
public enum TabBarPlacement
{
    /// <summary>A. A tab row above the content.</summary>
    Top,
    /// <summary>B. Over the top of the content, where the app's own title bar is.</summary>
    TitleBar,
    /// <summary>C. A tab column left of the content.</summary>
    Left,
}

public sealed record LayoutMetrics(double SplitterThickness = 4, double TabBarHeight = 24, double MinPaneWidth = 80, double MinPaneHeight = 40,
    TabBarPlacement TabBarPlacement = TabBarPlacement.Top, double TabColumnWidth = 160)
{
    /// <summary>
    /// Panes whose windows draw controls in their own title bar (tabs, menus), which placement B would cover:
    /// those panes put their tabs above the content instead.
    /// </summary>
    public Func<Pane, bool>? KeepsTitleBar { get; init; }

    public TabBarPlacement PlacementOf(Pane pane) =>
        TabBarPlacement == TabBarPlacement.TitleBar && KeepsTitleBar?.Invoke(pane) == true ? TabBarPlacement.Top : TabBarPlacement;
}

public sealed record PaneLayout(Pane Pane, Rect Bounds, Rect TabBar, Rect Content, TabBarPlacement Placement = TabBarPlacement.Top);

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
                var column = Math.Min(m.TabColumnWidth, r.Width);
                var placement = m.PlacementOf(pane);
                panes.Add(placement switch
                {
                    TabBarPlacement.TitleBar => new PaneLayout(pane, r, r with { Height = tab }, r, placement),
                    TabBarPlacement.Left => new PaneLayout(pane, r, r with { Width = column }, r with { X = r.X + column, Width = r.Width - column }, placement),
                    _ => new PaneLayout(pane, r, r with { Height = tab }, r with { Y = r.Y + tab, Height = r.Height - tab }, placement),
                });
                break;
            case SplitNode s when s.Orientation == Orientation.Horizontal:
            {
                // A split narrower than a splitter gives the splitter what there is, so nothing spills outside.
                var t = Math.Min(m.SplitterThickness, r.Width);
                var w = r.Width - t;
                var w1 = Math.Min(w, Math.Round(w * Math.Clamp(s.Ratio, 0, 1)));
                Arrange(s.First, r with { Width = w1 }, m, panes, splitters);
                splitters.Add(new SplitterLayout(s, r with { X = r.X + w1, Width = t }, r));
                Arrange(s.Second, r with { X = r.X + w1 + t, Width = w - w1 }, m, panes, splitters);
                break;
            }
            case SplitNode s:
            {
                var t = Math.Min(m.SplitterThickness, r.Height);
                var h = r.Height - t;
                var h1 = Math.Min(h, Math.Round(h * Math.Clamp(s.Ratio, 0, 1)));
                Arrange(s.First, r with { Height = h1 }, m, panes, splitters);
                splitters.Add(new SplitterLayout(s, r with { Y = r.Y + h1, Height = t }, r));
                Arrange(s.Second, r with { Y = r.Y + h1 + t, Height = h - h1 }, m, panes, splitters);
                break;
            }
        }
    }

    /// <summary>Smallest size the node can take. <paramref name="windowMinSize"/> gives each window's minimum content size.</summary>
    public static Size MinSize(LayoutNode node, LayoutMetrics m, Func<WindowHandle, Size> windowMinSize)
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
                return m.PlacementOf(pane) switch
                {
                    TabBarPlacement.TitleBar => new Size(w, Math.Max(h, m.TabBarHeight)),
                    TabBarPlacement.Left => new Size(w + m.TabColumnWidth, h),
                    _ => new Size(w, h + m.TabBarHeight),
                };
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
    public static double ClampRatio(SplitNode split, double ratio, double length, LayoutMetrics m, Func<WindowHandle, Size> windowMinSize)
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
    public static void ClampAll(LayoutNode node, Rect bounds, LayoutMetrics m, Func<WindowHandle, Size> windowMinSize)
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
