using Dockyard.Domain;
using Dockyard.Application.Internal;

namespace Dockyard.Application.Tests;

public class GroupControllerTests
{
    private static readonly WindowHandle Frame = new(1000), Other = new(900), Dialog = new(950);
    private static readonly WindowHandle A = new(1), B = new(2), C = new(3);
    private static readonly PixelRect Initial = new(10, 10, 610, 410);

    private readonly FakeWindowSystem _ws = new();
    private readonly FakeView _view = new(Frame);
    private readonly ManualScheduler _scheduler = new();
    private readonly GroupController _c;

    public GroupControllerTests()
    {
        _ws.AddToZOrder(Frame);
        _c = new GroupController(_view, _ws, _scheduler);
        _view.Connect(_c, _scheduler);
        _c.OnSurfaceResized();
    }

    private Pane RootPane => _c.Layout!.Panes[0].Pane;

    private PaneLayout PaneOf(WindowHandle w) => _c.Layout!.Panes.First(p => p.Pane.Windows.Contains(w));

    private WindowHandle Window(WindowHandle h, int minWidth = 0)
    {
        _ws.Add(h, Initial, $"w{h}");
        _ws.Windows[h].MinWidth = minWidth;
        return h;
    }

    private void AssertPlaced(WindowHandle w) => Assert.Equal(_view.ToScreen(PaneOf(w).Content), _ws.GetVisibleBounds(w));

