using Dockyard.Domain;

namespace Dockyard.Domain.Tests;

/// <summary>
/// Random sequences of layout operations, checked against invariants after every step. Seeds are fixed so a
/// failure reproduces; the seed and step are in the assertion message.
/// </summary>
public class LayoutPropertyTests
{
    private static readonly LayoutMetrics M = new(SplitterThickness: 4, TabBarHeight: 28, MinPaneWidth: 80, MinPaneHeight: 40);

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 25).Select(i => new object[] { i });

    private static WindowHandle W(nint n) => new(n);

    /// <summary>Applies one random operation and keeps <paramref name="model"/> (the expected window set) in step.</summary>
    private static string RandomStep(Random rng, LayoutTree tree, HashSet<WindowHandle> model, ref nint next)
    {
        var panes = tree.Panes.ToList();
        var windows = model.ToList();
        switch (rng.Next(windows.Count == 0 ? 2 : 6))
        {
            case 0:
            {
                var pane = panes[rng.Next(panes.Count)];
                tree.AddTab(pane, W(next));
                model.Add(W(next));
                return $"tab {next++}";
            }
            case 1:
            {
                var pane = panes[rng.Next(panes.Count)];
                var side = (DockSide)rng.Next(4);
                // Splitting an empty pane is only possible for the root, which the app never does with no window.
                if (pane.Windows.Count == 0) { tree.AddTab(pane, W(next)); model.Add(W(next)); return $"tab {next++}"; }
                tree.Split(pane, side, W(next));
                model.Add(W(next));
                return $"split {side} {next++}";
            }
            case 2 or 3:
            {
                var w = windows[rng.Next(windows.Count)];
                tree.Remove(w);
                model.Remove(w);
                return $"remove {w}";
            }
            case 4:
            {
                var w = windows[rng.Next(windows.Count)];
                tree.Activate(w);
                return $"activate {w}";
            }
            default:
            {
                if (tree.Root is not SplitNode) return "noop";
                var split = AllSplits(tree.Root).ElementAt(rng.Next(AllSplits(tree.Root).Count()));
                split.Ratio = rng.NextDouble();
                return "ratio";
            }
        }
    }

    private static IEnumerable<SplitNode> AllSplits(LayoutNode n) => n is SplitNode s
        ? new[] { s }.Concat(AllSplits(s.First)).Concat(AllSplits(s.Second))
        : [];

    private static void CheckTree(LayoutTree tree, HashSet<WindowHandle> model, string at)
    {
        Assert.True(tree.Root.Parent is null, at);
        Check(tree.Root);
        Assert.True(model.SetEquals(tree.Windows), $"{at}: windows differ");
        Assert.Equal(tree.Windows.Count(), tree.Windows.Distinct().Count());
        foreach (var w in model) Assert.Contains(w, tree.FindPane(w)!.Windows);

        void Check(LayoutNode n)
        {
            switch (n)
            {
                case SplitNode s:
                    Assert.Same(s, s.First.Parent);
                    Assert.Same(s, s.Second.Parent);
                    Check(s.First);
                    Check(s.Second);
                    break;
                case Pane p:
                    if (p.Parent is not null) Assert.True(p.Windows.Count > 0, $"{at}: empty non-root pane");
                    if (p.Windows.Count == 0) Assert.Equal(0, p.ActiveIndex);
                    else Assert.InRange(p.ActiveIndex, 0, p.Windows.Count - 1);
                    Assert.Equal(p.Windows.Count == 0 ? null : p.Windows[p.ActiveIndex], p.ActiveWindow);
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomOperations_KeepTheTreeConsistent(int seed)
    {
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var model = new HashSet<WindowHandle>();
        nint next = 1;
        for (var step = 0; step < 300; step++)
        {
            var op = RandomStep(rng, tree, model, ref next);
            CheckTree(tree, model, $"seed {seed} step {step} ({op})");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Arrange_TilesTheBoundsExactly(int seed)
    {
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var model = new HashSet<WindowHandle>();
        nint next = 1;
        for (var step = 0; step < 200; step++)
        {
            var op = RandomStep(rng, tree, model, ref next);
            var bounds = new Rect(rng.Next(-500, 500), rng.Next(-500, 500), rng.Next(0, 2500), rng.Next(0, 1500));
            var at = $"seed {seed} step {step} ({op}) bounds {bounds}";
            var result = LayoutEngine.Arrange(tree.Root, bounds, M);

            Assert.Equal(tree.Panes.Count(), result.Panes.Count);
            Assert.Equal(AllSplits(tree.Root).Count(), result.Splitters.Count);
            var pieces = result.Panes.Select(p => p.Bounds).Concat(result.Splitters.Select(s => s.Bounds)).ToList();
            foreach (var r in pieces)
            {
                Assert.True(r.Width >= 0 && r.Height >= 0, $"{at}: negative size {r}");
                Assert.True(Inside(r, bounds), $"{at}: {r} outside");
            }
            for (var i = 0; i < pieces.Count; i++)
                for (var j = i + 1; j < pieces.Count; j++)
                    Assert.True(Overlap(pieces[i], pieces[j]) == 0, $"{at}: {pieces[i]} overlaps {pieces[j]}");
            Assert.Equal(bounds.Width * bounds.Height, pieces.Sum(r => r.Width * r.Height), 6);

            foreach (var p in result.Panes)
            {
                Assert.Equal(p.Bounds, new Rect(p.TabBar.X, p.TabBar.Y, p.TabBar.Width, p.TabBar.Height + p.Content.Height));
                Assert.Equal(p.Bounds.X, p.Content.X);
                Assert.Equal(p.TabBar.Y + p.TabBar.Height, p.Content.Y);
            }
            foreach (var s in result.Splitters) Assert.True(Inside(s.Bounds, s.SplitBounds), at);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ClampAll_GivesEveryPaneItsMinimum_WhenTheBoundsAllowIt(int seed)
    {
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var model = new HashSet<WindowHandle>();
        var mins = new Dictionary<WindowHandle, Size>();
        nint next = 1;
        for (var step = 0; step < 150; step++)
        {
            var op = RandomStep(rng, tree, model, ref next);
            foreach (var w in model.Where(w => !mins.ContainsKey(w)))
                mins[w] = rng.Next(3) == 0 ? new Size(rng.Next(0, 500), rng.Next(0, 300)) : new Size(0, 0);
            var min = LayoutEngine.MinSize(tree.Root, M, w => mins[w]);
            var bounds = new Rect(0, 0, min.Width + rng.Next(0, 800), min.Height + rng.Next(0, 500));
            var at = $"seed {seed} step {step} ({op})";

            LayoutEngine.ClampAll(tree.Root, bounds, M, w => mins[w]);
            var result = LayoutEngine.Arrange(tree.Root, bounds, M);

            foreach (var p in result.Panes)
            {
                var need = LayoutEngine.MinSize(p.Pane, M, w => mins[w]);
                // Arrange rounds each split to whole units, so a pane can come out one unit short.
                Assert.True(p.Bounds.Width >= need.Width - 1 && p.Bounds.Height >= need.Height - 1,
                    $"{at}: pane {p.Bounds} below its minimum {need}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FindDivider_IsNullExactlyForEdgesOnTheOuterBorder(int seed)
    {
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var model = new HashSet<WindowHandle>();
        nint next = 1;
        var bounds = new Rect(0, 0, 2000, 1200);
        for (var step = 0; step < 100; step++)
        {
            RandomStep(rng, tree, model, ref next);
            var result = LayoutEngine.Arrange(tree.Root, bounds, M);
            foreach (var p in result.Panes)
            {
                var r = p.Bounds;
                var at = $"seed {seed} step {step} pane {r}";
                Assert.True((r.X == 0) == LayoutTree.FindDivider(p.Pane, DockSide.Left) is null, $"{at} left");
                Assert.True((r.Y == 0) == LayoutTree.FindDivider(p.Pane, DockSide.Top) is null, $"{at} top");
                Assert.True((r.X + r.Width == bounds.Width) == LayoutTree.FindDivider(p.Pane, DockSide.Right) is null, $"{at} right");
                Assert.True((r.Y + r.Height == bounds.Height) == LayoutTree.FindDivider(p.Pane, DockSide.Bottom) is null, $"{at} bottom");

                // The divider found for an inner edge really is the splitter adjacent to that edge.
                if (LayoutTree.FindDivider(p.Pane, DockSide.Right) is { } right)
                    Assert.Equal(r.X + r.Width, result.Splitters.Single(s => s.Split == right).Bounds.X);
                if (LayoutTree.FindDivider(p.Pane, DockSide.Bottom) is { } bottom)
                    Assert.Equal(r.Y + r.Height, result.Splitters.Single(s => s.Split == bottom).Bounds.Y);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ClampRatio_IsIdempotentAndStaysInRange(int seed)
    {
        var rng = new Random(seed);
        var tree = new LayoutTree();
        var a = (Pane)tree.Root;
        tree.AddTab(a, W(1));
        var split = (SplitNode)tree.Split(a, rng.Next(2) == 0 ? DockSide.Right : DockSide.Bottom, W(2)).Parent!;
        for (var i = 0; i < 500; i++)
        {
            var mins = new Dictionary<WindowHandle, Size> { [W(1)] = new(rng.Next(0, 900), rng.Next(0, 900)), [W(2)] = new(rng.Next(0, 900), rng.Next(0, 900)) };
            var length = rng.Next(0, 2000);
            var once = LayoutEngine.ClampRatio(split, rng.NextDouble() * 3 - 1, length, M, w => mins[w]);
            split.Ratio = once;
            var twice = LayoutEngine.ClampRatio(split, once, length, M, w => mins[w]);

            Assert.InRange(once, 0, 1);
            Assert.Equal(once, twice, 9);
        }
    }

    private static bool Inside(Rect r, Rect outer) =>
        r.X >= outer.X - 1e-9 && r.Y >= outer.Y - 1e-9 &&
        r.X + r.Width <= outer.X + outer.Width + 1e-9 && r.Y + r.Height <= outer.Y + outer.Height + 1e-9;

    private static double Overlap(Rect a, Rect b) =>
        Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) *
        Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
}
