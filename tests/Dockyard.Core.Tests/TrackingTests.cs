using Dockyard.Core;

namespace Dockyard.Core.Tests;

public class DragInterpreterTests
{
    private static readonly Rect Start = new(100, 100, 400, 300);

    [Fact]
    public void SameBounds_IsNothing() =>
        Assert.Equal(new DragChange(false, Edges.None), DragInterpreter.Interpret(Start, Start));

    [Fact]
    public void SameSizeElsewhere_IsMove() =>
        Assert.Equal(new DragChange(true, Edges.None), DragInterpreter.Interpret(Start, Start with { X = 150, Y = 80 }));

    [Theory]
    [InlineData(100, 100, 450, 300, Edges.Right)]
    [InlineData(50, 100, 450, 300, Edges.Left)]
    [InlineData(100, 100, 400, 350, Edges.Bottom)]
    [InlineData(100, 60, 400, 340, Edges.Top)]
    [InlineData(100, 100, 450, 350, Edges.Right | Edges.Bottom)]
    [InlineData(50, 60, 450, 340, Edges.Left | Edges.Top)]
    public void SizeChange_ReportsDraggedEdges(double x, double y, double w, double h, Edges expected) =>
        Assert.Equal(new DragChange(false, expected), DragInterpreter.Interpret(Start, new Rect(x, y, w, h)));

    [Fact]
    public void BothOppositeEdgesMovedWithSizeChange_IsNotAttributedToAnEdge() =>
        Assert.Equal(new DragChange(false, Edges.None), DragInterpreter.Interpret(Start, new Rect(90, 100, 420, 300)));
}

public class SettleTrackerTests
{
    private static readonly Rect Target = new(0, 0, 500, 400);

    [Fact]
    public void AtTarget_DoesNothing() =>
        Assert.Equal(SettleAction.None, new SettleTracker().Evaluate(Target, Target));

    [Fact]
    public void WithinTolerance_DoesNothing() =>
        Assert.Equal(SettleAction.None, new SettleTracker(tolerance: 8).Evaluate(Target, Target with { Width = 506, Height = 395 }));

    [Fact]
    public void LargerOnce_Retries_ThenLearnsWhenItStaysTheSame()
    {
        var t = new SettleTracker();
        var big = Target with { Width = 690 };
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big));
        Assert.Equal(SettleAction.LearnMinimum, t.Evaluate(Target, big));
    }

    [Fact]
    public void LargerThenAtTarget_WasJustSlow()
    {
        var t = new SettleTracker();
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, Target with { Width = 690 }));
        Assert.Equal(SettleAction.None, t.Evaluate(Target, Target));
    }

    [Fact]
    public void LargerThenAtTargetThenLarger_DoesNotLearnAnAcceptedSizeAsMinimum()
    {
        var t = new SettleTracker();
        var big = Target with { Width = 690 };
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big));
        Assert.Equal(SettleAction.None, t.Evaluate(Target, Target));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big));
    }

    [Fact]
    public void LargerThenWrongPositionThenLarger_RequiresConsecutiveObservations()
    {
        var t = new SettleTracker(maxRetries: 3);
        var big = Target with { Width = 690 };
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big with { X = 100 }));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, big));
        Assert.Equal(SettleAction.LearnMinimum, t.Evaluate(Target, big));
    }

    [Fact]
    public void WindowThatNeverMoves_StopsRetrying()
    {
        var t = new SettleTracker(maxRetries: 2);
        var stuck = new Rect(900, 900, 500, 400);
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, stuck));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, stuck));
        for (var i = 0; i < 10; i++) Assert.Equal(SettleAction.None, t.Evaluate(Target, stuck));
    }

    [Fact]
    public void NewTarget_ResetsRetries()
    {
        var t = new SettleTracker(maxRetries: 1);
        var stuck = new Rect(900, 900, 500, 400);
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, stuck));
        Assert.Equal(SettleAction.None, t.Evaluate(Target, stuck));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target with { X = 10 }, stuck));
    }

    [Fact]
    public void LargerButDifferentSizeEachTime_DoesNotLearn()
    {
        var t = new SettleTracker(maxRetries: 5);
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, Target with { Width = 600 }));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, Target with { Width = 620 }));
        Assert.Equal(SettleAction.LearnMinimum, t.Evaluate(Target, Target with { Width = 620 }));
    }
}

public class ZOrderPolicyTests
{
    private static readonly nint[] Members = [100, 200];

    [Fact]
    public void BroughtToTop_CarriesTabs() => Assert.True(ZOrderPolicy.ShouldCarryTabs(0, 0, Members));

    [Fact]
    public void PlacedBelowSomeOtherWindow_CarriesTabs() => Assert.True(ZOrderPolicy.ShouldCarryTabs(555, 0, Members));

    [Theory]
    [InlineData(1)]   // HWND_BOTTOM
    [InlineData(-1)]  // HWND_TOPMOST
    [InlineData(-2)]  // HWND_NOTOPMOST
    [InlineData(100)] // one of our own tabs
    public void SpecialOrMemberInsertAfter_IsLeftAlone(long insertAfter) =>
        Assert.False(ZOrderPolicy.ShouldCarryTabs((nint)insertAfter, 0, Members));

    [Theory]
    [InlineData(ZOrderPolicy.SWP_NOZORDER)]
    [InlineData(ZOrderPolicy.SWP_HIDEWINDOW)]
    public void NoZOrderChangeOrHiding_IsLeftAlone(uint flags) =>
        Assert.False(ZOrderPolicy.ShouldCarryTabs(0, flags, Members));

    [Fact]
    public void NoTabs_NothingToCarry() => Assert.False(ZOrderPolicy.ShouldCarryTabs(0, 0, []));
}
