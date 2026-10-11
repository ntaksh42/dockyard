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

    private static WindowHandle Handle(int value) => new(value);

    private WindowHandle Add(int value, string process = "app", string title = "t")
    {
        var handle = Handle(value);
        _platform.Desktop.Add(handle, Initial, title);
        _platform.Desktop.Windows[handle].ProcessName = process;
        _platform.CatalogFake.Windows.Add(new WindowInfo(handle, title, new ProcessName(process), true));
        return handle;
    }

    private static Pane PaneOf(FakeFrame frame) => frame.Controller!.Layout!.Panes[0].Pane;

    private static SavedGroup Saved(string name, params SavedTab[] tabs) => new(new LayoutName(name), SavedBounds, new SavedNode(Tabs: tabs));

    // ---- startup ----------------------------------------------------------------------------------

    [Fact]
    public void Start_WithNothingToRestore_OpensOneEmptyGroup()
    {
        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.True(frame.Shown);
        Assert.Equal("Group 1", frame.GroupName.Value);
        Assert.Empty(frame.Controller!.Members);
    }

    [Fact]
    public void Start_AfterACrash_PutsTheSameWindowsBackIntoTheirGroups()
    {
        Add(1, "notepad", "a");
        _platform.SessionStore.Session = [new SavedGroup(new LayoutName("Work"), SavedBounds,
            new SavedNode(Tabs: [new SavedTab(new ProcessName("notepad"), "a", Handle: Handle(1), OriginalBounds: Initial)]), TabBarPlacement.Left)];

        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Work", frame.GroupName.Value);
        Assert.Equal(SavedBounds, frame.ShownAt);
        Assert.Equal(TabBarPlacement.Left, frame.Placement);
        Assert.Contains(Handle(1), frame.Controller!.Members);
    }

    [Fact]
    public void NewGroup_DoesNotTakeTheNameOfARestoredOrRenamedGroup()
    {
        Add(1, "notepad", "a");
        _platform.SessionStore.Session = [new SavedGroup(new LayoutName("Group 1"), SavedBounds,
            new SavedNode(Tabs: [new SavedTab(new ProcessName("notepad"), "a", Handle: Handle(1), OriginalBounds: Initial)]))];
        Workspace.Start();

        Workspace.NewGroup();
        Workspace.NewGroup();

        Assert.Equal(["Group 1", "Group 2", "Group 3"], _shell.Frames.Select(f => f.GroupName.Value));
    }

    [Fact]
    public void NewGroup_ReusesTheNumberOfAGroupThatWasClosed()
    {
        Workspace.Start();
        Workspace.NewGroup();
        _shell.Frames[0].Close();

        Workspace.NewGroup();

        Assert.Equal("Group 1", _shell.Frames[2].GroupName.Value);
    }

    [Fact]
    public void Start_AfterACrash_ClosesGroupsWhoseWindowsAreGoneAndOpensAnEmptyOne()
    {
        _platform.SessionStore.Session = [new SavedGroup(new LayoutName("Gone"), SavedBounds,
            new SavedNode(Tabs: [new SavedTab(new ProcessName("notepad"), "a", Handle: Handle(1), OriginalBounds: Initial)]))];

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
            new SavedGroup(new LayoutName("First"), SavedBounds, new SavedNode(Tabs: [new SavedTab(new ProcessName("notepad"), "a", Handle: Handle(1))])),
            new SavedGroup(new LayoutName("Second"), SavedBounds, new SavedNode(Tabs: [new SavedTab(new ProcessName("notepad"), "a", Handle: Handle(1))])),
        ];

        Workspace.Start();

        Assert.Contains(Handle(1), _shell.Frames[0].Controller!.Members);
        Assert.True(_shell.Frames[1].Closed);
    }

    [Fact]
    public void Start_OpensTheLastGroupsWhenAsked()
    {
        Add(1, "code", "proj");
        _platform.SettingsStore.Settings.OpenLastGroups = true;
        _platform.SessionStore.LastGroups = [Saved("Dev", new SavedTab(new ProcessName("code"), "other title"))];

        Workspace.Start();

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Dev", frame.GroupName.Value);
        Assert.Contains(Handle(1), frame.Controller!.Members);
    }

    [Fact]
    public void Start_DoesNotOpenTheLastGroupsUnlessAsked()
    {
        _platform.SessionStore.LastGroups = [Saved("Dev", new SavedTab(new ProcessName("code"), "proj"))];

        Workspace.Start();

        Assert.Equal("Group 1", Assert.Single(_shell.Frames).GroupName.Value);
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
        Assert.Equal("Group 1", layout.Name.Value);
        Assert.Equal(["Saved layout \"Group 1\". Click to set a hotkey.", "Replaced the saved layout \"Group 1\"."], _shell.Notifications);
    }

    [Fact]
    public void ClickingTheNotificationOfANewLayout_ShowsItInTheLayoutsTab()
    {
        Workspace.Start();
        var frame = _shell.Frames[0];

        frame.UserSavesLayout();
        _shell.LastNotificationClick!();

        Assert.Equal(["Group 1"], _shell.LayoutsSelected);
    }

    [Fact]
    public void TheNotificationOfAReplacedLayout_HasNothingToClick()
    {
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.UserSavesLayout();

        frame.UserSavesLayout();

        Assert.Null(_shell.LastNotificationClick);
    }

    [Fact]
    public void OpenLayout_FillsTabsFromOpenWindowsAsANewGroup()
    {
        Add(1, "code", "proj");
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj")));

        Assert.Equal(2, _shell.Frames.Count);
        Assert.Contains(Handle(1), _shell.Frames[1].Controller!.Members);
        Assert.Equal(SavedBounds, _shell.Frames[1].ShownAt);
    }

    [Fact]
    public void OpenLayout_GivesEachTabItsExactTitleBeforeAnyTabTakesAnyWindowOfTheApp()
    {
        Add(1, "msedge", "Docs");
        Add(2, "msedge", "News");
        Workspace.Start();
        var layout = new SavedGroup(new LayoutName("Web"), SavedBounds, new SavedNode(
            Split: Orientation.Horizontal,
            First: new SavedNode(Tabs: [new SavedTab(new ProcessName("msedge"), "Mail")]),
            Second: new SavedNode(Tabs: [new SavedTab(new ProcessName("msedge"), "Docs")])));

        Workspace.OpenLayout(layout);

        var panes = _shell.Frames[1].Controller!.Layout!.Panes;
        // "Docs" goes to the pane that asked for it; "Mail" takes what is left.
        Assert.Equal([Handle(2)], panes[0].Pane.Windows);
        Assert.Equal([Handle(1)], panes[1].Pane.Windows);
    }

    [Fact]
    public void OpenLayout_IntoAnEmptyGroupKeepsWhereThatGroupIs()
    {
        Add(1, "code", "proj");
        Workspace.Start();

        _shell.Frames[0].UserOpensLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj")));

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Dev", frame.GroupName.Value);
        Assert.Contains(Handle(1), frame.Controller!.Members);
    }

    [Fact]
    public void OpenLayout_StartsMissingAppsOnlyWhenEnabled()
    {
        _platform.SettingsStore.Settings.LaunchMissingApps = true;
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj", Path: @"C:\apps\code.exe")));

        Assert.Equal([@"C:\apps\code.exe"], _platform.LauncherFake.Launched);
    }

    [Fact]
    public void OpenLayout_DoesNotStartAppsByDefault()
    {
        Workspace.Start();

        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj", Path: @"C:\apps\code.exe")));

        Assert.Empty(_platform.LauncherFake.Launched);
    }

    // ---- opening a layout by name: hotkey, shortcut file -------------------------------------------

    private static readonly SavedTab CodeTab = new(new ProcessName("code"), "proj", Path: @"C:\apps\code.exe");

    private void StoreLayout(string name, string? hotkey = null) =>
        _platform.LayoutStore.Layouts.Add(Saved(name, CodeTab) with { Hotkey = hotkey });

    [Fact]
    public void OpenLayoutByName_StartsMissingAppsWhateverTheSettingSays()
    {
        StoreLayout("Dev");
        Workspace.Start();

        Assert.True(Workspace.OpenLayoutByName(new LayoutName("Dev")));

        Assert.Equal("Dev", _shell.Frames[1].GroupName.Value);
        Assert.Equal([@"C:\apps\code.exe"], _platform.LauncherFake.Launched);
    }

    [Fact]
    public void OpenLayoutByName_WithAnUnknownName_TellsTheUser()
    {
        Workspace.Start();

        Assert.False(Workspace.OpenLayoutByName(new LayoutName("Nope")));

        Assert.Single(_shell.Frames);
        Assert.Contains("Nope", Assert.Single(_shell.Notifications));
    }

    [Fact]
    public void OpenLayoutByName_WhenItsGroupIsOpen_BringsItForwardInsteadOfStartingEverythingAgain()
    {
        StoreLayout("Dev");
        Workspace.Start();
        Workspace.OpenLayoutByName(new LayoutName("Dev"));
        var frame = _shell.Frames[1];
        frame.Minimize();

        Workspace.OpenLayoutByName(new LayoutName("Dev"));

        Assert.Equal(2, _shell.Frames.Count);
        Assert.Single(_platform.LauncherFake.Launched);
        Assert.False(frame.IsMinimized);
        Assert.Equal(frame.Handle, _platform.Desktop.Focused);
    }

    [Fact]
    public void Start_WithALayoutAsked_OpensItInsteadOfAnEmptyGroup()
    {
        StoreLayout("Dev");

        Workspace.Start(new LayoutName("Dev"));

        var frame = Assert.Single(_shell.Frames);
        Assert.Equal("Dev", frame.GroupName.Value);
    }

    [Fact]
    public void Start_WithAnUnknownLayoutAsked_StillOpensAnEmptyGroup()
    {
        Workspace.Start(new LayoutName("Nope"));

        Assert.Equal("Group 1", Assert.Single(_shell.Frames).GroupName.Value);
    }

    [Fact]
    public void ALayoutHotkeyOpensItFromAnywhere()
    {
        StoreLayout("Dev", "Ctrl+Alt+1");
        Workspace.Start();
        // The focus is on no group.
        _platform.Desktop.Focus(Handle(999));

        var taken = _platform.KeyboardFake.Press(0x31 /* 1 */, CtrlAlt);
        _scheduler.RunPosted();

        Assert.True(taken);
        Assert.Equal("Dev", _shell.Frames[1].GroupName.Value);
    }

    [Fact]
    public void AKeyThatIsNoLayoutHotkeyIsLeftAlone()
    {
        StoreLayout("Dev", "Ctrl+Alt+1");
        Workspace.Start();

        Assert.False(_platform.KeyboardFake.Press(0x32 /* 2 */, CtrlAlt));
    }

    [Fact]
    public void SetLayoutHotkey_TakesTheKeyFromAnActionAndFromAnotherLayout()
    {
        StoreLayout("Dev", "Ctrl+Alt+F1");
        StoreLayout("Ops");
        Workspace.Start();
        var key = Shortcuts.Defaults[ShortcutAction.NextTab];

        Workspace.SetLayoutHotkey(new LayoutName("Ops"), key);
        Workspace.SetLayoutHotkey(new LayoutName("Dev"), new ShortcutKey(CtrlAlt, 0x70 /* F1 */));
        Workspace.SetLayoutHotkey(new LayoutName("Ops"), new ShortcutKey(CtrlAlt, 0x70));

        Assert.DoesNotContain(ShortcutAction.NextTab, Workspace.ShortcutBindings.Keys);
        var layouts = _platform.LayoutStore.Layouts;
        Assert.Null(layouts.Single(l => l.Name.Value == "Dev").Hotkey);
        Assert.Equal("Ctrl+Alt+F1", layouts.Single(l => l.Name.Value == "Ops").Hotkey);
    }

    [Fact]
    public void BindingAnActionToALayoutsKey_TakesTheKeyFromTheLayout()
    {
        StoreLayout("Dev", "Ctrl+Alt+F1");
        Workspace.Start();
        var bindings = Workspace.ShortcutBindings.ToDictionary();
        bindings[ShortcutAction.NextTab] = new ShortcutKey(CtrlAlt, 0x70);

        Workspace.SetShortcuts(bindings);

        Assert.Null(_platform.LayoutStore.Layouts.Single().Hotkey);
        // With no group focused the action does not apply, and the layout no longer opens on the key.
        Assert.False(_platform.KeyboardFake.Press(0x70, CtrlAlt));
        Assert.Single(_shell.Frames);
    }

    [Fact]
    public void BindShortcut_TakesTheKeyFromAnotherActionAndFromALayout()
    {
        StoreLayout("Dev", "Ctrl+Alt+F1");
        Workspace.Start();
        var nextTabKey = Shortcuts.Defaults[ShortcutAction.NextTab];

        Workspace.BindShortcut(ShortcutAction.PreviousTab, nextTabKey);
        Workspace.BindShortcut(ShortcutAction.NextTab, new ShortcutKey(CtrlAlt, 0x70 /* F1 */));

        Assert.Equal(nextTabKey, Workspace.ShortcutBindings[ShortcutAction.PreviousTab]);
        Assert.Equal(new ShortcutKey(CtrlAlt, 0x70), Workspace.ShortcutBindings[ShortcutAction.NextTab]);
        Assert.Null(_platform.LayoutStore.Layouts.Single().Hotkey);
    }

    [Fact]
    public void SavingAGroupAgain_KeepsTheHotkeyOfTheLayout()
    {
        StoreLayout("Dev", "Ctrl+Alt+1");
        Workspace.Start(new LayoutName("Dev"));

        _shell.Frames[0].UserSavesLayout();

        Assert.Equal("Ctrl+Alt+1", _platform.LayoutStore.Layouts.Single().Hotkey);
    }

    [Fact]
    public void CreateLayoutShortcut_WritesTheFileAndTellsTheUser()
    {
        Workspace.Start();

        Workspace.CreateLayoutShortcut(new LayoutName("Dev"));

        Assert.Equal(["Dev"], _platform.ShortcutsFake.Created);
        Assert.Contains("Dockyard - Dev", Assert.Single(_shell.Notifications));
    }

    [Fact]
    public void ANewWindowOfAWaitedForAppFillsTheWaitingTabOnceItHasATitle()
    {
        Workspace.Start();
        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj")));
        var frame = _shell.Frames[1];
        Assert.Empty(frame.Controller!.Members);

        _platform.Desktop.Add(Handle(7), Initial);
        _platform.Desktop.Windows[Handle(7)].ProcessName = "code";
        _platform.Events.Raise(WindowEvent.Created, Handle(7));
        _platform.Events.Raise(WindowEvent.Shown, Handle(7));
        Assert.Empty(frame.Controller.Members);

        _platform.CatalogFake.Windows.Add(new WindowInfo(Handle(7), "proj", new ProcessName("code"), true));
        _platform.Events.Raise(WindowEvent.TitleChanged, Handle(7));

        Assert.Contains(Handle(7), frame.Controller.Members);
    }

    [Fact]
    public void AWaitingTabIsFilledWhileItsGroupIsMinimized_AndTheWindowJoinsTheHiddenOnes()
    {
        Workspace.Start();
        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj")));
        var frame = _shell.Frames[1];
        frame.Minimize();
        frame.Controller!.OnFrameMinimized();

        _platform.Desktop.Add(Handle(7), Initial, "proj");
        _platform.Desktop.Windows[Handle(7)].ProcessName = "code";
        _platform.CatalogFake.Windows.Add(new WindowInfo(Handle(7), "proj", new ProcessName("code"), true));
        _platform.Events.Raise(WindowEvent.Created, Handle(7));
        _platform.Events.Raise(WindowEvent.Shown, Handle(7));

        Assert.Contains(Handle(7), frame.Controller.Members);
        Assert.False(_platform.Desktop.IsVisible(Handle(7)));

        frame.Restore();
        frame.Controller.OnFrameRestored();
        Assert.True(_platform.Desktop.IsVisible(Handle(7)));
        Assert.Equal(frame.Controller.Layout!.Panes[0].Content.Width, _platform.Desktop.GetVisibleBounds(Handle(7)).Width);
    }

    [Fact]
    public void AWindowThatWasAlreadyOpenDoesNotFillAWaitingTab()
    {
        Workspace.Start();
        Workspace.OpenLayout(Saved("Dev", new SavedTab(new ProcessName("code"), "proj")));
        Add(7, "code", "proj");

        _platform.Events.Raise(WindowEvent.Shown, Handle(7));

        Assert.Empty(_shell.Frames[1].Controller!.Members);
    }

    // ---- window events ----------------------------------------------------------------------------

    [Fact]
    public void ADestroyedMemberLeavesItsGroup()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);

        _platform.Desktop.Close(Handle(1));
        _platform.Events.Raise(WindowEvent.Destroyed, Handle(1));

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
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        _platform.InputFake.IsLeftButtonDown = true;

        _scheduler.FireTimers();
        Assert.Null(_platform.SessionStore.Session);

        _platform.InputFake.IsLeftButtonDown = false;
        _scheduler.FireTimers();

        var saved = Assert.Single(_platform.SessionStore.Session!);
        Assert.Equal("Group 1", saved.Name.Value);
        Assert.Single(saved.Layout.Tabs!);
        Assert.Single(_platform.SessionStore.LastGroups!);
    }

    [Fact]
    public void SavedLayoutsAndLastGroupsKeepNoWindowHandlesOrOriginalBounds_ButTheSessionDoes()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);

        frame.UserSavesLayout();
        _scheduler.FireTimers();

        // Handles are reused and the original place is that of one run: only the crash session needs them.
        var layoutTab = Assert.Single(Assert.Single(_platform.LayoutStore.Layouts).Layout.Tabs!);
        Assert.Equal(WindowHandle.None, layoutTab.Handle);
        Assert.Null(layoutTab.OriginalBounds);
        var lastTab = Assert.Single(Assert.Single(_platform.SessionStore.LastGroups!).Layout.Tabs!);
        Assert.Equal(WindowHandle.None, lastTab.Handle);
        Assert.Null(lastTab.OriginalBounds);
        var sessionTab = Assert.Single(Assert.Single(_platform.SessionStore.Session!).Layout.Tabs!);
        Assert.Equal(Handle(1), sessionTab.Handle);
        Assert.Equal(Initial, sessionTab.OriginalBounds);
    }

    [Fact]
    public void OpenLayout_ReturnsWindowsToWhereTheyAreNow_NotToAnOlderSavedPlace()
    {
        Add(1, "code", "proj");
        Workspace.Start();
        // A layout file written before handles and places were left out of it.
        var old = Saved("Dev", new SavedTab(new ProcessName("code"), "proj", Handle: Handle(1), OriginalBounds: new PixelRect(900, 900, 1300, 1200)));

        Workspace.OpenLayout(old);
        _shell.Frames[1].Controller!.Remove(Handle(1));

        Assert.Equal(Initial, _platform.Desktop.GetBounds(Handle(1)));
    }

    [Fact]
    public void RenamingAGroupIsWrittenToTheSessionAndTheLastGroups()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        _scheduler.FireTimers();
        Assert.Equal("Group 1", Assert.Single(_platform.SessionStore.Session!).Name.Value);

        frame.UserRenames("Work");
        _scheduler.FireTimers();

        Assert.Equal("Work", Assert.Single(_platform.SessionStore.Session!).Name.Value);
        Assert.Equal("Work", Assert.Single(_platform.SessionStore.LastGroups!).Name.Value);
    }

    [Fact]
    public void WhenWindowsEndsTheSession_TheAppsClosingDoNotOverwriteTheGroupsToReopen()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        _scheduler.FireTimers();

        Workspace.SessionEnding();
        _platform.Desktop.Close(Handle(1));
        _platform.Events.Raise(WindowEvent.Destroyed, Handle(1));
        _scheduler.FireTimers();

        var tab = Assert.Single(Assert.Single(_platform.SessionStore.LastGroups!).Layout.Tabs!);
        Assert.Equal("app", tab.ProcessName.Value);
    }

    [Fact]
    public void WhenTheSessionEndIsCancelled_TheGroupsAreWrittenAgain()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        Workspace.SessionEnding();
        _scheduler.FireTimers();

        // Dockyard is still running seconds later: the session did not end.
        _platform.Desktop.Close(Handle(1));
        _platform.Events.Raise(WindowEvent.Destroyed, Handle(1));
        _scheduler.FireTimers();
        _scheduler.FireTimers();

        Assert.Empty(_platform.SessionStore.LastGroups!);
    }

    [Fact]
    public void Stop_PutsMembersBackAndRemovesTheSession()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        _platform.Desktop.PlaceVisible(Handle(1), new PixelRect(300, 300, 500, 500));

        Workspace.Stop();

        Assert.Equal(Initial, _platform.Desktop.GetBounds(Handle(1)));
        Assert.True(_platform.SessionStore.SessionCleared > 0);
        Assert.Null(_platform.KeyboardFake.KeyPressed);
    }

    [Fact]
    public void Quit_KeepsTheOpenGroupsForTheNextStartAndClosesThemAll()
    {
        Add(1);
        Workspace.Start();
        var frame = _shell.Frames[0];
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);

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
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        frame.Controller.Adopt(Handle(2), PaneOf(frame), null);
        _platform.Desktop.Focus(Handle(2));

        var taken = _platform.KeyboardFake.Press(0x25 /* Left */, CtrlAlt);
        _scheduler.RunPosted();

        Assert.True(taken);
        Assert.Equal(Handle(1), PaneOf(frame).ActiveWindow);
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
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        _platform.Desktop.Focus(Handle(1));

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
        Assert.Equal("Group 2", _shell.Frames[1].GroupName.Value);
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
        source.Controller!.Adopt(Handle(1), PaneOf(source), null);
        source.Controller.Adopt(Handle(2), PaneOf(source), null);
        // Both frames lie on the same spot; the target is the one on top.
        _platform.Desktop.BringToTop(target.Handle);
        _platform.InputFake.Cursor = (600, 500);

        source.UserStartsTabDrag(Handle(2));
        Assert.True(_shell.Guide.Shown > 0);
        source.UserEndsTabDrag(false);

        Assert.Contains(Handle(2), target.Controller!.Members);
        Assert.DoesNotContain(Handle(2), source.Controller.Members);
    }

    [Fact]
    public void ACancelledTabDragChangesNothing()
    {
        Add(1);
        Add(2);
        Workspace.Start();
        Workspace.NewGroup();
        var (source, target) = (_shell.Frames[0], _shell.Frames[1]);
        source.Controller!.Adopt(Handle(1), PaneOf(source), null);
        source.Controller.Adopt(Handle(2), PaneOf(source), null);
        _platform.Desktop.BringToTop(target.Handle);
        _platform.InputFake.Cursor = (600, 500);

        source.UserStartsTabDrag(Handle(2));
        source.UserEndsTabDrag(true);

        Assert.Empty(target.Controller!.Members);
        Assert.Contains(Handle(2), source.Controller.Members);
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

        Workspace.SetTitleBarApps([new ProcessName("chrome"), new ProcessName("slack")]);

        Assert.Equal([new ProcessName("chrome"), new ProcessName("slack")], _platform.SettingsStore.Settings.TitleBarApps);
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
        frame.Controller!.Adopt(Handle(1), PaneOf(frame), null);
        frame.Controller.Adopt(Handle(2), PaneOf(frame), null);
        frame.UserActivates();
        _platform.Desktop.Focus(Handle(1));

        Workspace.ReturnFocusToGroup();

        Assert.Equal(frame.Controller.LastActive, _platform.Desktop.Focused);
    }
}
