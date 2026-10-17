using System.Globalization;
using Dockyard.Domain;

namespace Dockyard.Application;

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

public enum ShortcutAction
{
    PreviousTab, NextTab, FocusLeft, FocusRight, FocusUp, FocusDown, RemoveFromGroup,
    Tab1, Tab2, Tab3, Tab4, Tab5, Tab6, Tab7, Tab8, Tab9,
    MoveTabLeft, MoveTabRight, MoveTabUp, MoveTabDown, GrowPane, ShrinkPane,
    SaveLayout, RenameGroup, ChangeTabBar, MaximizeGroup, MinimizeGroup, CloseGroup, OpenMenu,
    AddTab, SplitRight, SplitDown, OpenLayout, NewGroup, ManageLayouts, OpenSettings, Quit,
}

/// <summary>A key with the modifiers that must be held. Written as e.g. "Ctrl+Alt+Left".</summary>
public readonly record struct ShortcutKey(KeyModifiers Modifiers, int Key)
{
    private static readonly (KeyModifiers Flag, string Name)[] ModifierNames =
        [(KeyModifiers.Ctrl, "Ctrl"), (KeyModifiers.Alt, "Alt"), (KeyModifiers.Shift, "Shift"), (KeyModifiers.Win, "Win")];

    private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();

    public override string ToString()
    {
        var modifiers = Modifiers;
        return string.Join("+", ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(KeyName(Key)));
    }

    public static ShortcutKey? Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = ModifierNames.FirstOrDefault(n => n.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (m.Name is null) return null;
            modifiers |= m.Flag;
        }
        var key = KeyNames.FirstOrDefault(k => k.Value.Equals(parts[^1], StringComparison.OrdinalIgnoreCase)).Key;
        if (key == 0 && parts[^1].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            int.TryParse(parts[^1][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key);
        return key == 0 ? null : new ShortcutKey(modifiers, key);
    }

    private static string KeyName(int key) => KeyNames.TryGetValue(key, out var name) ? name : $"0x{key:X2}";

    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2D] = "Insert", [0x2E] = "Delete",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };
        for (var c = '0'; c <= '9'; c++) names[c] = c.ToString();
        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var f = 1; f <= 24; f++) names[0x6F + f] = $"F{f}";
        return names;
    }
}

