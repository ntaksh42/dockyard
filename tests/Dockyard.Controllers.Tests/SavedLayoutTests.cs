using System.Text.Json;
using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.App.Controllers.Tests;

public class SavedLayoutTests
{
    private const nint Frame = 1000, A = 1, B = 2, C = 3;
    private static readonly PixelRect Initial = new(10, 10, 610, 410);

    private readonly FakeWindowSystem _ws = new();
    private readonly ManualScheduler _scheduler = new();

    private GroupController NewGroup(nint frame = Frame)
    {
        _ws.AddToZOrder(frame);
        var view = new FakeView(frame);
        var c = new GroupController(view, _ws, _scheduler);
        view.Connect(c, _scheduler);
        c.OnSurfaceResized();
        return c;
    }

    private nint Window(nint h, string process = "app")
    {
        _ws.Add(h, Initial, $"w{h}");
        _ws.Windows[h].ProcessName = process;
        return h;
    }

    /// <summary>A | (B, C as tabs, C active), with the divider moved off the middle.</summary>
    private GroupController BuildSample()
    {
        var c = NewGroup();
        c.Adopt(Window(A, "code"), c.Layout!.Panes[0].Pane, null);
        var right = c.Layout!.Panes[0].Pane;
        c.Adopt(Window(B, "cmd"), right, DockSide.Right);
        right = c.Layout!.Panes[1].Pane;
        c.Adopt(Window(C, "cmd"), right, null);
        ((SplitNode)c.Layout!.Splitters[0].Split).Ratio = 0.3;
        return c;
    }

    [Fact]
    public void SaveLayout_RecordsTreeTabsAndOriginalBounds()
    {
        var saved = BuildSample().SaveLayout();

        Assert.Equal(Orientation.Horizontal, saved.Split);
        Assert.Equal(0.3, saved.Ratio, 3);
        Assert.Equal(new SavedTab("code", "w1", A, Initial), Assert.Single(saved.First!.Tabs!));
        Assert.Equal(["cmd", "cmd"], saved.Second!.Tabs!.Select(t => t.ProcessName));
        Assert.Equal(1, saved.Second.ActiveIndex);
    }

    [Fact]
    public void RestoreLayout_RebuildsTheGroupAndKeepsOriginalBounds()
    {
        var saved = BuildSample().SaveLayout();
        // The first group goes away without releasing (as in a crash): its windows stay where it put them.
        var restored = NewGroup(1001);

        Assert.Equal(3, restored.RestoreLayout(saved, t => (nint)t.Handle));

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Equal(0.3, ((SplitNode)restored.Layout.Splitters[0].Split).Ratio, 3);
        Assert.Equal(C, restored.Layout.Panes[1].Pane.ActiveWindow);
        Assert.True(_ws.Z(C) < _ws.Z(1001) && _ws.Z(1001) < _ws.Z(B));
        restored.ReleaseAll();
        Assert.All(new[] { A, B, C }, w => Assert.Equal(Initial, _ws.GetBounds(w)));
    }

    [Fact]
    public void RestoreLayout_KeepsPanesWhoseWindowsAreMissing()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(1001);

        Assert.Equal(1, restored.RestoreLayout(saved, t => t.ProcessName == "code" ? (nint)t.Handle : 0));

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Empty(restored.Layout.Panes[1].Pane.Windows);
    }

    [Fact]
    public void RestoreLayout_ShowsWindowsLeftHiddenByAMinimizedGroup()
    {
        var c = BuildSample();
        c.OnFrameMinimized();
        var saved = c.SaveLayout();
        Assert.False(_ws.IsVisible(B));

        NewGroup(1001).RestoreLayout(saved, t => (nint)t.Handle);

        Assert.All(new[] { A, B, C }, w => Assert.True(_ws.IsVisible(w)));
    }

    [Fact]
    public void RestoreLayout_RefusesAGroupThatAlreadyHasWindows()
    {
        var c = BuildSample();
        Assert.Equal(0, c.RestoreLayout(new SavedNode(Tabs: []), _ => 0));
        Assert.Equal(3, c.Members.Count());
    }

    [Fact]
    public void SavedGroup_RoundTripsThroughJson()
    {
        var group = new SavedGroup("Dev", new PixelRect(1, 2, 3, 4), BuildSample().SaveLayout());
        var json = JsonSerializer.Serialize(group);
        var back = JsonSerializer.Deserialize<SavedGroup>(json)!;
        Assert.Equal(json, JsonSerializer.Serialize(back));
    }

    // ---- matching -------------------------------------------------------------------------------

    private static readonly WindowCandidate[] Candidates =
    [
        new(10, "cmd", "build"),
        new(11, "cmd", "server"),
        new(12, "code", "project"),
    ];

    [Fact]
    public void Match_PrefersTheSameWindow()
    {
        var claimed = new HashSet<nint>();
        Assert.Equal(11, WindowMatcher.Match(new SavedTab("cmd", "renamed", 11), Candidates, claimed, sameWindowOnly: false));
        Assert.Contains(11, claimed);
    }

    [Fact]
    public void Match_FallsBackToTitleThenApp()
    {
        var claimed = new HashSet<nint>();
        Assert.Equal(11, WindowMatcher.Match(new SavedTab("cmd", "server"), Candidates, claimed, false));
        Assert.Equal(10, WindowMatcher.Match(new SavedTab("cmd", "server"), Candidates, claimed, false));
        Assert.Equal(0, WindowMatcher.Match(new SavedTab("cmd", "server"), Candidates, claimed, false));
    }

    [Fact]
    public void Match_SameWindowOnly_IgnoresOtherWindowsOfTheApp()
    {
        Assert.Equal(0, WindowMatcher.Match(new SavedTab("cmd", "server", 99), Candidates, new HashSet<nint>(), sameWindowOnly: true));
        // A reused handle now belonging to another app is not the same window.
        Assert.Equal(0, WindowMatcher.Match(new SavedTab("notepad", "x", 12), Candidates, new HashSet<nint>(), sameWindowOnly: true));
    }
}
