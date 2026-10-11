namespace Dockyard.Domain.Tests;

public class NavigationTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 20);

    /// <summary>A | (B / C): A on the left, B above C on the right.</summary>
    private static (LayoutResult Layout, Pane A, Pane B, Pane C) Build()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, new WindowHandle(1));
        var b = tree.Split(a, DockSide.Right, new WindowHandle(2));
        var c = tree.Split(b, DockSide.Bottom, new WindowHandle(3));
        return (LayoutEngine.Arrange(tree.Root, new Rect(0, 0, 404, 404), M), a, b, c);
    }

    [Fact]
    public void FindNeighbor_FollowsTheSplits()
    {
        var (layout, a, b, c) = Build();
        Assert.Equal(b, Navigation.FindNeighbor(layout, a, DockSide.Right)?.Pane);
        Assert.Equal(a, Navigation.FindNeighbor(layout, b, DockSide.Left)?.Pane);
        Assert.Equal(a, Navigation.FindNeighbor(layout, c, DockSide.Left)?.Pane);
        Assert.Equal(c, Navigation.FindNeighbor(layout, b, DockSide.Bottom)?.Pane);
        Assert.Equal(b, Navigation.FindNeighbor(layout, c, DockSide.Top)?.Pane);
    }

    [Fact]
    public void FindNeighbor_ReturnsNullAtTheBorder()
    {
        var (layout, a, b, _) = Build();
        Assert.Null(Navigation.FindNeighbor(layout, a, DockSide.Left));
        Assert.Null(Navigation.FindNeighbor(layout, a, DockSide.Top));
        Assert.Null(Navigation.FindNeighbor(layout, b, DockSide.Right));
    }
}
