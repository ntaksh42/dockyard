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
    /// <summary>The view of the group created last.</summary>
    private FakeView _view = null!;

    private GroupController NewGroup(nint frame = Frame)
    {
        _ws.AddToZOrder(frame);
        var view = _view = new FakeView(frame);
        var c = new GroupController(view, _ws, _scheduler);
        view.Connect(c, _scheduler);
        c.OnSurfaceResized();
        return c;
    }

    private nint Window(nint h, string process = "app")
    {
        _ws.Add(h, Initial, $"w{h}");
        _ws.Windows[h].ProcessName = process;
        _ws.Windows[h].ProcessPath = $@"C:/apps/{process}.exe";
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
        Assert.Equal(new SavedTab("code", "w1", A, Initial, @"C:/apps/code.exe"), Assert.Single(saved.First!.Tabs!));
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
    public void RestoreLayout_MissingWindowsWaitAndAreSavedAgain()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(1001);
        restored.RestoreLayout(saved, t => t.ProcessName == "code" ? (nint)t.Handle : 0);

        var right = restored.Layout!.Panes[1].Pane;
        Assert.Equal(["w2", "w3"], _view.LastRender!.Waiting[right].Select(t => t.Title));
        Assert.Equal([@"C:/apps/cmd.exe", @"C:/apps/cmd.exe"], restored.WaitingTabs.Select(t => t.Path));
        // Saved again (e.g. in the session file), the waiting tabs keep their place but not the old window.
        Assert.Equal([0L, 0L], restored.SaveLayout().Second!.Tabs!.Select(t => t.Handle));
    }

    [Fact]
    public void RemovingThePaneLastWindow_KeepsThePaneWhileTabsStillWaitThere()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(1001);
        restored.RestoreLayout(saved, t => t.Title == "w3" ? 0 : (nint)t.Handle);

        restored.Remove(B);

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Equal(["w3"], restored.WaitingTabs.Select(t => t.Title));
        Assert.True(restored.FillWaiting(Window(21, "cmd"), "cmd", "w3"));
        Assert.Equal([21], restored.Layout.Panes[1].Pane.Windows);
    }

    [Fact]
    public void RestoreLayout_WithAnotherWindowOfTheApp_PutsThatWindowBackWhereItWas()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(1001);
        var elsewhere = new PixelRect(700, 300, 900, 500);
        _ws.Add(30, elsewhere, "w1");
        _ws.Windows[30].ProcessName = "code";

        restored.RestoreLayout(saved, t => t.ProcessName == "code" ? 30 : 0);
        restored.ReleaseAll();

        Assert.Equal(elsewhere, _ws.GetBounds(30));
    }

    [Fact]
    public void FillWaiting_PutsANewWindowOfTheAppInItsPane_PreferringTheSameTitle()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(1001);
        restored.RestoreLayout(saved, t => t.ProcessName == "code" ? (nint)t.Handle : 0);
        var right = restored.Layout!.Panes[1].Pane;

        Assert.False(restored.FillWaiting(Window(20, "notepad"), "notepad", "w3"));
        Assert.True(restored.FillWaiting(Window(21, "cmd"), "cmd", "w3"));

        Assert.Equal([21], right.Windows);
        Assert.Equal(["w2"], _view.LastRender!.Waiting[right].Select(t => t.Title));
        Assert.True(restored.FillWaiting(Window(22, "cmd"), "CMD", "other"));
        Assert.Empty(_view.LastRender!.Waiting);
        Assert.False(restored.FillWaiting(Window(23, "cmd"), "cmd", "w2"));
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

    // ---- the library of saved layouts ------------------------------------------------------------

    private static SavedGroup Named(string name) => new(name, default, new SavedNode());

    [Fact]
    public void LayoutLibrary_PutReplacesTheSameName()
    {
        var layouts = new List<SavedGroup> { Named("A"), Named("B") };
        Assert.False(LayoutLibrary.Put(layouts, Named("C")));
        Assert.True(LayoutLibrary.Put(layouts, Named("A") with { Bounds = new PixelRect(1, 1, 2, 2) }));
        Assert.Equal(["B", "C", "A"], layouts.Select(l => l.Name));
        Assert.Equal(new PixelRect(1, 1, 2, 2), layouts[^1].Bounds);
    }

    [Theory]
    [InlineData("A", " New ", true, "New")]
    [InlineData("A", "B", false, "A")]
    [InlineData("A", "  ", false, "A")]
    [InlineData("A", "A", true, "A")]
    [InlineData("X", "New", false, "A")]
    public void LayoutLibrary_RenameKeepsNamesUnique(string from, string to, bool ok, string first)
    {
        var layouts = new List<SavedGroup> { Named("A"), Named("B") };
        Assert.Equal(ok, LayoutLibrary.Rename(layouts, from, to));
        Assert.Equal([first, "B"], layouts.Select(l => l.Name));
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
