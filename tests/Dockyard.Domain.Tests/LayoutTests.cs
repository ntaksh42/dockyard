using Dockyard.Domain;

namespace Dockyard.Domain.Tests;

public class LayoutTreeTests
{
    private static WindowHandle W(int n) => new(n);

    [Fact]
    public void AddTab_ActivatesNewTab()
    {
        var tree = new LayoutTree();
        var pane = (Pane)tree.Root;
        tree.AddTab(pane, W(1));
        tree.AddTab(pane, W(2));
        Assert.Equal([W(1), W(2)], pane.Windows);
        Assert.Equal(W(2), pane.ActiveWindow);
    }

    [Theory]
    [InlineData(DockSide.Left, Orientation.Horizontal, true)]
    [InlineData(DockSide.Right, Orientation.Horizontal, false)]
    [InlineData(DockSide.Top, Orientation.Vertical, true)]
    [InlineData(DockSide.Bottom, Orientation.Vertical, false)]
    public void Split_PlacesNewPaneOnSide(DockSide side, Orientation orientation, bool newIsFirst)
    {
        var tree = new LayoutTree();
        var original = (Pane)tree.Root;
        tree.AddTab(original, W(1));

        var added = tree.Split(original, side, W(2));

        var split = Assert.IsType<SplitNode>(tree.Root);
        Assert.Equal(orientation, split.Orientation);
        Assert.Same(newIsFirst ? added : original, split.First);
        Assert.Same(newIsFirst ? original : added, split.Second);
        Assert.Same(split, added.Parent);
        Assert.Same(split, original.Parent);
        Assert.Null(split.Parent);
    }

    [Fact]
    public void Split_NestedPane_ReplacesItInParent()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var b = tree.Split(a, DockSide.Right, W(2));
        var c = tree.Split(b, DockSide.Bottom, W(3));

        var root = Assert.IsType<SplitNode>(tree.Root);
        var inner = Assert.IsType<SplitNode>(root.Second);
        Assert.Same(root, inner.Parent);
        Assert.Same(b, inner.First);
        Assert.Same(c, inner.Second);
        Assert.Equal([W(1), W(2), W(3)], tree.Windows);
    }

    [Fact]
    public void Remove_LastWindowOfPane_CollapsesSplit()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var b = tree.Split(a, DockSide.Right, W(2));
        tree.Split(b, DockSide.Bottom, W(3));

        tree.Remove(W(2));

        var root = Assert.IsType<SplitNode>(tree.Root);
        Assert.Same(a, root.First);
        var c = Assert.IsType<Pane>(root.Second);
        Assert.Same(root, c.Parent);
        Assert.Equal([W(1), W(3)], tree.Windows);
    }

    [Fact]
    public void Remove_UntilOnePaneLeft_RootBecomesPane()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, DockSide.Right, W(2));

        tree.Remove(W(2));

        Assert.Same(a, tree.Root);
        Assert.Null(a.Parent);
    }

    [Fact]
    public void Remove_LastWindowOverall_KeepsEmptyRootPane()
    {
        var tree = new LayoutTree();
        tree.AddTab((Pane)tree.Root, W(1));
        tree.Remove(W(1));
        var root = Assert.IsType<Pane>(tree.Root);
        Assert.Empty(root.Windows);
        Assert.Null(root.ActiveWindow);
    }

    [Theory]
    [InlineData(0, 2, 3)] // removing before the active tab keeps the same window active
    [InlineData(2, 2, 2)] // removing the active last tab activates the previous one
    [InlineData(1, 0, 1)]
    public void Remove_AdjustsActiveIndex(int removeIndex, int activeIndex, int expectedActive)
    {
        var tree = new LayoutTree();
        var pane = (Pane)tree.Root;
        WindowHandle[] windows = [W(1), W(2), W(3)];
        foreach (var w in windows) tree.AddTab(pane, w);
        pane.ActiveIndex = activeIndex;

        tree.Remove(windows[removeIndex]);

        Assert.Equal(W(expectedActive), pane.ActiveWindow);
    }

    [Fact]
    public void FindDivider_WalksUpToMatchingSplit()
    {
        // [ a | [ b / c ] ]
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var b = tree.Split(a, DockSide.Right, W(2));
        var c = tree.Split(b, DockSide.Bottom, W(3));
        var root = (SplitNode)tree.Root;
        var inner = (SplitNode)root.Second;

        Assert.Same(root, LayoutTree.FindDivider(a, DockSide.Right));
        Assert.Null(LayoutTree.FindDivider(a, DockSide.Left));
        Assert.Null(LayoutTree.FindDivider(a, DockSide.Top));
        Assert.Same(root, LayoutTree.FindDivider(c, DockSide.Left)); // c's left edge is the root divider, through the vertical split
        Assert.Null(LayoutTree.FindDivider(c, DockSide.Right));
        Assert.Same(inner, LayoutTree.FindDivider(b, DockSide.Bottom));
        Assert.Same(inner, LayoutTree.FindDivider(c, DockSide.Top));
        Assert.Null(LayoutTree.FindDivider(c, DockSide.Bottom));
    }

    [Fact]
    public void Activate_SetsActiveIndex()
    {
        var tree = new LayoutTree();
        var pane = (Pane)tree.Root;
        tree.AddTab(pane, W(1));
        tree.AddTab(pane, W(2));
        tree.Activate(W(1));
        Assert.Equal(W(1), pane.ActiveWindow);
    }
}