/// <summary>
/// Keyboard shortcuts (Q7). By default all on Ctrl+Alt: ←/→ switch tabs in the pane, Shift+arrows move to the
/// next pane, W takes the window out of its group. Not Ctrl+Win: Ctrl+Win+←/→ switches virtual desktops. Not
/// without Alt either: the members' own shortcuts (Ctrl+Shift+N opens a private window in a browser and a new
/// folder in Explorer) would stop working while they have the focus. The user can rebind them in the settings.
/// </summary>
public static class Shortcuts
{
    public static IReadOnlyDictionary<ShortcutAction, ShortcutKey> Defaults { get; } = new Dictionary<ShortcutAction, ShortcutKey>
    {
        [ShortcutAction.PreviousTab] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x25),
        [ShortcutAction.NextTab] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x27),
        [ShortcutAction.FocusLeft] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x25),
        [ShortcutAction.FocusRight] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x27),
        [ShortcutAction.FocusUp] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x26),
        [ShortcutAction.FocusDown] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x28),
        [ShortcutAction.RemoveFromGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x57),
        [ShortcutAction.Tab1] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x31),
        [ShortcutAction.Tab2] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x32),
        [ShortcutAction.Tab3] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x33),
        [ShortcutAction.Tab4] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x34),
        [ShortcutAction.Tab5] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x35),
        [ShortcutAction.Tab6] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x36),
        [ShortcutAction.Tab7] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x37),
        [ShortcutAction.Tab8] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x38),
        [ShortcutAction.Tab9] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x39),
        // Win joins Ctrl+Alt for moving a tab: Ctrl+Alt+Shift+arrows already move the focus.
        [ShortcutAction.MoveTabLeft] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win, 0x25),
        [ShortcutAction.MoveTabRight] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win, 0x27),
        [ShortcutAction.MoveTabUp] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win, 0x26),
        [ShortcutAction.MoveTabDown] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win, 0x28),
        [ShortcutAction.GrowPane] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0xDD),
        [ShortcutAction.ShrinkPane] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0xDB),
        [ShortcutAction.RenameGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x52),
        [ShortcutAction.ChangeTabBar] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x42),
        [ShortcutAction.MaximizeGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x26),
        [ShortcutAction.MinimizeGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x28),
        [ShortcutAction.CloseGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x57),
        [ShortcutAction.OpenMenu] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x4D),
        [ShortcutAction.AddTab] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x54),
        // V and H as in vim's :vsplit and :split: a vertical divider puts the new window to the right.
        [ShortcutAction.SplitRight] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x56),
        [ShortcutAction.SplitDown] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x48),
        [ShortcutAction.OpenLayout] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x4F),
        [ShortcutAction.NewGroup] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x4E),
        [ShortcutAction.ManageLayouts] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x4C),
        [ShortcutAction.OpenSettings] = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0xBC),
        [ShortcutAction.Quit] = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x51),
    };

    /// <summary>How the actions are called in the settings and menus.</summary>
    public static IReadOnlyDictionary<ShortcutAction, string> Names { get; } = new Dictionary<ShortcutAction, string>
    {
        [ShortcutAction.PreviousTab] = "Previous tab",
        [ShortcutAction.NextTab] = "Next tab",
        [ShortcutAction.FocusLeft] = "Pane to the left",
        [ShortcutAction.FocusRight] = "Pane to the right",
        [ShortcutAction.FocusUp] = "Pane above",
        [ShortcutAction.FocusDown] = "Pane below",
        [ShortcutAction.RemoveFromGroup] = "Remove from group",
        [ShortcutAction.Tab1] = "Tab 1",
        [ShortcutAction.Tab2] = "Tab 2",
        [ShortcutAction.Tab3] = "Tab 3",
        [ShortcutAction.Tab4] = "Tab 4",
        [ShortcutAction.Tab5] = "Tab 5",
        [ShortcutAction.Tab6] = "Tab 6",
        [ShortcutAction.Tab7] = "Tab 7",
        [ShortcutAction.Tab8] = "Tab 8",
        [ShortcutAction.Tab9] = "Last tab",
        [ShortcutAction.MoveTabLeft] = "Move tab left",
        [ShortcutAction.MoveTabRight] = "Move tab right",
        [ShortcutAction.MoveTabUp] = "Move tab up",
        [ShortcutAction.MoveTabDown] = "Move tab down",
        [ShortcutAction.GrowPane] = "Grow pane",
        [ShortcutAction.ShrinkPane] = "Shrink pane",
        [ShortcutAction.SaveLayout] = "Save layout",
        [ShortcutAction.RenameGroup] = "Rename group",
        [ShortcutAction.ChangeTabBar] = "Change tab bar position",
        [ShortcutAction.MaximizeGroup] = "Maximize or restore group",
        [ShortcutAction.MinimizeGroup] = "Minimize group",
        [ShortcutAction.CloseGroup] = "Close group",
        [ShortcutAction.OpenMenu] = "Open the + menu",
        [ShortcutAction.AddTab] = "Add window as tab",
        [ShortcutAction.SplitRight] = "Add window in a split to the right",
        [ShortcutAction.SplitDown] = "Add window in a split below",
        [ShortcutAction.OpenLayout] = "Open saved layout",
        [ShortcutAction.NewGroup] = "New group",
        [ShortcutAction.ManageLayouts] = "Manage layouts",
        [ShortcutAction.OpenSettings] = "Open settings",
        [ShortcutAction.Quit] = "Exit Dockyard",
    };

    /// <summary>Actions on the frame itself rather than on the focused window: they work in an empty group too.</summary>
    public static bool IsFrameAction(ShortcutAction action) =>
        action is ShortcutAction.SaveLayout or ShortcutAction.RenameGroup or ShortcutAction.ChangeTabBar or ShortcutAction.MaximizeGroup
            or ShortcutAction.MinimizeGroup or ShortcutAction.CloseGroup or ShortcutAction.OpenMenu
            or ShortcutAction.AddTab or ShortcutAction.SplitRight or ShortcutAction.SplitDown or ShortcutAction.OpenLayout
            or ShortcutAction.NewGroup or ShortcutAction.ManageLayouts or ShortcutAction.OpenSettings or ShortcutAction.Quit;

    /// <summary>The action bound to the key, or null if it is not a shortcut.</summary>
    public static ShortcutAction? Match(IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings, int virtualKey, KeyModifiers modifiers)
    {
        var pressed = new ShortcutKey(modifiers, virtualKey);
        foreach (var (action, binding) in bindings)
            if (binding == pressed) return action;
        return null;
    }

    /// <summary>
    /// Bindings from the settings file: action name → binding text, an empty text meaning unbound. Actions not
    /// in the file keep their default; unreadable entries are ignored.
    /// </summary>
    public static Dictionary<ShortcutAction, ShortcutKey> Load(IReadOnlyDictionary<string, string>? saved)
    {
        var bindings = Defaults.ToDictionary();
        var chosen = new HashSet<ShortcutAction>();
        foreach (var (name, text) in saved ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse<ShortcutAction>(name, out var action)) continue;
            if (string.IsNullOrEmpty(text)) bindings.Remove(action);
            else if (ShortcutKey.Parse(text) is { } binding) bindings[action] = binding;
            chosen.Add(action);
        }
        // A key the user chose wins over a default that came later (e.g. a new action's) for the same key.
        foreach (var action in Defaults.Keys.Where(a => !chosen.Contains(a)))
            if (chosen.Any(c => bindings.TryGetValue(c, out var key) && key == bindings[action]))
                bindings.Remove(action);
        return bindings;
    }

    /// <summary>Only the bindings that differ from the defaults, so a later change of a default still applies.</summary>
    public static Dictionary<string, string> Save(IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings) =>
        Enum.GetValues<ShortcutAction>()
            .Where(a => bindings.TryGetValue(a, out var b) != Defaults.TryGetValue(a, out var d) || b != d)
            .ToDictionary(a => a.ToString(), a => bindings.TryGetValue(a, out var b) ? b.ToString() : "");

    internal static DockSide? Direction(ShortcutAction action) => action switch
    {
        ShortcutAction.FocusLeft or ShortcutAction.MoveTabLeft => DockSide.Left,
        ShortcutAction.FocusRight or ShortcutAction.MoveTabRight => DockSide.Right,
        ShortcutAction.FocusUp or ShortcutAction.MoveTabUp => DockSide.Top,
        ShortcutAction.FocusDown or ShortcutAction.MoveTabDown => DockSide.Bottom,
        _ => null,
    };
}
