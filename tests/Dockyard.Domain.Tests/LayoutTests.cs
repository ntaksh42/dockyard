using Dockyard.Domain;

namespace Dockyard.Domain.Tests;

public class LayoutTreeTests
{
    [Fact]
    public void AddTab_ActivatesNewTab()
    {
        var tree = new LayoutTree();
        var pane = (Pane)tree.Root;
        tree.AddTab(pane, 1);
        tree.AddTab(pane, 2);
        Assert.Equal([1, 2], pane.Windows);
        Assert.Equal(2, pane.ActiveWindow);
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
        tree.AddTab(original, 1);

        var added = tree.Split(original, side, 2);

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
        tree.AddTab(a, 1);
        var b = tree.Split(a, DockSide.Right, 2);
        var c = tree.Split(b, DockSide.Bottom, 3);

        var root = Assert.IsType<SplitNode>(tree.Root);
        var inner = Assert.IsType<SplitNode>(root.Second);
        Assert.Same(root, inner.Parent);
        Assert.Same(b, inner.First);
        Assert.Same(c, inner.Second);
        Assert.Equal([1, 2, 3], tree.Windows);
    }

    [Fact]
    public void Remove_LastWindowOfPane_CollapsesSplit()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        var b = tree.Split(a, DockSide.Right, 2);
        tree.Split(b, DockSide.Bottom, 3);

        tree.Remove(2);

        var root = Assert.IsType<SplitNode>(tree.Root);
        Assert.Same(a, root.First);
        var c = Assert.IsType<Pane>(root.Second);
        Assert.Same(root, c.Parent);
        Assert.Equal([1, 3], tree.Windows);
    }

    [Fact]
    public void Remove_UntilOnePaneLeft_RootBecomesPane()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Right, 2);

        tree.Remove(2);

        Assert.Same(a, tree.Root);
        Assert.Null(a.Parent);
    }

    [Fact]
    public void Remove_LastWindowOverall_KeepsEmptyRootPane()
    {
        var tree = new LayoutTree();
        tree.AddTab((Pane)tree.Root, 1);
        tree.Remove(1);
        var root = Assert.IsType<Pane>(tree.Root);
        Assert.Empty(root.Windows);
        Assert.Null(root.ActiveWindow);
    }

    [Theory]
    [InlineData(0, 2, 2)] // removing before the active tab keeps the same window active
    [InlineData(2, 2, 2)] // removing the active last tab activates the previous one
    [InlineData(1, 0, 1)]
    public void Remove_AdjustsActiveIndex(int removeIndex, int activeIndex, nint expectedActive)
    {
        var tree = new LayoutTree();
        var pane = (Pane)tree.Root;
        nint[] windows = [1, 2, 3];
        foreach (var w in windows) tree.AddTab(pane, w);
        pane.ActiveIndex = activeIndex;

        tree.Remove(windows[removeIndex]);

        Assert.Equal(expectedActive, pane.ActiveWindow);
    }

    [Fact]
    public void FindDivider_WalksUpToMatchingSplit()
    {
        // [ a | [ b / c ] ]
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        var b = tree.Split(a, DockSide.Right, 2);
        var c = tree.Split(b, DockSide.Bottom, 3);
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
        tree.AddTab(pane, 1);
        tree.AddTab(pane, 2);
        tree.Activate(1);
        Assert.Equal(1, pane.ActiveWindow);
    }
}

public class LayoutEngineTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 20, MinPaneWidth: 50, MinPaneHeight: 30);

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
    public void Arrange_HorizontalSplit_UsesRatioAndSplitter()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Right, 2);
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
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Bottom, 2);

        var result = LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 100, 204), M);

        Assert.Equal(new Rect(0, 0, 100, 100), result.Panes[0].Bounds);
        Assert.Equal(new Rect(0, 104, 100, 100), result.Panes[1].Bounds);
    }

    [Fact]
    public void MinSize_CombinesWindowMinimumsAndSplits()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        tree.AddTab(a, 2);
        tree.Split(a, DockSide.Right, 3);
        var mins = new Dictionary<nint, Size> { [1] = new(120, 10), [2] = new(60, 90), [3] = new(0, 0) };

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
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Right, 2);
        var split = (SplitNode)tree.Root;

        var ratio = LayoutEngine.ClampRatio(split, requested, 404, M, w => w == 1 ? new Size(120, 0) : new Size(0, 0));

        Assert.Equal(expected, ratio, 6);
    }

    [Fact]
    public void ClampAll_ClampsNestedSplitsWithinTheirOwnBounds()
    {
        // [ a | [ b | c ] ], c needs 200 wide
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        var b = tree.Split(a, DockSide.Right, 2);
        tree.Split(b, DockSide.Right, 3);
        var root = (SplitNode)tree.Root;
        var inner = (SplitNode)root.Second;
        root.Ratio = 0.5;
        inner.Ratio = 0.5;

        LayoutEngine.ClampAll(tree.Root, new Rect(0, 0, 604, 100), M, w => w == 3 ? new Size(200, 0) : new Size(0, 0));

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
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Right, 2);
        var split = (SplitNode)tree.Root;

        var ratio = LayoutEngine.ClampRatio(split, 0.5, 104, M, _ => new Size(150, 0));

        Assert.Equal(0.5, ratio, 6);
    }
}
