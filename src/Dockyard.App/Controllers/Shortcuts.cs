using System.Globalization;
using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.App.Controllers;

public enum ShortcutAction { PreviousTab, NextTab, FocusLeft, FocusRight, FocusUp, FocusDown, RemoveFromGroup }

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
/// next pane, W takes the window out of its group. Not Ctrl+Win: Ctrl+Win+←/→ switches virtual desktops. The
/// user can rebind them in the settings.
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
    };

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
        foreach (var (name, text) in saved ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse<ShortcutAction>(name, out var action)) continue;
            if (text.Length == 0) bindings.Remove(action);
            else if (ShortcutKey.Parse(text) is { } binding) bindings[action] = binding;
        }
        return bindings;
    }

    /// <summary>Only the bindings that differ from the defaults, so a later change of a default still applies.</summary>
    public static Dictionary<string, string> Save(IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings) =>
        Enum.GetValues<ShortcutAction>()
            .Where(a => !bindings.TryGetValue(a, out var b) || b != Defaults[a])
            .ToDictionary(a => a.ToString(), a => bindings.TryGetValue(a, out var b) ? b.ToString() : "");

    internal static DockSide? Direction(ShortcutAction action) => action switch
    {
        ShortcutAction.FocusLeft => DockSide.Left,
        ShortcutAction.FocusRight => DockSide.Right,
        ShortcutAction.FocusUp => DockSide.Top,
        ShortcutAction.FocusDown => DockSide.Bottom,
        _ => null,
    };
}
