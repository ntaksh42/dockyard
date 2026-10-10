using Dockyard.Core;

namespace Dockyard.Core.Tests;

public class ClampRatioTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 28, MinPaneWidth: 80, MinPaneHeight: 40);

    private static SplitNode TwoPanes(out LayoutTree tree)
    {
        tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        tree.Split(a, DockSide.Right, 2);
        return (SplitNode)tree.Root;
    }

    [Fact]
    public void MinimumsExactlyFillTheSpace_DoesNotThrow()
    {
        // Crash seen in the field: min1/available ended up a hair above 1 - min2/available.
        var split = TwoPanes(out _);
        double min1 = 401.33333333333331, min2 = 805.6666666666666;
        var length = min1 + min2 + M.SplitterThickness;

        var ratio = LayoutEngine.ClampRatio(split, 0.3, length, M, w => w == 1 ? new Size(min1, 0) : new Size(min2, 0));

        Assert.Equal(min1 / (min1 + min2), ratio, 9);
    }

    [Fact]
    public void ManyNearlyTightCombinations_NeverThrowAndStayInRange()
    {
        var split = TwoPanes(out _);
        var rng = new Random(1234);
        for (var i = 0; i < 20000; i++)
        {
            var min1 = 80 + rng.NextDouble() * 900;
            var min2 = 80 + rng.NextDouble() * 900;
            // Lengths around the exact fit, where rounding bites.
            var length = min1 + min2 + M.SplitterThickness + (rng.NextDouble() - 0.5) * 1e-9 * (i % 3);
            var requested = rng.NextDouble() * 1.4 - 0.2;

            var ratio = LayoutEngine.ClampRatio(split, requested, length, M, w => w == 1 ? new Size(min1, 0) : new Size(min2, 0));

            Assert.InRange(ratio, 0, 1);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteRequest_KeepsCurrentRatio(double requested)
    {
        var split = TwoPanes(out _);
        split.Ratio = 0.4;
        var ratio = LayoutEngine.ClampRatio(split, requested, 1000, M, _ => new Size(0, 0));
        Assert.Equal(0.4, ratio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void NoRoom_KeepsCurrentRatio(double length)
    {
        var split = TwoPanes(out _);
        split.Ratio = 0.4;
        Assert.Equal(0.4, LayoutEngine.ClampRatio(split, 0.9, length, M, _ => new Size(0, 0)));
    }

    [Fact]
    public void ClampAll_OnTinyBounds_DoesNotThrow()
    {
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, 1);
        var b = tree.Split(a, DockSide.Right, 2);
        tree.Split(b, DockSide.Bottom, 3);

        foreach (var w in new[] { 0.0, 3, 4, 5, 100, 1000 })
            LayoutEngine.ClampAll(tree.Root, new Rect(0, 0, w, w), M, _ => new Size(300, 300));
    }
}
