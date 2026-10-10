using Dockyard.Domain;

namespace Dockyard.Domain.Tests;

public class DockingTests
{
    private readonly LayoutTree _tree = new();
    private LayoutResult Layout => LayoutEngine.Arrange(_tree.Root, new Rect(0, 0, 400, 328), new());

    [Theory]
    [InlineData(10, 178, DockSide.Left)]
    [InlineData(390, 178, DockSide.Right)]
    [InlineData(200, 40, DockSide.Top)]
    [InlineData(200, 320, DockSide.Bottom)]
    public void ContentEdgesSplit(double x, double y, DockSide side)
    {
        _tree.AddTab((Pane)_tree.Root, 1);
        var target = Docking.HitTest(Layout, x, y)!;
        Assert.Equal(side, target.Side);
        Assert.Equal(side is DockSide.Left or DockSide.Right ? 200 : 400, target.Preview.Width);
        Assert.Equal(side is DockSide.Top or DockSide.Bottom ? 164 : 328, target.Preview.Height);
    }

    [Theory]
    [InlineData(200, 178)]
    [InlineData(10, 10)]
    public void CenterAndTabBarAddTabs(double x, double y)
    {
        _tree.AddTab((Pane)_tree.Root, 1);
        var target = Docking.HitTest(Layout, x, y)!;
        Assert.Null(target.Side);
        Assert.Equal(Layout.Panes[0].Bounds, target.Preview);
    }

    [Fact]
    public void EmptyPaneAddsTabEvenAtEdge() => Assert.Null(Docking.HitTest(Layout, 1, 100)!.Side);

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(400, 100)]
    [InlineData(100, 328)]
    public void OutsideDoesNotDock(double x, double y) => Assert.Null(Docking.HitTest(Layout, x, y));

    [Fact]
    public void SplitterDoesNotDock()
    {
        _tree.AddTab((Pane)_tree.Root, 1);
        _tree.Split((Pane)_tree.Root, DockSide.Right, 2);
        Assert.Null(Docking.HitTest(Layout, 200, 100));
    }
}
