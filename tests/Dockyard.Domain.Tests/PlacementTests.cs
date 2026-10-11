namespace Dockyard.Domain.Tests;

public class PlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void FitToWorkArea_LeavesAWindowWhoseCaptionCanBeReached()
    {
        var bounds = new Rect(500, 300, 800, 600);

        Assert.Equal(bounds, Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_LeavesAWindowPartlyOffTheSideAlone()
    {
        var bounds = new Rect(-600, 100, 800, 600);

        Assert.Equal(bounds, Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_AllowsAMaximizedWindowsOverhang()
    {
        var bounds = new Rect(-8, -8, 1936, 1056);

        Assert.Equal(bounds, Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_BringsAWindowFromAMonitorThatIsGone()
    {
        var bounds = new Rect(2500, 200, 800, 600);

        Assert.Equal(new Rect(1120, 200, 800, 600), Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_BringsBackAWindowWhoseCaptionIsAboveTheWorkArea()
    {
        var bounds = new Rect(300, -500, 800, 600);

        Assert.Equal(new Rect(300, 0, 800, 600), Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_BringsBackAWindowBelowTheWorkArea()
    {
        var bounds = new Rect(300, 1500, 800, 600);

        Assert.Equal(new Rect(300, 440, 800, 600), Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_ShrinksAWindowLargerThanTheWorkArea()
    {
        var bounds = new Rect(5000, 5000, 3000, 2000);

        Assert.Equal(Work, Placement.FitToWorkArea(bounds, Work));
    }

    [Fact]
    public void FitToWorkArea_WorksOnAMonitorNotAtTheOrigin()
    {
        var second = new Rect(1920, 0, 1280, 1024);

        Assert.Equal(new Rect(1920, 100, 800, 600), Placement.FitToWorkArea(new Rect(-3000, 100, 800, 600), second));
    }
}
