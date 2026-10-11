using System.Text.Json;
using System.Text.Json.Serialization;
using Dockyard.Domain;
using Dockyard.Application.Internal;

namespace Dockyard.Application.Tests;

public class SavedLayoutTests
{
    private static readonly WindowHandle Frame = new(1000), RestoredFrame = new(1001), A = new(1), B = new(2), C = new(3);
    private static readonly PixelRect Initial = new(10, 10, 610, 410);

    private readonly FakeWindowSystem _ws = new();
    private readonly ManualScheduler _scheduler = new();
    /// <summary>The view of the group created last.</summary>
    private FakeView _view = null!;

    private GroupController NewGroup() => NewGroup(Frame);

    private GroupController NewGroup(WindowHandle frame)
    {
        _ws.AddToZOrder(frame);
        var view = _view = new FakeView(frame);
        var c = new GroupController(view, _ws, _scheduler);
        view.Connect(c, _scheduler);
        c.OnSurfaceResized();
        return c;
    }

    private WindowHandle Window(WindowHandle h, string process = "app")
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
        Assert.Equal(new SavedTab(new ProcessName("code"), "w1", A, Initial, @"C:/apps/code.exe"), Assert.Single(saved.First!.Tabs!));
        Assert.Equal(["cmd", "cmd"], saved.Second!.Tabs!.Select(t => t.ProcessName.Value));
        Assert.Equal(1, saved.Second.ActiveIndex);
    }

    [Fact]
    public void RestoreLayout_RebuildsTheGroupAndKeepsOriginalBounds()
    {
        var saved = BuildSample().SaveLayout();
        // The first group goes away without releasing (as in a crash): its windows stay where it put them.
        var restored = NewGroup(RestoredFrame);

        Assert.Equal(3, restored.RestoreLayout(saved, t => t.Handle));

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Equal(0.3, ((SplitNode)restored.Layout.Splitters[0].Split).Ratio, 3);
        Assert.Equal(C, restored.Layout.Panes[1].Pane.ActiveWindow);
        Assert.True(_ws.Z(C) < _ws.Z(RestoredFrame) && _ws.Z(RestoredFrame) < _ws.Z(B));
        restored.ReleaseAll();
        Assert.All(new[] { A, B, C }, w => Assert.Equal(Initial, _ws.GetBounds(w)));
    }

    [Fact]
    public void RestoreLayout_KeepsPanesWhoseWindowsAreMissing()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(RestoredFrame);

        Assert.Equal(1, restored.RestoreLayout(saved, t => t.ProcessName.Value == "code" ? t.Handle : WindowHandle.None));

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Empty(restored.Layout.Panes[1].Pane.Windows);
    }

    [Fact]
    public void RestoreLayout_MissingWindowsWaitAndAreSavedAgain()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(RestoredFrame);
        restored.RestoreLayout(saved, t => t.ProcessName.Value == "code" ? t.Handle : WindowHandle.None);

        var right = restored.Layout!.Panes[1].Pane;
        Assert.Equal(["w2", "w3"], _view.LastRender!.Waiting[right].Select(t => t.Title));
        Assert.Equal([@"C:/apps/cmd.exe", @"C:/apps/cmd.exe"], restored.WaitingTabs.Select(t => t.Path));
        // Saved again (e.g. in the session file), the waiting tabs keep their place but not the old window.
        Assert.Equal([WindowHandle.None, WindowHandle.None], restored.SaveLayout().Second!.Tabs!.Select(t => t.Handle));
    }

    [Fact]
    public void RemovingThePaneLastWindow_KeepsThePaneWhileTabsStillWaitThere()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(RestoredFrame);
        restored.RestoreLayout(saved, t => t.Title == "w3" ? WindowHandle.None : t.Handle);

        restored.Remove(B);

        Assert.Equal(2, restored.Layout!.Panes.Count);
        Assert.Equal(["w3"], restored.WaitingTabs.Select(t => t.Title));
        Assert.True(restored.FillWaiting(Window(new WindowHandle(21), "cmd"), new ProcessName("cmd"), "w3"));
        Assert.Equal([new WindowHandle(21)], restored.Layout.Panes[1].Pane.Windows);
    }

    [Fact]
    public void RestoreLayout_WithAnotherWindowOfTheApp_PutsThatWindowBackWhereItWas()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(RestoredFrame);
        var elsewhere = new PixelRect(700, 300, 900, 500);
        var other = new WindowHandle(30);
        _ws.Add(other, elsewhere, "w1");
        _ws.Windows[other].ProcessName = "code";

        restored.RestoreLayout(saved, t => t.ProcessName.Value == "code" ? other : WindowHandle.None);
        restored.ReleaseAll();

        Assert.Equal(elsewhere, _ws.GetBounds(other));
    }

    [Fact]
    public void FillWaiting_PutsANewWindowOfTheAppInItsPane_PreferringTheSameTitle()
    {
        var saved = BuildSample().SaveLayout();
        var restored = NewGroup(RestoredFrame);
        restored.RestoreLayout(saved, t => t.ProcessName.Value == "code" ? t.Handle : WindowHandle.None);
        var right = restored.Layout!.Panes[1].Pane;

        Assert.False(restored.FillWaiting(Window(new WindowHandle(20), "notepad"), new ProcessName("notepad"), "w3"));
        Assert.True(restored.FillWaiting(Window(new WindowHandle(21), "cmd"), new ProcessName("cmd"), "w3"));

        Assert.Equal([new WindowHandle(21)], right.Windows);
        Assert.Equal(["w2"], _view.LastRender!.Waiting[right].Select(t => t.Title));
        Assert.True(restored.FillWaiting(Window(new WindowHandle(22), "cmd"), new ProcessName("CMD"), "other"));
        Assert.Empty(_view.LastRender!.Waiting);
        Assert.False(restored.FillWaiting(Window(new WindowHandle(23), "cmd"), new ProcessName("cmd"), "w2"));
    }

    [Fact]
    public void RestoreLayout_ShowsWindowsLeftHiddenByAMinimizedGroup()
    {
        var c = BuildSample();
        c.OnFrameMinimized();
        var saved = c.SaveLayout();
        Assert.False(_ws.IsVisible(B));

        NewGroup(RestoredFrame).RestoreLayout(saved, t => t.Handle);

        Assert.All(new[] { A, B, C }, w => Assert.True(_ws.IsVisible(w)));
    }

    [Fact]
    public void RestoreLayout_RefusesAGroupThatAlreadyHasWindows()
    {
        var c = BuildSample();
        Assert.Equal(0, c.RestoreLayout(new SavedNode(Tabs: []), _ => WindowHandle.None));
        Assert.Equal(3, c.Members.Count());
    }

    [Fact]
    public void SavedGroup_RoundTripsThroughJson()
    {
        var group = new SavedGroup(new LayoutName("Dev"), new PixelRect(1, 2, 3, 4), BuildSample().SaveLayout());
        var options = new JsonSerializerOptions { Converters = { new WindowHandleConverter() } };
        var json = JsonSerializer.Serialize(group, options);
        var back = JsonSerializer.Deserialize<SavedGroup>(json, options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, options));
    }

    /// <summary>System.Text.Json cannot write a nint, so a handle needs a converter; Infrastructure has its own, internal one.</summary>
    private sealed class WindowHandleConverter : JsonConverter<WindowHandle>
    {
        public override WindowHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new((nint)reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, WindowHandle value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((long)value.Value);
    }

    // ---- the library of saved layouts ------------------------------------------------------------

    private static SavedGroup Named(string name) => new(new LayoutName(name), default, new SavedNode());

    [Fact]
    public void LayoutLibrary_PutReplacesTheSameName()
    {
        var layouts = new List<SavedGroup> { Named("A"), Named("B") };
        Assert.False(LayoutLibrary.Put(layouts, Named("C")));
        Assert.True(LayoutLibrary.Put(layouts, Named("A") with { Bounds = new PixelRect(1, 1, 2, 2) }));
        Assert.Equal(["B", "C", "A"], layouts.Select(l => l.Name.Value));
        Assert.Equal(new PixelRect(1, 1, 2, 2), layouts[^1].Bounds);
    }

    [Fact]
    public void LayoutLibrary_PutKeepsTheHotkeyOfTheLayoutItReplaces()
    {
        var layouts = new List<SavedGroup> { Named("A") with { Hotkey = "Ctrl+Alt+1" } };

        LayoutLibrary.Put(layouts, Named("A"));

        Assert.Equal("Ctrl+Alt+1", Assert.Single(layouts).Hotkey);
    }

    [Fact]
    public void LayoutLibrary_SetHotkeyTakesTheKeyFromTheLayoutThatHadIt()
    {
        var layouts = new List<SavedGroup> { Named("A") with { Hotkey = "Ctrl+Alt+1" }, Named("B") };
        var key = new ShortcutKey(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x31);

        Assert.True(LayoutLibrary.SetHotkey(layouts, new LayoutName("B"), key));
        Assert.Null(layouts[0].Hotkey);
        Assert.Equal("Ctrl+Alt+1", layouts[1].Hotkey);

        Assert.True(LayoutLibrary.SetHotkey(layouts, new LayoutName("B"), null));
        Assert.Null(layouts[1].Hotkey);
        Assert.False(LayoutLibrary.SetHotkey(layouts, new LayoutName("X"), key));
    }

    [Fact]
    public void LayoutLibrary_HotkeysSkipsTextsThatDoNotParseAndKeysTakenTwice()
    {
        var layouts = new List<SavedGroup>
        {
            Named("A") with { Hotkey = "Ctrl+Alt+1" },
            Named("B") with { Hotkey = "Ctrl+Alt+1" },
            Named("C") with { Hotkey = "nonsense+" },
            Named("D"),
        };

        var hotkeys = LayoutLibrary.Hotkeys(layouts);

        Assert.Equal(new LayoutName("A"), Assert.Single(hotkeys).Value);
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
        Assert.Equal(ok, LayoutLibrary.Rename(layouts, new LayoutName(from), to));
        Assert.Equal([first, "B"], layouts.Select(l => l.Name.Value));
    }

    // ---- matching -------------------------------------------------------------------------------

    private static readonly WindowCandidate[] Candidates =
    [
        new(new WindowHandle(10), new ProcessName("cmd"), "build"),
        new(new WindowHandle(11), new ProcessName("cmd"), "server"),
        new(new WindowHandle(12), new ProcessName("code"), "project"),
    ];

    [Fact]
    public void Match_PrefersTheSameWindow()
    {
        var claimed = new HashSet<WindowHandle>();
        Assert.Equal(new WindowHandle(11), WindowMatcher.Match(new SavedTab(new ProcessName("cmd"), "renamed", new WindowHandle(11)), Candidates, claimed, sameWindowOnly: false));
        Assert.Contains(new WindowHandle(11), claimed);
    }

    [Fact]
    public void Match_FallsBackToTitleThenApp()
    {
        var claimed = new HashSet<WindowHandle>();
        Assert.Equal(new WindowHandle(11), WindowMatcher.Match(new SavedTab(new ProcessName("cmd"), "server"), Candidates, claimed, false));
        Assert.Equal(new WindowHandle(10), WindowMatcher.Match(new SavedTab(new ProcessName("cmd"), "server"), Candidates, claimed, false));
        Assert.Equal(WindowHandle.None, WindowMatcher.Match(new SavedTab(new ProcessName("cmd"), "server"), Candidates, claimed, false));
    }

    [Fact]
    public void Match_SameWindowOnly_IgnoresOtherWindowsOfTheApp()
    {
        Assert.Equal(WindowHandle.None, WindowMatcher.Match(new SavedTab(new ProcessName("cmd"), "server", new WindowHandle(99)), Candidates, new HashSet<WindowHandle>(), sameWindowOnly: true));
        // A reused handle now belonging to another app is not the same window.
        Assert.Equal(WindowHandle.None, WindowMatcher.Match(new SavedTab(new ProcessName("notepad"), "x", new WindowHandle(12)), Candidates, new HashSet<WindowHandle>(), sameWindowOnly: true));
    }
}
