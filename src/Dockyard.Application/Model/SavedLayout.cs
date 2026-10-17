using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>
/// A tab as saved: which app to put there (Q3: exe name and title), and for the session file also the exact
/// window and where it was before it joined, so a restart after a crash can give it back.
/// </summary>
/// <param name="Path">The app's exe, to start it when a layout is opened without it; null if unknown.</param>
public sealed record SavedTab(
    ProcessName ProcessName, string Title, WindowHandle Handle = default, PixelRect? OriginalBounds = null, string? Path = null);

/// <summary>A layout tree node: a split when <see cref="Split"/> is set, otherwise a pane with tabs.</summary>
public sealed record SavedNode(
    Orientation? Split = null, double Ratio = 0.5, SavedNode? First = null, SavedNode? Second = null,
    IReadOnlyList<SavedTab>? Tabs = null, int ActiveIndex = 0);

/// <param name="Bounds">The frame's window rectangle in screen pixels.</param>
/// <param name="TabBarPlacement">The group's own tab bar placement; null (layouts saved before) for the default.</param>
/// <param name="Hotkey">
/// A <see cref="ShortcutKey"/> as text that opens the layout from anywhere; null for none. Only saved layouts have
/// one: the session and last-groups files leave it out.
/// </param>
/// <param name="AutoHideTitleBars">The group's own title bar auto-hide; null (saved before) for the default.</param>
public sealed record SavedGroup(
    LayoutName Name, PixelRect Bounds, SavedNode Layout, TabBarPlacement? TabBarPlacement = null, string? Hotkey = null,
    bool? AutoHideTitleBars = null);

/// <summary>Saved layouts are known by their names, which stay unique.</summary>
public static class LayoutLibrary
{
    /// <summary>
    /// Adds the layout, replacing one with the same name, which hands its hotkey over: saving a group again must
    /// not undo what the user set up for the layout. Returns true if one was replaced.
    /// </summary>
    public static bool Put(List<SavedGroup> layouts, SavedGroup layout)
    {
        var previous = layouts.Find(l => l.Name == layout.Name);
        layouts.RemoveAll(l => l.Name == layout.Name);
        layouts.Add(previous is null ? layout : layout with { Hotkey = previous.Hotkey });
        return previous is not null;
    }

    /// <summary>
    /// Sets the layout's hotkey, or clears it with null. A key does one thing: another layout that had it loses it.
    /// Returns false if there is no layout of that name.
    /// </summary>
    public static bool SetHotkey(List<SavedGroup> layouts, LayoutName name, ShortcutKey? hotkey)
    {
        var index = layouts.FindIndex(l => l.Name == name);
        if (index < 0) return false;
        var text = hotkey?.ToString();
        for (var i = 0; i < layouts.Count; i++)
            if (text is not null && layouts[i].Hotkey == text) layouts[i] = layouts[i] with { Hotkey = null };
        layouts[index] = layouts[index] with { Hotkey = text };
        return true;
    }

    /// <summary>The hotkeys of the layouts by key. A text that does not parse, or a key taken twice, is ignored.</summary>
    public static Dictionary<ShortcutKey, LayoutName> Hotkeys(IEnumerable<SavedGroup> layouts)
    {
        var keys = new Dictionary<ShortcutKey, LayoutName>();
        foreach (var layout in layouts)
            if (layout.Hotkey is { } text && ShortcutKey.Parse(text) is { } key)
                keys.TryAdd(key, layout.Name);
        return keys;
    }

    /// <summary>Renames a layout. Fails for a blank name or one another layout has.</summary>
    public static bool Rename(List<SavedGroup> layouts, LayoutName from, string to)
    {
        var renamed = new LayoutName(to.Trim());
        var index = layouts.FindIndex(l => l.Name == from);
        if (index < 0 || renamed.Value.Length == 0 || layouts.Any(l => l.Name == renamed && l.Name != from)) return false;
        layouts[index] = layouts[index] with { Name = renamed };
        return true;
    }
}
