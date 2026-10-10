using Dockyard.Domain;

namespace Dockyard.Domain.Tests;

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
    public void SmallerWithinTolerance_DoesNothing() =>
        Assert.Equal(SettleAction.None, new SettleTracker(tolerance: 8).Evaluate(Target, Target with { Width = 494, Height = 395 }));

    [Fact]
    public void LargerByRoundingOnly_DoesNothing() =>
        Assert.Equal(SettleAction.None, new SettleTracker().Evaluate(Target, Target with { Width = 501, Height = 401 }));

    [Fact]
    public void LargerByAFewPixels_IsLearnedRatherThanToleratedOverTheNeighbour()
    {
        // Regression: up to 8px of overflow used to be tolerated, leaving the window over the next pane.
        var t = new SettleTracker(tolerance: 8);
        var slightlyLarger = Target with { Width = 505 };
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, slightlyLarger));
        Assert.Equal(SettleAction.LearnMinimum, t.Evaluate(Target, slightlyLarger));
    }

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

public class TrackingEdgeCaseTests
{
    private static readonly Rect Target = new(0, 0, 500, 400);

    [Fact]
    public void Drag_VerticalOnlyResize_ReportsTopOrBottom()
    {
        var start = new Rect(100, 100, 400, 300);
        Assert.Equal(Edges.Bottom, DragInterpreter.Interpret(start, start with { Height = 340 }).Resized);
        Assert.Equal(Edges.Top, DragInterpreter.Interpret(start, new Rect(100, 60, 400, 340)).Resized);
    }

    [Fact]
    public void Settle_SmallerThanTargetBeyondTolerance_Retries()
    {
        var t = new SettleTracker(tolerance: 8);
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, Target with { Width = 450 }));
    }

    [Fact]
    public void Settle_LargerButAlsoMoved_IsNotLearnedAsMinimum()
    {
        var t = new SettleTracker(maxRetries: 5);
        var bigElsewhere = new Rect(50, 0, 700, 400);
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, bigElsewhere));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, bigElsewhere));
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, bigElsewhere));
    }

    [Fact]
    public void Settle_LargerInHeightOnly_IsLearned()
    {
        var t = new SettleTracker();
        var tall = Target with { Height = 600 };
        Assert.Equal(SettleAction.Retry, t.Evaluate(Target, tall));
        Assert.Equal(SettleAction.LearnMinimum, t.Evaluate(Target, tall));
    }
}