    private void SplitAC()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(C), RootPane, DockSide.Right);
    }

    // ---- adding and removing ----------------------------------------------------------------------

    [Fact]
    public void TitleBars_AreHiddenOnAdoptionAndRestoredOnRelease()
    {
        _c.Adopt(Window(A), RootPane, null);
        Assert.False(_ws.Windows[A].TitleBarVisible);

        _c.Remove(A);

        Assert.True(_ws.Windows[A].TitleBarVisible);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Theory]
    [InlineData(TabBarPlacement.Top)]
    [InlineData(TabBarPlacement.TitleBar)]
    [InlineData(TabBarPlacement.Left)]
    public void TitleBarHover_RevealsOnlyTheHoveredPaneAndHidesAfterLeaving(TabBarPlacement placement)
    {
        SplitAC();
        _c.SetTabBarPlacement(placement);
        var target = _view.ToScreen(PaneOf(A).Content);
        var order = _ws.ZOrder.ToArray();

        _c.UpdateTitleBarHover(target.Right - 20, target.Top + 1, A);
        Assert.True(_ws.Windows[A].TitleBarVisible);
        Assert.False(_ws.Windows[C].TitleBarVisible);
        Assert.Equal(order, _ws.ZOrder);
        AssertPlaced(A);

        _c.UpdateTitleBarHover(target.Right - 20, target.Top + 30, A);
        _scheduler.FireTimers();
        Assert.True(_ws.Windows[A].TitleBarVisible);

        _c.UpdateTitleBarHover(target.Left, target.Bottom - 1, A);
        Assert.True(_ws.Windows[A].TitleBarVisible);
        _scheduler.FireTimers();
        Assert.False(_ws.Windows[A].TitleBarVisible);
    }

    [Fact]
    public void TitleBarHover_ReenteringCancelsDelayedHide()
    {
        _c.Adopt(Window(A), RootPane, null);
        var target = _view.ToScreen(PaneOf(A).Content);
        _c.UpdateTitleBarHover(target.Left + 20, target.Top + 1, A);
        _c.UpdateTitleBarHover(target.Left, target.Bottom - 1, A);
        _c.UpdateTitleBarHover(target.Left + 20, target.Top + 20, A);

        _scheduler.FireTimers();

        Assert.True(_ws.Windows[A].TitleBarVisible);
    }

    [Theory]
    [InlineData(900)]
    [InlineData(0)]
    public void TitleBarHover_DoesNotRevealThroughAnotherWindow(int hit)
    {
        _c.Adopt(Window(A), RootPane, null);
        var target = _view.ToScreen(PaneOf(A).Content);

        _c.UpdateTitleBarHover(target.Left + 20, target.Top + 1, new WindowHandle(hit));

        Assert.False(_ws.Windows[A].TitleBarVisible);
    }

    [Fact]
    public void TitleBarHover_DoesNotHideDuringMemberDrag()
    {
        _c.Adopt(Window(A), RootPane, null);
        var target = _view.ToScreen(PaneOf(A).Content);
        _c.UpdateTitleBarHover(target.Left + 20, target.Top + 1, A);
        _c.UpdateTitleBarHover(target.Left, target.Bottom - 1, A);
        _c.OnMoveSizeStart(A);

        _scheduler.FireTimers();

        Assert.True(_ws.Windows[A].TitleBarVisible);
    }

    [Fact]
    public void TitleBarHover_KeepsKeyboardMenuAnchorVisibleWhileInteracting()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.SetTabBarPlacement(TabBarPlacement.TitleBar);
        _c.RevealTitleBar(A);
        _c.UpdateTitleBarHover(0, 0, Other);

        _c.UpdateTitleBarHover(0, 0, Other, interacting: true);
        _scheduler.FireTimers();
        Assert.True(_c.IsTitleBarVisible(A));
        Assert.True(_ws.Windows[A].TitleBarVisible);

        _c.UpdateTitleBarHover(0, 0, Other);
        _scheduler.FireTimers();
        Assert.False(_c.IsTitleBarVisible(A));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Code")]
    [InlineData("WINDOWSTERMINAL")]
    public void TitleBars_KeepAppsWithCustomTitleControls(string processName)
    {
        Window(A);
        _ws.Windows[A].ProcessName = processName;
        _c.Adopt(A, RootPane, null);

        Assert.True(_ws.Windows[A].TitleBarVisible);

        _c.SetTitleBarApps([]);
        Assert.False(_ws.Windows[A].TitleBarVisible);
        _c.SetTitleBarApps([new ProcessName(processName)]);
        Assert.True(_ws.Windows[A].TitleBarVisible);
    }

    [Fact]
    public void TitleBars_DisablingAutoHideRestoresAllMembers()
    {
        SplitAC();

        _c.SetAutoHideTitleBars(false);
        Assert.All(_ws.Windows.Values, w => Assert.True(w.TitleBarVisible));

        _c.SetAutoHideTitleBars(true);
        Assert.All(_ws.Windows.Values, w => Assert.False(w.TitleBarVisible));
        _c.OnClosing();
        _scheduler.FireTimers();
        Assert.All(_ws.Windows.Values, w => Assert.True(w.TitleBarVisible));
    }

    [Fact]
    public void TitleBars_ChangingAutoHideIsReportedForTheSession()
    {
        var changes = 0;
        _c.Changed += () => changes++;

        _c.SetAutoHideTitleBars(false);
        _c.SetAutoHideTitleBars(false);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void TitleBarHover_ChangingTheDelayKeepsAPendingHide()
    {
        _c.Adopt(Window(A), RootPane, null);
        var target = _view.ToScreen(PaneOf(A).Content);
        _c.UpdateTitleBarHover(target.Left + 20, target.Top + 1, A);
        _c.UpdateTitleBarHover(target.Left, target.Bottom - 1, A);

        _c.SetTitleBarHideDelay(TimeSpan.FromSeconds(1));
        _scheduler.FireTimers();

        Assert.False(_ws.Windows[A].TitleBarVisible);
    }

    [Fact]
    public void TitleBars_AreRestoredWhenAnAppHidesItsWindow()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.Hide(A);
        _c.OnHidden(A);

        _scheduler.FireTimers();

        Assert.Empty(_c.Members);
        Assert.True(_ws.Windows[A].TitleBarVisible);
        Assert.False(_ws.IsVisible(A));
    }

    [Theory]
    [InlineData(DockSide.Left)]
    [InlineData(DockSide.Right)]
    [InlineData(DockSide.Top)]
    [InlineData(DockSide.Bottom)]
    public void Dock_SplitsExistingTabAndPreservesOriginalBounds(DockSide side)
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        Assert.True(_c.Dock(A, RootPane, side));
        Assert.Equal(2, _c.Layout!.Panes.Count);
        AssertPlaced(A);
        AssertPlaced(B);
        Assert.Equal(2, _c.Members.Count());
        _c.Remove(A);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Fact]
    public void Dock_MovesOnlyTabToAnotherPaneAndCollapsesSource()
    {
        SplitAC();
        var target = PaneOf(C).Pane;
        Assert.True(_c.Dock(A, target, null));
        Assert.Single(_c.Layout!.Panes);
        Assert.Equal(new[] { C, A }, target.Windows);
        AssertPlaced(A);
        _c.Remove(A);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Fact]
    public void Dock_OnlyTabOnItsOwnPaneDoesNotCreateEmptySplit()
    {
        _c.Adopt(Window(A), RootPane, null);
        Assert.True(_c.Dock(A, RootPane, DockSide.Right));
        Assert.Single(_c.Layout!.Panes);
        Assert.Single(RootPane.Windows);
    }

    [Fact]
    public void Dock_TransfersBetweenGroupsAndKeepsOriginalBounds()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _c.ActivateTab(A);
        var view = new FakeView(new WindowHandle(2000));
        _ws.AddToZOrder(view.Handle);
        var target = new GroupController(view, _ws, _scheduler);
        target.OnSurfaceResized();
        Assert.True(target.Dock(A, target.Layout!.Panes[0].Pane, null, _c));
        Assert.False(_c.Owns(A));
        Assert.True(target.Owns(A));
        Assert.Equal(B, RootPane.ActiveWindow);
        Assert.True(_ws.Z(B) < _ws.Z(Frame));
        AssertPlaced(B);
        target.Remove(A);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Fact]
    public void Dock_RefusesStalePaneWithoutRemovingSource()
    {
        _c.Adopt(Window(A), RootPane, null);
        Assert.False(_c.Dock(A, new Pane(), null));
        Assert.True(_c.Owns(A));
        Assert.Single(RootPane.Windows);
    }

    [Fact]
    public void Dock_CancelResynchronizesTheDraggedWindow()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.BeginDockDrag(A);
        _ws.SetBounds(A, Initial);
        _c.OnFrameMoved();
        Assert.Equal(Initial, _ws.GetBounds(A));
        _c.EndDockDrag();
        AssertPlaced(A);
    }

    [Fact]
    public void Adopt_RefusesPaneOutsideTheLayout()
    {
        Assert.False(_c.Adopt(Window(A), new Pane(), null));
        Assert.Empty(_c.Members);
    }

    [Fact]
    public void Dock_ExternalWindowRestoresBoundsFromBeforeDrag()
    {
        Window(A);
        _ws.SetBounds(A, new PixelRect(300, 300, 900, 700));
        Assert.True(_c.Dock(A, RootPane, null, originalBounds: Initial));
        _c.Remove(A);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Fact]
    public void Adopt_PlacesWindowOverItsPaneAboveTheFrame()
    {
        Assert.True(_c.Adopt(Window(A), RootPane, null));

        AssertPlaced(A);
        Assert.True(_ws.Z(A) < _ws.Z(Frame));
        Assert.Equal("w1", _view.LastRender!.ActiveTitle);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Adopt_RestoresMinimizedOrMaximizedWindowFirst(bool minimized, bool maximized)
    {
        Window(A);
        _ws.Windows[A].Minimized = minimized;
        _ws.Windows[A].Maximized = maximized;

        _c.Adopt(A, RootPane, null);

        Assert.False(_ws.IsMinimized(A));
        Assert.False(_ws.IsMaximized(A));
        AssertPlaced(A);
    }

    [Fact]
    public void Adopt_RefusesWindowThatIsGoneOrAlreadyMember()
    {
        Assert.False(_c.Adopt(new WindowHandle(77), RootPane, null));
        _c.Adopt(Window(A), RootPane, null);
        Assert.False(_c.Adopt(A, RootPane, null));
    }

    [Fact]
    public void Remove_PutsWindowBackWhereItWas()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Remove(A);

        Assert.False(_c.Owns(A));
        Assert.Equal(Initial, _ws.GetBounds(A));
        Assert.True(_ws.IsVisible(A));
    }

    // ---- attention ----------------------------------------------------------------------------------

    [Fact]
    public void Flashing_MarksTheTabUntilItIsActivated()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _ws.Focus(B);

        _c.OnFlashed(A);
        Assert.Contains(A, _view.LastRender!.Attention);

        _c.ActivateTab(A);
        Assert.Empty(_view.LastRender!.Attention);
    }

    [Fact]
    public void Flashing_IsClearedWhenTheWindowComesToTheForeground()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _ws.Focus(B);
        _c.OnFlashed(A);

        _ws.Focus(A);
        _c.OnForeground(A);

        Assert.Empty(_view.LastRender!.Attention);
    }

    [Fact]
    public void Flashing_OfTheWindowInUseMarksNothing()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.Focus(A);

        _c.OnFlashed(A);

        Assert.Empty(_view.LastRender!.Attention);
    }

    [Fact]
    public void Flashing_MarkLeavesWithTheWindow()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _ws.Focus(B);
        _c.OnFlashed(A);

        _c.Remove(A);

        Assert.Empty(_view.LastRender!.Attention);
    }

    [Fact]
    public void RemoveOthers_ReleasesTheOtherTabsOfThePaneOnly()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _c.Adopt(Window(C), RootPane, DockSide.Right);

        _c.RemoveOthers(A);

        Assert.True(_c.Owns(A));
        Assert.False(_c.Owns(B));
        Assert.True(_c.Owns(C));
        Assert.Equal(Initial, _ws.GetBounds(B));
    }

    [Fact]
    public void CloseWindow_AsksTheAppAndKeepsTheTabUntilTheWindowIsGone()
    {
        _c.Adopt(Window(A), RootPane, null);

        _c.CloseWindow(A);

        Assert.Equal([A], _ws.CloseRequests);
        Assert.True(_c.Owns(A));
    }

    [Fact]
    public void MoveTab_SplitsTheTabOffItsPane()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);

        _c.MoveTab(B, DockSide.Bottom);

        Assert.Equal(2, _c.Layout!.Panes.Count);
        Assert.NotEqual(PaneOf(A).Pane, PaneOf(B).Pane);
    }

    [Fact]
    public void Remove_KeepsAnAlwaysOnTopWindowAlwaysOnTop()
    {
        Window(A);
        _ws.Windows[A].Topmost = true;
        _c.Adopt(A, RootPane, null);
        // Stacking it among the group's windows puts it in the normal band.
        Assert.False(_ws.IsTopmost(A));

        _c.Remove(A);

        Assert.True(_ws.IsTopmost(A));
    }

    [Fact]
    public void Remove_LeavesAnOrdinaryWindowOutOfTheTopmostBand()
    {
        _c.Adopt(Window(A), RootPane, null);

        _c.Remove(A);

        Assert.False(_ws.IsTopmost(A));
    }

    [Fact]
    public void Closing_PutsEveryWindowBack()
    {
        SplitAC();
        _c.OnClosing();

        Assert.Equal(Initial, _ws.GetBounds(A));
        Assert.Equal(Initial, _ws.GetBounds(C));
        Assert.Empty(_c.Members);
    }

    [Fact]
    public void MemberAppExits_PaneCollapsesAndTheRestFillsTheFrame()
    {
        SplitAC();
        _ws.Close(C);
        _c.OnDestroyed(C);

        Assert.Single(_c.Layout!.Panes);
        AssertPlaced(A);
        Assert.Equal((int)_view.Surface.Width, _ws.GetVisibleBounds(A).Width);
    }

    // ---- tabs and splits --------------------------------------------------------------------------

    [Fact]
    public void AddingTab_NewTabInFrontOfFrame_PreviousTabBehindIt()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);

        Assert.True(_ws.Z(B) < _ws.Z(Frame));
        Assert.True(_ws.Z(Frame) < _ws.Z(A));
        AssertPlaced(A);
        AssertPlaced(B);
    }

    [Fact]
    public void ClickingTab_BringsItInFrontOfFrame()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);

        _c.ActivateTab(A);

        Assert.True(_ws.Z(A) < _ws.Z(Frame));
        Assert.True(_ws.Z(Frame) < _ws.Z(B));
        Assert.Equal("w1", _view.LastRender!.ActiveTitle);
    }

    [Fact]
    public void Split_PanesSideBySideWithoutOverlap_BothInFrontOfFrame()
    {
        SplitAC();

        AssertPlaced(A);
        AssertPlaced(C);
        Assert.True(_ws.GetVisibleBounds(A).Right <= _ws.GetVisibleBounds(C).Left);
        Assert.True(_ws.Z(A) < _ws.Z(Frame) && _ws.Z(C) < _ws.Z(Frame));
    }

    // ---- z-order --------------------------------------------------------------------------------

    [Fact]
    public void MemberActivated_BringsWholeGroupInFrontOfOtherApps()
    {
        SplitAC();
        _ws.Add(Other, Initial);            // another app now covers the group
        _ws.BringToTop(C);                  // the user clicks C

        _c.OnForeground(C);

        Assert.Equal(0, _ws.Z(C));
        Assert.True(_ws.Z(A) < _ws.Z(Frame));
        Assert.True(_ws.Z(Frame) < _ws.Z(Other));
    }

    [Fact]
    public void InactiveTabActivated_BecomesTheActiveTab()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);

        _c.OnForeground(A);

        Assert.Equal(A, RootPane.ActiveWindow);
        Assert.True(_ws.Z(Frame) < _ws.Z(B));
    }

    [Fact]
    public void FrameComingForward_TakesTheActiveTabsAlong()
    {
        SplitAC();
        _ws.Add(Other, Initial);

        var after = _c.OnFrameZChanging(new FrameZChange(ZPlacement.Top));
        // The system then puts the frame where the controller asked.
        Assert.NotNull(after);
        _ws.Stack(after.Value, [Frame]);

        Assert.True(_ws.Z(A) < _ws.Z(Frame) && _ws.Z(C) < _ws.Z(Frame));
        Assert.True(_ws.Z(Frame) < _ws.Z(Other));
        Assert.Equal(_ws.Z(Frame) - 1, Math.Max(_ws.Z(A), _ws.Z(C)));
    }

    [Fact]
    public void FramePlacedBelowAnotherApp_TabsGoThereToo()
    {
        SplitAC();
        _ws.Add(Other, Initial);

        var after = _c.OnFrameZChanging(new FrameZChange(ZPlacement.Below, Other));
        Assert.NotNull(after);
        _ws.Stack(after.Value, [Frame]);

        Assert.Equal(0, _ws.Z(Other));
        Assert.True(_ws.Z(A) < _ws.Z(Frame) && _ws.Z(C) < _ws.Z(Frame));
        Assert.True(_ws.Z(Other) < _ws.Z(A) && _ws.Z(Other) < _ws.Z(C));
    }

    [Fact]
    public void DragEndWithoutStart_IsReadAgainstTheLastPlacement()
    {
        // Move/size start can be missed (e.g. it arrived before the window joined); the end still applies.
        SplitAC();
        var a = _ws.GetVisibleBounds(A).Width;
        _ws.Windows[A].Bounds = FakeWindowSystem.BoundsFor(_ws.GetVisibleBounds(A) with { Right = _ws.GetVisibleBounds(A).Right + 50 });

        _c.OnMoveSizeEnd(A);

        Assert.InRange(_ws.GetVisibleBounds(A).Width, a + 48, a + 52);
        AssertPlaced(C);
    }

    [Theory]
    [InlineData(ZPlacement.Bottom)]
    [InlineData(ZPlacement.Topmost)]
    [InlineData(ZPlacement.NoTopmost)]
    public void FrameZChange_ToSpecialPlaces_IsLeftAlone(ZPlacement placement)
    {
        SplitAC();
        Assert.Null(_c.OnFrameZChanging(new FrameZChange(placement)));
    }

    [Fact]
    public void FrameZChange_RelativeToOwnTab_IsLeftAlone()
    {
        SplitAC();
        Assert.Null(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Below, A)));
    }

    [Fact]
    public void FrameBeingHidden_IsLeftAlone()
    {
        SplitAC();
        Assert.Null(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top, Hiding: true)));
    }

    [Fact]
    public void WhilePickerIsOpen_TabsStayBelowIt()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.AddToZOrder(Dialog);
        _view.ZAnchor = Dialog;

        _c.PickerCancelled();

        Assert.Equal(0, _ws.Z(Dialog));
        Assert.True(_ws.Z(Dialog) < _ws.Z(A) && _ws.Z(A) < _ws.Z(Frame));
    }

    [Fact]
    public void TabOverlay_StaysAboveTheActiveTabs()
    {
        var overlay = new WindowHandle(960);
        _ws.AddToZOrder(overlay);
        _view.TabOverlay = overlay;
        SplitAC();
        _ws.BringToTop(Other);

        Assert.NotNull(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top)));
        Assert.Equal(0, _ws.Z(overlay));
        Assert.True(_ws.Z(overlay) < _ws.Z(A) && _ws.Z(overlay) < _ws.Z(C));

        _ws.BringToTop(A);
        _c.OnForeground(A);
        Assert.Equal(0, _ws.Z(overlay));
        Assert.True(_ws.Z(A) < _ws.Z(Frame));
    }

    [Theory]
    [InlineData(TabBarPlacement.TitleBar)]
    [InlineData(TabBarPlacement.Left)]
    public void TabBarPlacement_PlacesMembersInTheNewContentArea(TabBarPlacement placement)
    {
        SplitAC();
        _c.SetTabBarPlacement(placement);
        AssertPlaced(A);
        AssertPlaced(C);
        if (placement == TabBarPlacement.TitleBar) Assert.Equal(PaneOf(A).Bounds, PaneOf(A).Content);
        else Assert.Equal(PaneOf(A).Bounds.X + _c.Metrics.TabColumnWidth, PaneOf(A).Content.X);
    }

    [Fact]
    public void TitleBarPlacement_PanesWithATitleBarAppKeepTabsAbove()
    {
        SplitAC();
        _ws.Windows[C].ProcessName = "msedge";
        // The process name is read when a window joins.
        _c.Remove(C);
        _c.Adopt(C, PaneOf(A).Pane, DockSide.Right);
        _c.SetTabBarPlacement(TabBarPlacement.TitleBar);
        _c.SetTitleBarApps([new ProcessName("MSEdge")]);

        Assert.Equal(TabBarPlacement.TitleBar, PaneOf(A).Placement);
        Assert.Equal(TabBarPlacement.Top, PaneOf(C).Placement);
        Assert.Equal(PaneOf(C).Bounds.Y + _c.Metrics.TabBarHeight, PaneOf(C).Content.Y);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void LeftPlacement_DraggingLeftEdgeOfRightPane_MovesTheDivider()
    {
        SplitAC();
        _c.SetTabBarPlacement(TabBarPlacement.Left);
        var splitter = _c.Layout!.Splitters.Single().Bounds.X;

        UserDrags(C, r => r with { Left = r.Left - 50 });

        Assert.InRange(_c.Layout!.Splitters.Single().Bounds.X, splitter - 52, splitter - 48);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    // ---- keyboard shortcuts -----------------------------------------------------------------------

    [Theory]
    [InlineData(ShortcutAction.NextTab, 1, 2)]
    [InlineData(ShortcutAction.NextTab, 2, 1)]
    [InlineData(ShortcutAction.PreviousTab, 1, 2)]
    public void TabShortcuts_CycleWithinThePane(ShortcutAction action, int from, int expected)
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _c.ActivateTab(new WindowHandle(from));

        _c.Execute(action, new WindowHandle(from));

        Assert.Equal(new WindowHandle(expected), RootPane.ActiveWindow);
        Assert.Equal(new WindowHandle(expected), _ws.Focused);
        Assert.True(_ws.Z(new WindowHandle(expected)) < _ws.Z(Frame));
    }

    [Fact]
    public void PaneShortcuts_FocusTheNeighbour()
    {
        SplitAC();

        _c.Execute(ShortcutAction.FocusRight, A);
        Assert.Equal(C, _ws.Focused);
        _c.Execute(ShortcutAction.FocusRight, C);
        Assert.Equal(C, _ws.Focused);
        _c.Execute(ShortcutAction.FocusLeft, C);
        Assert.Equal(A, _ws.Focused);
        Assert.Equal(A, _c.LastActive);
    }

    [Fact]
    public void TabNumberShortcuts_GoToThatTab_AndTheLastNumberToTheLastTab()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);
        _c.Adopt(Window(C), RootPane, null);

        _c.Execute(ShortcutAction.Tab1, C);
        Assert.Equal(A, RootPane.ActiveWindow);
        _c.Execute(ShortcutAction.Tab9, A);
        Assert.Equal(C, RootPane.ActiveWindow);
        Assert.Equal(C, _ws.Focused);
        _c.Execute(ShortcutAction.Tab5, C);
        Assert.Equal(C, RootPane.ActiveWindow);
    }

    [Fact]
    public void MoveTabShortcut_MovesTheTabIntoTheNeighbourPane()
    {
        SplitAC();

        _c.Execute(ShortcutAction.MoveTabRight, A);

        Assert.Equal([C, A], Assert.Single(_c.Layout!.Panes).Pane.Windows);
        Assert.Equal(A, _ws.Focused);
        AssertPlaced(A);
    }

    [Fact]
    public void MoveTabShortcut_WithoutANeighbour_SplitsTheTabOff()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, null);

        _c.Execute(ShortcutAction.MoveTabDown, B);

        Assert.Equal(2, _c.Layout!.Panes.Count);
        Assert.True(PaneOf(B).Bounds.Y > PaneOf(A).Bounds.Y);
        // A lone tab has nowhere to go.
        _c.Execute(ShortcutAction.MoveTabDown, B);
        Assert.Equal(2, _c.Layout!.Panes.Count);
    }

    [Fact]
    public void PaneSizeShortcuts_MoveTheDividerForThatPane()
    {
        SplitAC();
        var width = PaneOf(A).Bounds.Width;

        _c.Execute(ShortcutAction.GrowPane, A);
        Assert.True(PaneOf(A).Bounds.Width > width);
        _c.Execute(ShortcutAction.GrowPane, C);
        _c.Execute(ShortcutAction.GrowPane, C);
        Assert.True(PaneOf(A).Bounds.Width < width);
        _c.Execute(ShortcutAction.ShrinkPane, C);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void RemoveShortcut_ReleasesTheWindowToItsOriginalBounds()
    {
        SplitAC();

        _c.Execute(ShortcutAction.RemoveFromGroup, C);

        Assert.False(_c.Owns(C));
        Assert.Equal(Initial, _ws.GetBounds(C));
        Assert.Equal(C, _ws.Focused);
        AssertPlaced(A);
    }

    [Theory]
    [InlineData(0x25, false, ShortcutAction.PreviousTab)]
    [InlineData(0x27, true, ShortcutAction.FocusRight)]
    [InlineData(0x28, true, ShortcutAction.FocusDown)]
    [InlineData(0x57, false, ShortcutAction.RemoveFromGroup)]
    [InlineData(0x26, false, ShortcutAction.MaximizeGroup)]
    [InlineData(0x57, true, ShortcutAction.CloseGroup)]
    [InlineData(0x31, false, ShortcutAction.Tab1)]
    [InlineData(0x54, false, ShortcutAction.AddTab)]
    [InlineData(0x56, false, ShortcutAction.SplitRight)]
    [InlineData(0x48, false, ShortcutAction.SplitDown)]
    [InlineData(0x4F, false, ShortcutAction.OpenLayout)]
    [InlineData(0x4E, false, ShortcutAction.NewGroup)]
    [InlineData(0x4C, false, ShortcutAction.ManageLayouts)]
    [InlineData(0xBC, false, ShortcutAction.OpenSettings)]
    [InlineData(0x51, true, ShortcutAction.Quit)]
    [InlineData(0x51, false, null)]
    [InlineData(0x31, true, null)]
    [InlineData(0x41, false, null)]
    public void Shortcuts_DefaultsMapKeys(int key, bool shift, ShortcutAction? expected)
    {
        var modifiers = KeyModifiers.Ctrl | KeyModifiers.Alt | (shift ? KeyModifiers.Shift : 0);
        Assert.Equal(expected, Shortcuts.Match(Shortcuts.Defaults, key, modifiers));
    }

    [Fact]
    public void Shortcuts_DefaultsLeaveCtrlShiftNToTheApps()
    {
        Assert.Null(Shortcuts.Match(Shortcuts.Defaults, 0x4E, KeyModifiers.Ctrl | KeyModifiers.Shift));
    }

    [Fact]
    public void Shortcuts_EveryDefaultHoldsCtrlAndAlt()
    {
        Assert.All(Shortcuts.Defaults.Values, key =>
            Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt, key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)));
    }

    [Fact]
    public void Shortcuts_AKeyTheUserChoseWinsOverANewDefault()
    {
        var bindings = Shortcuts.Load(new Dictionary<string, string> { ["NextTab"] = "Ctrl+Alt+R" });

        Assert.Equal(ShortcutAction.NextTab, Shortcuts.Match(bindings, 0x52, KeyModifiers.Ctrl | KeyModifiers.Alt));
        Assert.False(bindings.ContainsKey(ShortcutAction.RenameGroup));
        Assert.True(bindings.ContainsKey(ShortcutAction.ChangeTabBar));
    }

    [Fact]
    public void Shortcuts_DefaultsAreAllDistinctAndNamed()
    {
        Assert.Equal(Shortcuts.Defaults.Count, Shortcuts.Defaults.Values.Distinct().Count());
        Assert.All(Enum.GetValues<ShortcutAction>(), a => Assert.True(Shortcuts.Names.ContainsKey(a), a.ToString()));
    }

    [Fact]
    public void Shortcuts_SaveLayoutHasNoDefaultKeyButCanBeBound()
    {
        Assert.False(Shortcuts.Defaults.ContainsKey(ShortcutAction.SaveLayout));
        Assert.DoesNotContain("SaveLayout", Shortcuts.Save(Shortcuts.Defaults).Keys);

        var saved = Shortcuts.Save(Shortcuts.Load(new Dictionary<string, string> { ["SaveLayout"] = "Ctrl+Alt+S" }));

        Assert.Equal("Ctrl+Alt+S", saved["SaveLayout"]);
    }

    [Theory]
    [InlineData("Ctrl+Win+Left")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Alt+K")]
    [InlineData("Ctrl+,")]
    [InlineData("Ctrl+0xE9")]
    public void ShortcutKey_RoundTripsThroughText(string text) => Assert.Equal(text, ShortcutKey.Parse(text)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Hyper+K")]
    [InlineData("Ctrl+NoSuchKey")]
    public void ShortcutKey_RejectsUnknownText(string text) => Assert.Null(ShortcutKey.Parse(text));

    [Fact]
    public void Shortcuts_LoadTakesANullBindingAsUnbound()
    {
        // A hand-edited settings file can hold null where an empty text means "unbound".
        var bindings = Shortcuts.Load(new Dictionary<string, string> { ["NextTab"] = null! });

        Assert.False(bindings.ContainsKey(ShortcutAction.NextTab));
        Assert.Equal(Shortcuts.Defaults[ShortcutAction.PreviousTab], bindings[ShortcutAction.PreviousTab]);
    }

    [Fact]
    public void Shortcuts_LoadOverridesDefaultsAndKeepsTheRest()
    {
        var bindings = Shortcuts.Load(new Dictionary<string, string>
        {
            ["NextTab"] = "Alt+Right",
            ["RemoveFromGroup"] = "",
            ["PreviousTab"] = "garbage",
            ["NoSuchAction"] = "Alt+X",
        });

        Assert.Equal(ShortcutAction.NextTab, Shortcuts.Match(bindings, 0x27, KeyModifiers.Alt));
        Assert.Null(Shortcuts.Match(bindings, 0x57, KeyModifiers.Ctrl | KeyModifiers.Alt));
        Assert.Equal(Shortcuts.Defaults[ShortcutAction.PreviousTab], bindings[ShortcutAction.PreviousTab]);
        Assert.Equal(bindings, Shortcuts.Load(Shortcuts.Save(bindings)));
    }

    [Fact]
    public void Shortcuts_SaveLeavesDefaultsOut()
    {
        var bindings = Shortcuts.Defaults.ToDictionary();
        bindings[ShortcutAction.NextTab] = new ShortcutKey(KeyModifiers.Alt, 0x27);
        bindings.Remove(ShortcutAction.RemoveFromGroup);

        Assert.Equal(new Dictionary<string, string> { ["NextTab"] = "Alt+Right", ["RemoveFromGroup"] = "" }, Shortcuts.Save(bindings));
    }

    [Fact]
    public void FrameActivated_RaisesTabsAfterTheSystemIsDone()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.BringToTop(Frame);

        _c.OnFrameActivated();
        Assert.True(_ws.Z(Frame) < _ws.Z(A)); // not yet: deferred
        _scheduler.RunPosted();

        Assert.True(_ws.Z(A) < _ws.Z(Frame));
    }

    // ---- dragging members ---------------------------------------------------------------------

    private void UserDrags(WindowHandle w,Func<PixelRect, PixelRect> change)
    {
        _c.OnMoveSizeStart(w);
        _ws.Windows[w].Bounds = FakeWindowSystem.BoundsFor(change(_ws.GetVisibleBounds(w)));
        _c.OnLocationChanged(w);
        _scheduler.RunPosted();
        _c.OnMoveSizeEnd(w);
    }

    [Fact]
    public void DraggingInnerEdge_MovesTheDivider()
    {
        SplitAC();
        var a = _ws.GetVisibleBounds(A).Width;
        var c = _ws.GetVisibleBounds(C).Width;

        UserDrags(A, r => r with { Right = r.Right + 100 });

        Assert.InRange(_ws.GetVisibleBounds(A).Width, a + 98, a + 102);
        Assert.InRange(_ws.GetVisibleBounds(C).Width, c - 102, c - 98);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void DraggingOuterEdge_ResizesTheFrame()
    {
        _c.Adopt(Window(A), RootPane, null);
        var width = _view.Surface.Width;

        UserDrags(A, r => r with { Right = r.Right + 80 });

        Assert.Equal(width + 80, _view.Surface.Width);
        AssertPlaced(A);
    }

    [Fact]
    public void DraggingOuterEdge_GrowsOnlyThePaneBeingDragged()
    {
        SplitAC();
        var a = _ws.GetVisibleBounds(A);
        var c = _ws.GetVisibleBounds(C).Width;

        // The right edge of the right pane, then the left edge of the left pane.
        UserDrags(C, r => r with { Right = r.Right + 80 });
        Assert.Equal(a, _ws.GetVisibleBounds(A));
        Assert.InRange(_ws.GetVisibleBounds(C).Width, c + 79, c + 81);

        var right = _ws.GetVisibleBounds(C);
        UserDrags(A, r => r with { Left = r.Left - 40 });
        Assert.Equal(right, _ws.GetVisibleBounds(C));
        Assert.InRange(_ws.GetVisibleBounds(A).Width, a.Width + 39, a.Width + 41);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void DraggingTitleBar_MovesTheWholeGroup()
    {
        SplitAC();
        var origin = _view.Origin;

        UserDrags(A, r => new PixelRect(r.Left + 50, r.Top + 30, r.Right + 50, r.Bottom + 30));

        Assert.Equal((origin.X + 50, origin.Y + 30), _view.Origin);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void LiveDragUpdates_AreCoalescedIntoOne()
    {
        SplitAC();
        _c.OnMoveSizeStart(A);
        _ws.Windows[A].Bounds = FakeWindowSystem.BoundsFor(_ws.GetVisibleBounds(A) with { Right = _ws.GetVisibleBounds(A).Right + 60 });
        for (var i = 0; i < 5; i++) _c.OnLocationChanged(A);
        var before = _ws.GetVisibleBounds(C);

        _scheduler.RunPosted();

        Assert.NotEqual(before, _ws.GetVisibleBounds(C)); // the neighbour followed the drag
    }

    [Fact]
    public void ReleasingAndReAddingAWindowMidDrag_PlacesItAndDropsTheOldDragUpdate()
    {
        SplitAC();
        _c.OnMoveSizeStart(A);
        _ws.Windows[A].Bounds = FakeWindowSystem.BoundsFor(_ws.GetVisibleBounds(A) with { Right = _ws.GetVisibleBounds(A).Right + 60 });
        _c.OnLocationChanged(A); // a live update is queued
        var origin = _view.Origin;

        // The move/size end has not arrived yet.
        _c.Remove(A);
        Assert.True(_c.Adopt(A, RootPane, null));

        AssertPlaced(A);
        _scheduler.RunPosted();
        AssertPlaced(A);
        Assert.Equal(origin, _view.Origin);
    }

    // ---- settling and minimum sizes -------------------------------------------------------------

    [Fact]
    public void WindowThatKeepsALargerSize_IsLearnedAsMinimum_AndTheDividerMovesForIt()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(C, minWidth: 700), RootPane, DockSide.Right);
        Assert.True(_ws.GetVisibleBounds(C).Width > PaneOf(C).Content.Width); // overflows its pane

        _scheduler.FireTimers(); // first sighting: retried
        _scheduler.FireTimers(); // same size again: learned

        Assert.True(PaneOf(C).Content.Width >= 700);
        AssertPlaced(C);
        Assert.True(_view.MinimumContent.Width >= 700);
    }

    [Fact]
    public void LearnedMinimum_StopsTheDivider()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(C, minWidth: 700), RootPane, DockSide.Right);
        _scheduler.FireTimers();
        _scheduler.FireTimers();

        UserDrags(A, r => r with { Right = r.Right + 500 });

        Assert.True(PaneOf(C).Content.Width >= 700);
    }

    // ---- window states ------------------------------------------------------------------------

    [Fact]
    public void MemberMaximizedByUser_IsPutBackIntoItsPane()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.Windows[A].Maximized = true;
        _ws.Windows[A].Bounds = new PixelRect(0, 0, 1920, 1080);

        _c.OnLocationChanged(A);

        Assert.False(_ws.IsMaximized(A));
        AssertPlaced(A);
    }

    [Fact]
    public void MemberMinimizedByUser_MinimizesTheGroup()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.Windows[A].Minimized = true;

        _c.OnLocationChanged(A);

        Assert.True(_view.Minimized);
    }

    [Fact]
    public void GroupMinimizedAndRestored_HidesThenShowsMembersInPlace()
    {
        SplitAC();
        _view.Minimized = true;
        _c.OnFrameMinimized();
        Assert.False(_ws.IsVisible(A) || _ws.IsVisible(C));

        _view.Minimized = false;
        _c.OnFrameRestored();

        Assert.True(_ws.IsVisible(A) && _ws.IsVisible(C));
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void ActivatingMemberOfMinimizedGroup_RestoresTheGroup()
    {
        _c.Adopt(Window(A), RootPane, null);
        _view.Minimized = true;

        _c.OnForeground(A);

        Assert.False(_view.Minimized);
    }

    [Fact]
    public void MemberMinimizedByUser_StaysMinimizedWhenTheNextMemberReportsTheForegroundLate()
    {
        SplitAC();
        _ws.Windows[A].Minimized = true;
        _c.OnLocationChanged(A);
        _c.OnFrameMinimized();

        // Windows activated C when A was minimized; the report arrives after C was hidden with the group.
        _c.OnForeground(C);

        Assert.True(_view.Minimized);
        Assert.False(_ws.IsVisible(C));
    }

    [Fact]
    public void MemberHiddenByItsApp_LeavesTheGroup()
    {
        SplitAC();
        _ws.Hide(A);

        _c.OnHidden(A);
        _scheduler.FireTimers();

        Assert.False(_c.Owns(A));
        Assert.True(_c.Owns(C));
        Assert.False(_ws.IsVisible(A));
    }

    [Fact]
    public void MemberHiddenOnlyForAMoment_StaysInTheGroup()
    {
        SplitAC();
        _ws.Hide(A);
        _c.OnHidden(A);
        _ws.Show(A);

        _scheduler.FireTimers();

        Assert.True(_c.Owns(A));
    }

    [Fact]
    public void MembersHiddenByMinimizingTheGroup_StayInTheGroup()
    {
        SplitAC();
        _view.Minimized = true;
        _c.OnFrameMinimized();

        _c.OnHidden(A);
        _c.OnHidden(C);
        _scheduler.FireTimers();
        _view.Minimized = false;
        _c.OnFrameRestored();

        Assert.True(_c.Owns(A) && _c.Owns(C));
        Assert.True(_ws.IsVisible(A) && _ws.IsVisible(C));
    }

    [Fact]
    public void TitleChange_UpdatesTabsAndFrameTitle()
    {
        _c.Adopt(Window(A), RootPane, null);
        _ws.Windows[A].Title = "renamed";

        _c.OnTitleChanged(A);

        Assert.Equal("renamed", _view.LastRender!.Titles[A]);
        Assert.Equal("renamed", _view.LastRender.ActiveTitle);
    }

    [Fact]
    public void EventsForWindowsOfOtherGroups_AreIgnored()
    {
        _c.Adopt(Window(A), RootPane, null);
        var z = _ws.ZOrder.ToList();

        _c.OnForeground(Other);
        _c.OnDestroyed(Other);

        Assert.Equal(z, _ws.ZOrder);
        Assert.True(_c.Owns(A));
    }

    // ---- splitters, vertical splits and frame changes -----------------------------------------

    private void SplitAB_Vertical()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(B), RootPane, DockSide.Bottom);
    }

    [Fact]
    public void DraggingSplitter_MovesItAndReplacesBothPanes()
    {
        SplitAC();
        var s = _c.Layout!.Splitters.Single();

        _c.DragSplitter(s, s.SplitBounds.X + 300);

        Assert.InRange(PaneOf(A).Content.Width, 297, 299);
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void DraggingVerticalSplitter_MovesItAlongY()
    {
        SplitAB_Vertical();
        var s = _c.Layout!.Splitters.Single();

        _c.DragSplitter(s, s.SplitBounds.Y + 400);

        Assert.InRange(PaneOf(A).Bounds.Height, 397, 399);
        AssertPlaced(A);
        AssertPlaced(B);
    }

    [Fact]
    public void DraggingSplitter_StopsAtTheMinimum()
    {
        SplitAC();
        var s = _c.Layout!.Splitters.Single();

        _c.DragSplitter(s, s.SplitBounds.X - 500);

        Assert.True(PaneOf(A).Bounds.Width >= _c.Metrics.MinPaneWidth);
    }

    [Fact]
    public void DraggingBottomEdgeOfUpperPane_MovesTheHorizontalDivider()
    {
        SplitAB_Vertical();
        var a = _ws.GetVisibleBounds(A).Height;

        UserDrags(A, r => r with { Bottom = r.Bottom + 60 });

        Assert.InRange(_ws.GetVisibleBounds(A).Height, a + 58, a + 62);
        AssertPlaced(B);
    }

    [Fact]
    public void DraggingTopEdgeOfLowerPane_MovesTheHorizontalDivider()
    {
        SplitAB_Vertical();
        var b = _ws.GetVisibleBounds(B).Height;

        UserDrags(B, r => r with { Top = r.Top - 60 });

        Assert.InRange(_ws.GetVisibleBounds(B).Height, b + 58, b + 62);
        AssertPlaced(A);
    }

    [Fact]
    public void DraggingOuterLeftOrTopEdge_MovesAndResizesTheFrame()
    {
        _c.Adopt(Window(A), RootPane, null);
        var (origin, size) = (_view.Origin, _view.Surface);

        UserDrags(A, r => r with { Left = r.Left - 40 });
        UserDrags(A, r => r with { Top = r.Top - 30 });

        Assert.Equal((origin.X - 40, origin.Y - 30), _view.Origin);
        Assert.Equal(new Size(size.Width + 40, size.Height + 30), _view.Surface);
        AssertPlaced(A);
    }

    [Fact]
    public void Live_KeepsMembersBehindTheFrameUntilItEnds()
    {
        SplitAC();
        _c.BeginLive();
        Assert.True(_ws.Z(Frame) < _ws.Z(A) && _ws.Z(Frame) < _ws.Z(C));

        // Moving, resizing and activating the frame meanwhile neither brings them forward nor stops them following.
        _view.Origin = (300, 100);
        _c.OnFrameMoved();
        _c.OnFrameActivated();
        _scheduler.RunPosted();
        Assert.Null(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top, WindowHandle.None, false)));
        Assert.True(_ws.Z(Frame) < _ws.Z(A) && _ws.Z(Frame) < _ws.Z(C));
        AssertPlaced(A);
        AssertPlaced(C);

        _c.EndLive();
        Assert.True(_ws.Z(A) < _ws.Z(Frame) && _ws.Z(C) < _ws.Z(Frame));
        AssertPlaced(A);
        AssertPlaced(C);
    }

    [Fact]
    public void QueuedStackChanges_DoNotStartAnotherFrameRestack()
    {
        SplitAC();
        _ws.IsStacking = true;

        Assert.Null(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top)));

        _ws.IsStacking = false;
        Assert.NotNull(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top)));
    }

    [Fact]
    public void FrameMovedOrResized_MembersFollow()
    {
        SplitAC();

        _view.Origin = (500, 50);
        _c.OnFrameMoved();
        AssertPlaced(A);
        AssertPlaced(C);

        _view.Surface = new Size(1400, 800);
        _c.OnSurfaceResized();
        AssertPlaced(A);
        AssertPlaced(C);
    }

    // ---- things that must be ignored ----------------------------------------------------------

    [Fact]
    public void QueuedDragUpdate_AfterTheWindowClosed_IsIgnored()
    {
        SplitAC();
        _c.OnMoveSizeStart(A);
        _c.OnLocationChanged(A);
        _ws.Close(A);
        _c.OnDestroyed(A);

        _scheduler.RunPosted();

        AssertPlaced(C);
        Assert.Single(_c.Layout!.Panes);
    }

    [Fact]
    public void EventsAfterClosing_AreIgnored()
    {
        SplitAC();
        _c.OnClosing();
        var z = _ws.ZOrder.ToList();

        _c.OnForeground(A);
        _c.OnLocationChanged(A);
        _c.OnDestroyed(A);
        _scheduler.FireTimers();

        Assert.Equal(z, _ws.ZOrder);
        Assert.Equal(Initial, _ws.GetBounds(A));
    }

    [Fact]
    public void WhileMinimized_NothingIsMovedOrRestacked()
    {
        SplitAC();
        _view.Minimized = true;
        _c.OnFrameMinimized();
        var bounds = _ws.GetBounds(A);
        var z = _ws.ZOrder.ToList();

        _c.OnFrameMoved();
        _c.ActivateTab(C);
        Assert.Null(_c.OnFrameZChanging(new FrameZChange(ZPlacement.Top)));

        Assert.Equal(bounds, _ws.GetBounds(A));
        Assert.Equal(z, _ws.ZOrder);
    }

    [Fact]
    public void OwnRestacking_IsNotTreatedAsASystemChange()
    {
        // The frame's own z-change while the controller is stacking windows must not trigger another stack.
        var view = new ReentrantView(Frame);
        var ws = new FakeWindowSystem();
        ws.AddToZOrder(Frame);
        var c = new GroupController(view, new ReentrantWindowSystem(ws, () => view.Controller!.OnFrameZChanging(new FrameZChange(ZPlacement.Top))), _scheduler);
        view.Controller = c;
        c.OnSurfaceResized();
        ws.Add(A, Initial);

        c.Adopt(A, c.Layout!.Panes[0].Pane, null);

        Assert.True(ws.Z(A) < ws.Z(Frame));
    }

    /// <summary>A view whose z-order changes come back into the controller, as the frame's WndProc does.</summary>
    private sealed class ReentrantView(WindowHandle handle) : IGroupView
    {
        private readonly FakeView _inner = new(handle);
        public GroupController? Controller { get; set; }
        public WindowHandle Handle => _inner.Handle;
        public bool IsOnScreen => true;
        public bool IsMinimized => false;
        public Size SurfaceSize => _inner.SurfaceSize;
        public double Scale => 1;
        public WindowHandle ZAnchor => WindowHandle.None;
        public WindowHandle TabOverlay => WindowHandle.None;
        public PixelRect ToScreen(Rect r) => _inner.ToScreen(r);
        public Rect FromScreen(PixelRect r) => _inner.FromScreen(r);
        public void Render(GroupSnapshot s) { }
        public void MoveBy(double dx, double dy) => _inner.MoveBy(dx, dy);
        public void ResizeEdge(DockSide e, double d) => _inner.ResizeEdge(e, d);
        public void SetMinimumContentSize(Size m) => _inner.SetMinimumContentSize(m);
        public void Minimize() { }
        public void Restore() { }
    }

    /// <summary>Calls back into the controller whenever the frame itself is restacked.</summary>
    private sealed class ReentrantWindowSystem(FakeWindowSystem inner, Action onFrameStacked) : IWindowSystem
    {
        public bool Exists(WindowHandle w) => inner.Exists(w);
        public bool IsVisible(WindowHandle w) => inner.IsVisible(w);
        public bool IsMinimized(WindowHandle w) => inner.IsMinimized(w);
        public bool IsMaximized(WindowHandle w) => inner.IsMaximized(w);
        public bool IsTopmost(WindowHandle w) => inner.IsTopmost(w);
        public void RequestClose(WindowHandle w) => inner.RequestClose(w);
        public string GetTitle(WindowHandle w) => inner.GetTitle(w);
        public WindowHandle GetForeground() => inner.GetForeground();
        public WindowHandle GetWindowAt(int x, int y) => inner.GetWindowAt(x, y);
        public bool SetTitleBarVisible(WindowHandle w, bool visible) => inner.SetTitleBarVisible(w, visible);
        public bool IsTopLevel(WindowHandle w) => inner.IsTopLevel(w);
        public bool IsCaptionDrag(WindowHandle w) => inner.IsCaptionDrag(w);
        public IReadOnlyList<WindowHandle> GetTopLevelWindows() => inner.GetTopLevelWindows();
        public ProcessName GetProcessName(WindowHandle w) => inner.GetProcessName(w);
        public string? GetProcessPath(WindowHandle w) => inner.GetProcessPath(w);
        public PixelRect GetVisibleBounds(WindowHandle w) => inner.GetVisibleBounds(w);
        public PixelRect GetBounds(WindowHandle w) => inner.GetBounds(w);
        public void Restore(WindowHandle w) => inner.Restore(w);
        public void Show(WindowHandle w) => inner.Show(w);
        public void Hide(WindowHandle w) => inner.Hide(w);
        public void Focus(WindowHandle w) => inner.Focus(w);
        public void PlaceVisible(WindowHandle w, PixelRect t) => inner.PlaceVisible(w, t);
        public void SetBounds(WindowHandle w, PixelRect b) => inner.SetBounds(w, b);
        public void SetTopmost(WindowHandle w) => inner.SetTopmost(w);

        public void Stack(WindowHandle after, IReadOnlyList<WindowHandle> topDown)
        {
            inner.Stack(after, topDown);
            if (topDown.Contains(Frame)) onFrameStacked();
        }
    }
}
