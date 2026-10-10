using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.App.Controllers;

/// <summary>
/// A tab as saved: which app to put there (Q3: exe name and title), and for the session file also the exact
/// window and where it was before it joined, so a restart after a crash can give it back.
/// </summary>
public sealed record SavedTab(string ProcessName, string Title, long Handle = 0, PixelRect? OriginalBounds = null);

/// <summary>A layout tree node: a split when <see cref="Split"/> is set, otherwise a pane with tabs.</summary>
public sealed record SavedNode(
    Orientation? Split = null, double Ratio = 0.5, SavedNode? First = null, SavedNode? Second = null,
    IReadOnlyList<SavedTab>? Tabs = null, int ActiveIndex = 0);

/// <param name="Bounds">The frame's window rectangle in screen pixels.</param>
public sealed record SavedGroup(string Name, PixelRect Bounds, SavedNode Layout);

/// <summary>A window that could fill a saved tab.</summary>
public sealed record WindowCandidate(nint Handle, string ProcessName, string Title);

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

public static class WindowMatcher
{
    /// <summary>
    /// Picks the window for a saved tab among <paramref name="candidates"/> and adds it to
    /// <paramref name="claimed"/>, or returns 0. The same window wins if it still exists; otherwise, unless
    /// <paramref name="sameWindowOnly"/>, a window of the same app, preferring the same title. Titles change
    /// (documents, web pages), so the app alone is enough.
    /// </summary>
    public static nint Match(SavedTab tab, IEnumerable<WindowCandidate> candidates, ISet<nint> claimed, bool sameWindowOnly)
    {
        var free = candidates
            .Where(c => !claimed.Contains(c.Handle) && c.ProcessName.Equals(tab.ProcessName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hit = free.FirstOrDefault(c => c.Handle == tab.Handle);
        if (hit is null && !sameWindowOnly) hit = free.FirstOrDefault(c => c.Title == tab.Title) ?? free.FirstOrDefault();
        if (hit is null) return 0;
        claimed.Add(hit.Handle);
        return hit.Handle;
    }
}
