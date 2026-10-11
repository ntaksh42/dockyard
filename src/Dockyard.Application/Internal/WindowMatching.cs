
namespace Dockyard.Application.Internal;

/// <summary>A window that could fill a saved tab.</summary>
internal sealed record WindowCandidate(nint Handle, string ProcessName, string Title);

internal static class WindowMatcher
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
