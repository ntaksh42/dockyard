using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>
/// A tab as saved: which app to put there (Q3: exe name and title), and for the session file also the exact
/// window and where it was before it joined, so a restart after a crash can give it back.
/// </summary>
/// <param name="Path">The app's exe, to start it when a layout is opened without it; null if unknown.</param>
public sealed record SavedTab(string ProcessName, string Title, long Handle = 0, PixelRect? OriginalBounds = null, string? Path = null);

/// <summary>A layout tree node: a split when <see cref="Split"/> is set, otherwise a pane with tabs.</summary>
public sealed record SavedNode(
    Orientation? Split = null, double Ratio = 0.5, SavedNode? First = null, SavedNode? Second = null,
    IReadOnlyList<SavedTab>? Tabs = null, int ActiveIndex = 0);

/// <param name="Bounds">The frame's window rectangle in screen pixels.</param>
/// <param name="TabBarPlacement">The group's own tab bar placement; null (layouts saved before) for the default.</param>
public sealed record SavedGroup(string Name, PixelRect Bounds, SavedNode Layout, TabBarPlacement? TabBarPlacement = null);

/// <summary>Saved layouts are known by their names, which stay unique.</summary>
public static class LayoutLibrary
{
    /// <summary>Adds the layout, replacing one with the same name. Returns true if one was replaced.</summary>
    public static bool Put(List<SavedGroup> layouts, SavedGroup layout)
    {
        var replaced = layouts.RemoveAll(l => l.Name == layout.Name) > 0;
        layouts.Add(layout);
        return replaced;
    }

    /// <summary>Renames a layout. Fails for a blank name or one another layout has.</summary>
    public static bool Rename(List<SavedGroup> layouts, string from, string to)
    {
        to = to.Trim();
        var index = layouts.FindIndex(l => l.Name == from);
        if (index < 0 || to.Length == 0 || layouts.Any(l => l.Name == to && l.Name != from)) return false;
        layouts[index] = layouts[index] with { Name = to };
        return true;
    }
}