public class LayoutEngineTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 20, MinPaneWidth: 50, MinPaneHeight: 30);

    private static WindowHandle W(int n) => new(n);

    [Fact]
    public void Arrange_SinglePane_SplitsTabBarAndContent()
    {
        var tree = new LayoutTree();
        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 200, 100), M);
        var p = Assert.Single(result.Panes);
        Assert.Equal(new Rect(0, 0, 200, 20), p.TabBar);
        Assert.Equal(new Rect(0, 20, 200, 80), p.Content);
        Assert.Empty(result.Splitters);
    }

    [Fact]
    public void Arrange_TitleBarPlacement_OverlaysTabsOnContent()
    {
        var m = M with { TabBarPlacement = TabBarPlacement.TitleBar };
        var p = Assert.Single(LayoutEngine.Arrange(new LayoutTree().Root, new Rect(0, 0, 200, 100), m).Panes);
        Assert.Equal(new Rect(0, 0, 200, 20), p.TabBar);
        Assert.Equal(new Rect(0, 0, 200, 100), p.Content);
    }

    [Fact]
    public void Arrange_LeftPlacement_PutsTabColumnBesideContent()
    {
        var m = M with { TabBarPlacement = TabBarPlacement.Left, TabColumnWidth = 60 };
        var p = Assert.Single(LayoutEngine.Arrange(new LayoutTree().Root, new Rect(0, 0, 200, 100), m).Panes);
        Assert.Equal(new Rect(0, 0, 60, 100), p.TabBar);
        Assert.Equal(new Rect(60, 0, 140, 100), p.Content);
    }

    [Fact]
    public void TitleBarPlacement_PanesThatKeepTheirTitleBarGetTabsAbove()
    {
        var tree = new LayoutTree();
        tree.AddTab((Pane)tree.Root, W(1));
        tree.Split((Pane)tree.Root, DockSide.Right, W(2));
        var m = M with { TabBarPlacement = TabBarPlacement.TitleBar, KeepsTitleBar = p => p.Windows.Contains(W(2)) };

        var panes = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 204, 100), m).Panes;

        Assert.Equal([TabBarPlacement.TitleBar, TabBarPlacement.Top], panes.Select(p => p.Placement));
        Assert.Equal(panes[0].Bounds, panes[0].Content);
        Assert.Equal(panes[1].Bounds.Y + 20, panes[1].Content.Y);
        Assert.Equal(new Size(90 + 4 + 90, 120), LayoutEngine.MinSize(tree.Root, m, _ => new Size(90, 100)));
        // Only placement B is affected.
        Assert.Equal(TabBarPlacement.Left, (m with { TabBarPlacement = TabBarPlacement.Left }).PlacementOf((Pane)tree.Panes.Last()));
    }

    [Theory]
    [InlineData(TabBarPlacement.Top, 90, 120)]
    [InlineData(TabBarPlacement.TitleBar, 90, 100)]
    [InlineData(TabBarPlacement.Left, 150, 100)]
    public void MinSize_ReservesRoomForTabsByPlacement(TabBarPlacement placement, double width, double height)
    {
        var tree = new LayoutTree();
        tree.AddTab((Pane)tree.Root, W(1));
        var m = M with { TabBarPlacement = placement, TabColumnWidth = 60 };
        Assert.Equal(new Size(width, height), LayoutEngine.MinSize(tree.Root, m, _ => new Size(90, 100)));
    }

    [Fact]
    public void Arrange_HorizontalSplit_UsesRatioAndSplitter()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, DockSide.Right, W(2));
        ((SplitNode)tree.Root).Ratio = 0.25;

        var result = LayoutEngine.Arrange(tree.Root, new Rect(10, 0, 404, 100), M);

        Assert.Equal(new Rect(10, 0, 100, 100), result.Panes[0].Bounds);
        Assert.Equal(new Rect(110, 0, 4, 100), Assert.Single(result.Splitters).Bounds);
        Assert.Equal(new Rect(114, 0, 300, 100), result.Panes[1].Bounds);
    }

    [Fact]
    public void Arrange_VerticalSplit_StacksPanes()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, DockSide.Bottom, W(2));

        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 100, 204), M);

        Assert.Equal(new Rect(0, 0, 100, 100), result.Panes[0].Bounds);
        Assert.Equal(new Rect(0, 104, 100, 100), result.Panes[1].Bounds);
    }

    [Theory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void Arrange_FractionalSpace_DoesNotRoundPastTheAvailableSize(Orientation orientation)
    {
        var split = new SplitNode(orientation, new Pane(), new Pane()) { Ratio = 1 };
        var bounds = new Rect(10, 20, 4.75, 4.75);

        LayoutResult result = LayoutEngine.Arrange(split, bounds, M);

        foreach (Rect piece in result.Panes.SelectMany(p => new[] { p.Bounds, p.TabBar, p.Content })
            .Concat(result.Splitters.Select(s => s.Bounds)))
        {
            Assert.InRange(piece.Width, 0, bounds.Width);
            Assert.InRange(piece.Height, 0, bounds.Height);
            Assert.InRange(piece.X + piece.Width, bounds.X, bounds.X + bounds.Width);
            Assert.InRange(piece.Y + piece.Height, bounds.Y, bounds.Y + bounds.Height);
        }
        Assert.Equal(bounds.Width * bounds.Height,
            result.Panes.Sum(p => p.Bounds.Width * p.Bounds.Height)
            + result.Splitters.Sum(s => s.Bounds.Width * s.Bounds.Height), 9);
    }

    [Fact]
    public void MinSize_CombinesWindowMinimumsAndSplits()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.AddTab(a, W(2));
        tree.Split(a, DockSide.Right, W(3));
        var mins = new Dictionary<WindowHandle, Size> { [W(1)] = new(120, 10), [W(2)] = new(60, 90), [W(3)] = new(0, 0) };

        var size = LayoutEngine.MinSize(tree.Root, M, w => mins[w]);

        // left pane: 120 x (90 + 20), right pane: default 50 x (30 + 20)
        Assert.Equal(new Size(120 + 4 + 50, 110), size);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.05, 0.3)]  // first side min 120 of 400 available
    [InlineData(0.95, 0.875)] // second side min 50 of 400 available
    public void ClampRatio_StopsAtMinimums(double requested, double expected)
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, DockSide.Right, W(2));
        var split = (SplitNode)tree.Root;

        var ratio = LayoutEngine.ClampRatio(split, requested, 404, M, w => w == W(1) ?new Size(120, 0) : new Size(0, 0));

        Assert.Equal(expected, ratio, 6);
    }

    [Fact]
    public void ClampAll_ClampsNestedSplitsWithinTheirOwnBounds()
    {
        // [ a | [ b | c ] ], c needs 200 wide
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var b = tree.Split(a, DockSide.Right, W(2));
        tree.Split(b, DockSide.Right, W(3));
        var root = (SplitNode)tree.Root;
        var inner = (SplitNode)root.Second;
        root.Ratio = 0.5;
        inner.Ratio = 0.5;

        LayoutEngine.ClampAll(tree.Root, new Rect(0, 0, 604, 100), M, w => w == W(3) ?new Size(200, 0) : new Size(0, 0));

        // inner needs 50 + 4 + 200 = 254, so root gives it at least that; inner then leaves c its 200.
        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 604, 100), M);
        Assert.True(result.Panes[2].Bounds.Width >= 200);
        Assert.True(result.Panes[1].Bounds.Width >= 50);
        Assert.Equal(300, result.Panes[0].Bounds.Width); // root was already fine at 0.5
    }

    [Fact]
    public void ClampRatio_WhenBothMinimumsDoNotFit_SharesProportionally()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, DockSide.Right, W(2));
        var split = (SplitNode)tree.Root;

        var ratio = LayoutEngine.ClampRatio(split, 0.5, 104, M, _ => new Size(150, 0));

        Assert.Equal(0.5, ratio, 6);
    }
}

