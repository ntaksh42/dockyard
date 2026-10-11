namespace Dockyard.Application.Internal;

/// <summary>A window that could fill a saved tab.</summary>
internal sealed record WindowCandidate(nint Handle, string ProcessName, string Title);

internal static class WindowMatcher
{
    /// <summary>
    /// Picks the window for a saved tab among <paramref name="candidates"/> and adds it to
    /// <paramref name="claimed"/>, or returns 0. See <see cref="MatchAll"/> for the rules.
    /// </summary>
    public static nint Match(SavedTab tab, IEnumerable<WindowCandidate> candidates, ISet<nint> claimed, bool sameWindowOnly) =>
        MatchAll([tab], candidates, claimed, sameWindowOnly).GetValueOrDefault(tab);

    /// <summary>
    /// Picks the windows for <paramref name="tabs"/> and adds them to <paramref name="claimed"/>. A tab gets the
    /// same window if it still exists; otherwise, unless <paramref name="sameWindowOnly"/>, a window of the same
    /// app, preferring the same title. Titles change (documents, web pages), so the app alone is enough. The
    /// rules are applied to all tabs one after the other, so a tab that settles for any window of its app cannot
    /// take the one another tab matches by title. Tabs without a window are left out of the result, which is
    /// keyed by the tab instances.
    /// </summary>
    public static Dictionary<SavedTab, nint> MatchAll(
        IReadOnlyList<SavedTab> tabs, IEnumerable<WindowCandidate> candidates, ISet<nint> claimed, bool sameWindowOnly)
    {
        var free = candidates.Where(c => !claimed.Contains(c.Handle)).ToList();
        var matched = new Dictionary<SavedTab, nint>(ReferenceEqualityComparer.Instance);
        Func<SavedTab, WindowCandidate, bool>[] rules = sameWindowOnly
            ? [(t, c) => c.Handle == t.Handle]
            : [(t, c) => c.Handle == t.Handle, (t, c) => c.Title == t.Title, (_, _) => true];
        foreach (var rule in rules)
        {
            foreach (var tab in tabs)
            {
                if (matched.ContainsKey(tab)) continue;
                var hit = free.FirstOrDefault(c => c.ProcessName.Equals(tab.ProcessName, StringComparison.OrdinalIgnoreCase) && rule(tab, c));
                if (hit is null) continue;
                free.Remove(hit);
                claimed.Add(hit.Handle);
                matched[tab] = hit.Handle;
            }
        }
        return matched;
    }

    /// <summary>The tabs of a saved layout, first pane first.</summary>
    public static IEnumerable<SavedTab> TabsOf(SavedNode node) =>
        node is { Split: not null, First: { } first, Second: { } second }
            ? TabsOf(first).Concat(TabsOf(second))
            : node.Tabs ?? [];
}
