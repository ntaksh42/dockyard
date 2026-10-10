using Dockyard.Domain;

namespace Dockyard.Application.Tests;

public class GroupControllerTests
{
    private const nint Frame = 1000, Other = 900, Dialog = 950;
    private const nint A = 1, B = 2, C = 3;
    private static readonly PixelRect Initial = new(10, 10, 610, 410);

    private readonly FakeWindowSystem _ws = new();
    private readonly FakeView _view = new(Frame);
    private readonly ManualScheduler _scheduler = new();
    private readonly GroupController _c;

    public GroupControllerTests()
    {
        _ws.AddToZOrder(Frame);
        _c = new GroupController(_view, _ws, _scheduler);
        _c.OnSurfaceResized();
    }

    private Pane RootPane => _c.Layout!.Panes[0].Pane;

    private PaneLayout PaneOf(nint w) => _c.Layout!.Panes.First(p => p.Pane.Windows.Contains(w));

    private nint Window(nint h, int minWidth = 0)
    {
        _ws.Add(h, Initial, $"w{h}");
        _ws.Windows[h].MinWidth = minWidth;
        return h;
    }

    private void AssertPlaced(nint w) => Assert.Equal(_view.ToScreen(PaneOf(w).Content), _ws.GetVisibleBounds(w));

    private void SplitAC()
    {
        _c.Adopt(Window(A), RootPane, null);
        _c.Adopt(Window(C), RootPane, DockSide.Right);
    }

    // ---- adding and removing ----------------------------------------------------------------------

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
        Assert.False(_c.Adopt(77, RootPane, null));
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

    private void UserDrags(nint w, Func<PixelRect, PixelRect> change)
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
}
