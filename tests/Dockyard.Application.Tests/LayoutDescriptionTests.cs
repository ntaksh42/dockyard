using Dockyard.Domain;

namespace Dockyard.Application.Tests;

public class LayoutDescriptionTests
{
    [Fact]
    public void Describe_DeduplicatesAppsButCountsEveryTab()
    {
        var layout = new SavedNode(Split: Orientation.Horizontal,
            First: new SavedNode(Tabs: [Tab("Code", "project"), Tab("code", "other project")]),
            Second: new SavedNode(Tabs: [Tab("chrome", "Wiki")]));

        var description = LayoutDescription.Describe(layout);

        Assert.Equal("Code, chrome · 2 panes, 3 tabs", description.Summary);
        Assert.Equal(string.Join(Environment.NewLine,
            "Saved window titles:", "Left:", "  Code — project", "  code — other project",
            "Right:", "  chrome — Wiki"), description.Details);
    }

    [Fact]
    public void Describe_VerticalSplitIncludesEmptyPane()
    {
        var layout = new SavedNode(Split: Orientation.Vertical,
            First: new SavedNode(), Second: new SavedNode(Tabs: [Tab("notepad", "notes")]));

        var description = LayoutDescription.Describe(layout);

        Assert.Equal("notepad · 2 panes, 1 tab", description.Summary);
        Assert.Contains("Top:" + Environment.NewLine + "  (Empty)", description.Details);
        Assert.Contains("Bottom:" + Environment.NewLine + "  notepad — notes", description.Details);
    }

    [Fact]
    public void Describe_EmptyLayoutStillHasOnePane()
    {
        var description = LayoutDescription.Describe(new SavedNode());

        Assert.Equal("No apps · 1 pane, 0 tabs", description.Summary);
        Assert.Contains("Pane 1:" + Environment.NewLine + "  (Empty)", description.Details);
    }

    [Fact]
    public void Describe_NestedSplitsNumberPanesInTreeOrder()
    {
        var layout = new SavedNode(Split: Orientation.Horizontal,
            First: new SavedNode(Tabs: [Tab("Code", "日本語の文書")]),
            Second: new SavedNode(Split: Orientation.Vertical,
                First: new SavedNode(Tabs: [Tab("terminal", "")]), Second: new SavedNode()));

        var description = LayoutDescription.Describe(layout);

        Assert.Equal("Code, terminal · 3 panes, 2 tabs", description.Summary);
        Assert.Equal(string.Join(Environment.NewLine,
            "Saved window titles:", "Pane 1:", "  Code — 日本語の文書",
            "Pane 2:", "  terminal", "Pane 3:", "  (Empty)"), description.Details);
    }

    private static SavedTab Tab(string process, string title) => new(new ProcessName(process), title);
}
