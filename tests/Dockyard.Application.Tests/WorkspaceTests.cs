using Dockyard.Domain;

namespace Dockyard.Application.Tests;

/// <summary>The workspace against an in-memory platform and shell: what happens between groups and the machine.</summary>
public class WorkspaceTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Ctrl | KeyModifiers.Alt;
    private static readonly PixelRect Initial = new(10, 10, 610, 410);
    private static readonly PixelRect SavedBounds = new(50, 60, 850, 660);

    private readonly FakePlatform _platform = new();
    private readonly ManualScheduler _scheduler = new();
    private readonly FakeShell _shell;
    private Workspace? _workspace;

    public WorkspaceTests()
    {
        _shell = new FakeShell(_scheduler) { Windows = _platform.Desktop };
    }

    private Workspace Workspace => _workspace ??= new Workspace(_platform, _shell);

    private nint Add(nint handle, string process = "app", string title = "t")
    {
        _platform.Desktop.Add(handle, Initial, title);
        _platform.Desktop.Windows[handle].ProcessName = process;
        _platform.CatalogFake.Windows.Add(new WindowInfo(handle, title, process, true));
        return handle;
    }

    private static Pane PaneOf(FakeFrame frame) => frame.Controller!.Layout!.Panes[0].Pane;

    private static SavedGroup Saved(string name, params SavedTab[] tabs) => new(name, SavedBounds, new SavedNode(Tabs: tabs));

    // ---- startup ----------------------------------------------------------------------------------

    [Fact]
    public void Start_WithNothingToRestore_OpensOneEmptyGroup()
    {
        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.True(frame.Shown);
        Assert.Equal("Group 1", frame.GroupName);
        Assert.Empty(frame.Controller!.Members);
    }

    [Fact]
    public void Start_AfterACrash_PutsTheSameWindowsBackIntoTheirGroups()
    {
        Add(1, "notepad", "a");
        _platform.SessionStore.Session = [new SavedGroup("Work", SavedBounds,
            new SavedNode(Tabs: [new SavedTab("notepad", "a", Handle: 1, OriginalBounds: Initial)]), TabBarPlacement.Left)];

        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Work", frame.GroupName);
        Assert.Equal(SavedBounds, frame.ShownAt);
        Assert.Equal(TabBarPlacement.Left, frame.Placement);
        Assert.Contains(1, frame.Controller!.Members);
    }

    [Fact]
    public void Start_AfterACrash_ClosesGroupsWhoseWindowsAreGoneAndOpensAnEmptyOne()
    {
        _platform.SessionStore.Session = [new SavedGroup("Gone", SavedBounds,
            new SavedNode(Tabs: [new SavedTab("notepad", "a", Handle: 1, OriginalBounds: Initial)]))];

        Workspace.Start();

        Assert.Equal(2, _shell.Frames.Count);
        Assert.True(_shell.Frames[0].Closed);
        Assert.False(_shell.Frames[1].Closed);
    }

    [Fact]
    public void Start_TakesTheSameWindowOnlyOnce()
    {
        Add(1, "notepad", "a");
        _platform.SessionStore.Session =
        [
            new SavedGroup("First", SavedBounds, new SavedNode(Tabs: [new SavedTab("notepad", "a", Handle: 1)])),
            new SavedGroup("Second", SavedBounds, new SavedNode(Tabs: [new SavedTab("notepad", "a", Handle: 1)])),
        ];

        Workspace.Start();

        Assert.Contains(1, _shell.Frames[0].Controller!.Members);
        Assert.True(_shell.Frames[1].Closed);
    }

    [Fact]
    public void Start_OpensTheLastGroupsWhenAsked()
    {
        Add(1, "code", "proj");
        _platform.SettingsStore.Settings.OpenLastGroups = true;
        _platform.SessionStore.LastGroups = [Saved("Dev", new SavedTab("code", "other title"))];

        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Dev", frame.GroupName);
        Assert.Contains(1, frame.Controller!.Members);
    }

    [Fact]
    public void Start_DoesNotOpenTheLastGroupsUnlessAsked()
    {
        _platform.SessionStore.LastGroups = [Saved("Dev", new SavedTab("code", "proj"))];

        Workspace.Start();

        Assert.Equal("Group 1", Assert.Single(_shell.Frames).GroupName);
    }

    // ---- layouts ----------------------------------------------------------------------------------

    [Fact]
    public void SaveLayout_ReplacesALayoutOfTheSameNameAndSaysSo()
    {
        Workspace.Start();
        var frame = _shell.Frames[0];

        frame.UserSavesLayout();
        frame.UserSavesLayout();

        var layout = Assert.Single(_platform.LayoutStore.Layouts);
        Assert.Equal("Group 1", layout.Name);
        Assert.Equal(["Saved layout \"Group 1\".", "Replaced the saved layout \"Group 1\"."], _shell.Notifications);
    }

    [Fact]
    public void OpenLayout_FillsTabsFromOpenWindowsAsANewGroup()
    {
        Add(1, "code", "proj");
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab("code", "proj")));

        Assert.Equal(2, _shell.Frames.Count);
        Assert.Contains(1, _shell.Frames[1].Controller!.Members);
        Assert.Equal(SavedBounds, _shell.Frames[1].ShownAt);
    }

    [Fact]
    public void OpenLayout_GivesEachTabItsExactTitleBeforeAnyTabTakesAnyWindowOfTheApp()
    {
        Add(1, "msedge", "Docs");
        Add(2, "msedge", "News");
        Workspace.Start();
        var layout = new SavedGroup("Web", SavedBounds, new SavedNode(
            Split: Orientation.Horizontal,
            First: new SavedNode(Tabs: [new SavedTab("msedge", "Mail")]),
            Second: new SavedNode(Tabs: [new SavedTab("msedge", "Docs")])));

        Workspace.OpenLayout(layout);

        var panes = _shell.Frames[1].Controller!.Layout!.Panes;
        // "Docs" goes to the pane that asked for it; "Mail" takes what is left.
        Assert.Equal([(nint)2], panes[0].Pane.Windows);
        Assert.Equal([(nint)1], panes[1].Pane.Windows);
    }

    [Fact]
    public void OpenLayout_IntoAnEmptyGroupKeepsWhereThatGroupIs()
    {
        Add(1, "code", "proj");
        Workspace.Start();

        _shell.Frames[0].UserOpensLayout(Saved("Dev", new SavedTab("code", "proj")));

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Dev", frame.GroupName);
        Assert.Contains(1, frame.Controller!.Members);
    }

    [Fact]
    public void OpenLayout_StartsMissingAppsOnlyWhenEnabled()
    {
        _platform.SettingsStore.Settings.LaunchMissingApps = true;
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab("code", "proj", Path: @"C:\apps\code.exe")));

        Assert.Equal([@"C:\apps\code.exe"], _platform.LauncherFake.Launched);
    }

    [Fact]
    public void OpenLayout_DoesNotStartAppsByDefault()
    {
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab("code", "proj", Path: @"C:\apps\code.exe")));

        Assert.Empty(_platform.LauncherFake.Launched);
    }

    [Fact]
    public void ANewWindowOfAWaitedForAppFillsTheWaitingTabOnceItHasATitle()
    {
        Workspace.Start();
        Workspace.OpenLayout(Saved("Dev", new SavedTab("code", "proj")));
        var frame = _shell.Frames[1];
        Assert.Empty(frame.Controller!.Members);

        _platform.Desktop.Add(7, Initial);
        _platform.Desktop.Windows[7].ProcessName = "code";
        _platform.Events.Raise(WindowEvent.Created, 7);
        _platform.Events.Raise(WindowEvent.Shown, 7);
        Assert.Empty(frame.Controller.Members);

        _platform.CatalogFake.Windows.Add(new WindowInfo(7, "proj", "code", true));
        _platform.Events.Raise(WindowEvent.TitleChanged, 7);

        Assert.Contains(7, frame.Controller.Members);
    }

    [Fact]
    public void AWindowThatWasAlreadyOpenDoesNotFillAWaitingTab()
    {
        Workspace.Start();
        Workspace.OpenLayout(Saved("Dev", new SavedTab("code", "proj")));
        Add(7, "code", "proj");

        _platform.Events.Raise(WindowEvent.Shown, 7);

        Assert.Empty(_shell.Frames[1].Controller!.Members);
    }

    // ---- window events ----------------------------------------------------------------------------

    [Fact]
    public void ADestroyedMemberLeavesItsGroup()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);

        _platform.Desktop.Close(1);
        _platform.Events.Raise(WindowEvent.Destroyed, 1);

        Assert.Empty(frame.Controller.Members);
    }

    [Fact]
    public void TheForegroundIsCheckedPeriodically()
    {
        Workspace.Start();

        _scheduler.FireTimers();

        Assert.True(_platform.Events.ForegroundChecks >= 1);
    }

    // ---- session ----------------------------------------------------------------------------------

    [Fact]
    public void TheSessionIsWrittenOnceTheMouseIsReleased()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);
        _platform.InputFake.IsLeftButtonDown = true;

        _scheduler.FireTimers();
        Assert.Null(_platform.SessionStore.Session);

        _platform.InputFake.IsLeftButtonDown = false;
        _scheduler.FireTimers();

        var saved = Assert.Single(_platform.SessionStore.Session!);
        Assert.Equal("Group 1", saved.Name);
        Assert.Single(saved.Layout.Tabs!);
        Assert.Single(_platform.SessionStore.LastGroups!);
    }

    [Fact]
    public void Stop_PutsMembersBackAndRemovesTheSession()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);
        _platform.Desktop.PlaceVisible(1, new PixelRect(300, 300, 500, 500));

        Workspace.Stop();

        Assert.Equal(Initial, _platform.Desktop.GetBounds(1));
        Assert.True(_platform.SessionStore.SessionCleared > 0);
        Assert.Null(_platform.KeyboardFake.KeyPressed);
    }

    [Fact]
    public void Quit_KeepsTheOpenGroupsForTheNextStartAndClosesThemAll()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);

        Workspace.Quit();

        Assert.True(frame.Closed);
        Assert.True(_shell.Exited);
        Assert.Single(_platform.SessionStore.LastGroups!);
        // Closing the groups one by one must not empty what was just kept.
        _scheduler.FireTimers();
        Assert.Single(_platform.SessionStore.LastGroups!);
    }

    // ---- shortcuts --------------------------------------------------------------------------------

    [Fact]
    public void AShortcutActsOnTheGroupOfTheForegroundWindow()
    {
        Add(1);
        Add(2);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);
        frame.Controller.Adopt(2, PaneOf(frame), null);
        _platform.Desktop.Focus(2);

        var taken = _platform.KeyboardFake.Press(0x25 /* Left */, CtrlAlt);
        _scheduler.RunPosted();

        Assert.True(taken);
        Assert.Equal(1, PaneOf(frame).ActiveWindow);
    }

    [Fact]
    public void AShortcutIsLeftAloneWhenNoGroupHasTheFocus()
    {
        Add(1);
        Workspace.Start();

        Assert.False(_platform.KeyboardFake.Press(0x25, CtrlAlt));
    }

    [Fact]
    public void AKeyThatIsNoShortcutIsLeftAlone()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);
        _platform.Desktop.Focus(1);

        Assert.False(_platform.KeyboardFake.Press(0x41 /* A */, CtrlAlt));
    }

    [Fact]
    public void AFrameShortcutRunsOnTheFrameOfTheGroup()
    {
        Workspace.Start();
        var frame = _shell.Frames[0];
        _platform.Desktop.Focus(frame.Handle);

        Assert.True(_platform.KeyboardFake.Press(0x52 /* R */, CtrlAlt));
        _scheduler.RunPosted();

        Assert.Equal([ShortcutAction.RenameGroup], frame.ShortcutsRun);
    }

    [Fact]
    public void NewGroupShortcut_OpensAnotherGroup()
    {
        Workspace.Start();
        _platform.Desktop.Focus(_shell.Frames[0].Handle);

        Assert.True(_platform.KeyboardFake.Press(0x4E /* N */, CtrlAlt));
        _scheduler.RunPosted();

        Assert.Equal(2, _shell.Frames.Count);
        Assert.Equal("Group 2", _shell.Frames[1].GroupName);
    }

    [Fact]
    public void ShortcutsGoToTheTextBoxWhileTheFrameIsEditingText()
    {
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.IsEditingText = true;
        _platform.Desktop.Focus(frame.Handle);

        Assert.False(_platform.KeyboardFake.Press(0x52, CtrlAlt));
    }

    [Fact]
    public void WhileTheSettingsWaitForKeys_AKeyGoesThereAndModifiersAloneAreIgnored()
    {
        Workspace.Start();
        _shell.Capturing = true;

        Assert.False(_platform.KeyboardFake.Press(0x11 /* Ctrl */, CtrlAlt));
        Assert.True(_platform.KeyboardFake.Press(0x41, CtrlAlt));
        _scheduler.RunPosted();

        Assert.Equal([new ShortcutKey(CtrlAlt, 0x41)], _shell.Captured);
    }

    // ---- docking ----------------------------------------------------------------------------------

    [Fact]
    public void ATabDroppedOnAnotherFrameMovesThere()
    {
        Add(1);
        Add(2);
        Workspace.Start();
        Workspace.NewGroup();
        var (source, target) = (_shell.Frames[0], _shell.Frames[1]);
        source.Controller!.Adopt(1, PaneOf(source), null);
        source.Controller.Adopt(2, PaneOf(source), null);
        // Both frames lie on the same spot; the target is the one on top.
        _platform.Desktop.BringToTop(target.Handle);
        _platform.InputFake.Cursor = (600, 500);

        source.UserStartsTabDrag(2);
        Assert.True(_shell.Guide.Shown > 0);
        source.UserEndsTabDrag(false);

        Assert.Contains(2, target.Controller!.Members);
        Assert.DoesNotContain(2, source.Controller.Members);
    }

    [Fact]
    public void ACancelledTabDragChangesNothing()
    {
        Add(1);
        Add(2);
        Workspace.Start();
        Workspace.NewGroup();
        var (source, target) = (_shell.Frames[0], _shell.Frames[1]);
        source.Controller!.Adopt(1, PaneOf(source), null);
        source.Controller.Adopt(2, PaneOf(source), null);
        _platform.Desktop.BringToTop(target.Handle);
        _platform.InputFake.Cursor = (600, 500);

        source.UserStartsTabDrag(2);
        source.UserEndsTabDrag(true);

        Assert.Empty(target.Controller!.Members);
        Assert.Contains(2, source.Controller.Members);
    }

    // ---- settings ---------------------------------------------------------------------------------

    [Fact]
    public void ChangingTheTabBarPlacementReachesEveryGroupAndIsSaved()
    {
        Workspace.Start();
        Workspace.NewGroup();

        Workspace.SetTabBarPlacement(TabBarPlacement.Left);

        Assert.All(_shell.Frames, f => Assert.Equal(TabBarPlacement.Left, f.Placement));
        Assert.Equal(TabBarPlacement.Left, _platform.SettingsStore.Settings.TabBarPlacement);
        Assert.Equal(1, _platform.SettingsStore.Saves);
    }

    [Fact]
    public void ChangedShortcutsApplyRightAwayAndAreSavedAsDifferencesFromTheDefaults()
    {
        Workspace.Start();
        var bindings = Shortcuts.Defaults.ToDictionary();
        bindings[ShortcutAction.NewGroup] = new ShortcutKey(CtrlAlt, 0x47);

        Workspace.SetShortcuts(bindings);

        Assert.Equal(new ShortcutKey(CtrlAlt, 0x47), Workspace.ShortcutBindings[ShortcutAction.NewGroup]);
        Assert.Equal(new Dictionary<string, string> { ["NewGroup"] = "Ctrl+Alt+G" }, _platform.SettingsStore.Settings.Shortcuts);
    }

    [Fact]
    public void ChangedTitleBarAppsAreSaved()
    {
        Workspace.Start();

        Workspace.SetTitleBarApps(["chrome", "slack"]);

        Assert.Equal(["chrome", "slack"], _platform.SettingsStore.Settings.TitleBarApps);
    }

    [Fact]
    public void AFrameAsksForTheSettingsThroughTheShell()
    {
        Workspace.Start();

        _shell.Frames[0].UserAsksForSettings();

        Assert.Equal(1, _shell.SettingsShown);
    }

    [Fact]
    public void ReturnFocusToGroup_FocusesTheTabTheUserLastWorkedIn()
    {
        Add(1);
        Add(2);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(1, PaneOf(frame), null);
        frame.Controller.Adopt(2, PaneOf(frame), null);
        frame.UserActivates();
        _platform.Desktop.Focus(1);

        Workspace.ReturnFocusToGroup();

        Assert.Equal(frame.Controller.LastActive, _platform.Desktop.Focused);
    }
}
