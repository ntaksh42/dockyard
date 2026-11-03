using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>Text describing the apps and panes in a saved layout.</summary>
public static class LayoutDescription
{
    /// <summary>Builds a short summary and a pane-by-pane list of saved window titles.</summary>
    public static (string Summary, string Details) Describe(SavedNode layout)
    {
        var panes = new List<SavedNode>();
        CollectPanes(layout, panes);
        var tabs = panes.SelectMany(pane => pane.Tabs ?? []).ToList();
        string apps = string.Join(", ", tabs.Select(tab => tab.ProcessName).Distinct());
        string summary = $"{(apps.Length == 0 ? "No apps" : apps)} · "
            + $"{panes.Count} {(panes.Count == 1 ? "pane" : "panes")}, "
            + $"{tabs.Count} {(tabs.Count == 1 ? "tab" : "tabs")}";

        var lines = new List<string> { "Saved window titles:" };
        for (int index = 0; index < panes.Count; index++)
        {
            string name = panes.Count == 2
                ? layout.Split == Orientation.Horizontal
                    ? (index == 0 ? "Left" : "Right")
                    : (index == 0 ? "Top" : "Bottom")
                : $"Pane {index + 1}";
            lines.Add($"{name}:");
            if (panes[index].Tabs is not { Count: > 0 } paneTabs)
            {
                lines.Add("  (Empty)");
                continue;
            }
            foreach (SavedTab tab in paneTabs)
            {
                lines.Add(string.IsNullOrWhiteSpace(tab.Title)
                    ? $"  {tab.ProcessName}" : $"  {tab.ProcessName} — {tab.Title}");
            }
        }
        return (summary, string.Join(Environment.NewLine, lines));
    }

    private static void CollectPanes(SavedNode node, List<SavedNode> panes)
    {
        if (node.Split is null)
        {
            panes.Add(node);
            return;
        }
        if (node.First is { } first)
        {
            CollectPanes(first, panes);
        }
        if (node.Second is { } second)
        {
            CollectPanes(second, panes);
        }
    }
}
