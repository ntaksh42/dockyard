using Dockyard.Domain;

namespace Dockyard.Application.Tests;

public class WindowInfoTests
{
    private static readonly WindowInfo Chrome = new(new WindowHandle(1), "Pull request #38 - GitHub", new ProcessName("chrome"), true);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chrome")]
    [InlineData("CHROME")]
    [InlineData("github")]
    [InlineData("#38 chrome")]
    [InlineData("github pull")]
    public void Matches_EveryWordInTheTitleOrTheExeName(string query) => Assert.True(Chrome.Matches(query));

    [Theory]
    [InlineData("edge")]
    [InlineData("github edge")]
    public void Matches_NotWhenAWordIsMissing(string query) => Assert.False(Chrome.Matches(query));
}