public class LayoutEdgeCaseTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 20, MinPaneWidth: 50, MinPaneHeight: 30);

    private static WindowHandle W(int n) => new(n);

    private static LayoutTree TwoPanes(DockSide side)
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        tree.Split(a, side, W(2));
        return tree;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Arrange_SplitShorterThanSplitter_StaysInsideItsBounds(double length)
    {
        // Regression: the splitter used to keep its full thickness and spill outside (e.g. a 0x0 frame at startup).
        var result = LayoutEngine.Arrange(TwoPanes(DockSide.Right).Root, new Rect(10, 0, length, 100), M);
        var s = Assert.Single(result.Splitters).Bounds;
        Assert.Equal(length, s.Width);
        Assert.All(result.Panes, p => Assert.True(p.Bounds.X >= 10 && p.Bounds.X + p.Bounds.Width <= 10 + length));
    }

    [Fact]
    public void Arrange_NestedSplitInZeroWidthPane_DoesNotOverlapParentSplitter()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var b = tree.Split(a, DockSide.Right, W(2));
        tree.Split(a, DockSide.Right, W(3));
        ((SplitNode)tree.Root).Ratio = 0; // the nested split gets no width at all

        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 300, 100), M);

        var outer = result.Splitters.Single(s => s.Split == tree.Root).Bounds;
        Assert.All(result.Splitters.Where(s => s.Split != tree.Root), s => Assert.True(s.Bounds.X + s.Bounds.Width <= outer.X));
        Assert.Equal(new Rect(4, 0, 296, 100), result.Panes.Single(p => p.Pane == b).Bounds);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.7, 400)]
    public void Arrange_RatioOutsideZeroToOne_IsTreatedAsTheNearestEnd(double ratio, double firstWidth)
    {
        var tree = TwoPanes(DockSide.Right);
        ((SplitNode)tree.Root).Ratio = ratio;
        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 404, 100), M);
        Assert.Equal(firstWidth, result.Panes[0].Bounds.Width);
        Assert.Equal(400 - firstWidth, result.Panes[1].Bounds.Width);
    }

    [Fact]
    public void Arrange_PaneShorterThanTabBar_GetsNoContentArea()
    {
        var result = LayoutEngine.Arrange(new LayoutTree().Root, new Rect(0, 0, 100, 12), M);
        var p = Assert.Single(result.Panes);
        Assert.Equal(12, p.TabBar.Height);
        Assert.Equal(0, p.Content.Height);
    }

    [Fact]
    public void MinSize_VerticalSplit_StacksHeights()
    {
        var tree = TwoPanes(DockSide.Bottom);
        var size = LayoutEngine.MinSize(tree.Root, M, w => w == W(1) ?new Size(200, 100) : new Size(0, 0));
        Assert.Equal(new Size(200, (100 + 20) + 4 + (30 + 20)), size);
    }

    [Fact]
    public void ClampRatio_VerticalSplit_UsesHeights()
    {
        var tree = TwoPanes(DockSide.Bottom);
        var ratio = LayoutEngine.ClampRatio((SplitNode)tree.Root, 0.01, 504, M, w => w == W(1) ?new Size(0, 180) : new Size(0, 0));
        Assert.Equal(200.0 / 500, ratio, 6); // 180 content + 20 tab bar of 500 available
    }

    [Fact]
    public void RemoveOrActivate_UnknownWindow_Throws()
    {
        var tree = new LayoutTree();
        Assert.Throws<ArgumentException>(() => tree.Remove(W(9)));
        Assert.Throws<ArgumentException>(() => tree.Activate(W(9)));
    }
}
