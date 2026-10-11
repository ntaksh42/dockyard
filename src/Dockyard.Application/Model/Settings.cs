using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>User preferences. Read and written by an <see cref="ISettingsStore"/>.</summary>
public sealed class Settings
{
    public TabBarPlacement TabBarPlacement { get; set; }

    /// <summary>Shortcut bindings by action name, e.g. "NextTab": "Ctrl+Alt+Right". Empty means unbound; missing means the default.</summary>
    public Dictionary<string, string>? Shortcuts { get; set; }

    /// <summary>Apps (exe names) that keep their title bar with tab bar placement B. Null means the defaults.</summary>
    public List<ProcessName>? TitleBarApps { get; set; }

    /// <summary>Browsers and other apps known to put tabs or menus in their own title bar.</summary>
    public static IReadOnlyList<ProcessName> DefaultTitleBarApps { get; } =
        [new("chrome"), new("msedge"), new("firefox"), new("brave"), new("opera"), new("vivaldi"), new("Code"), new("WindowsTerminal"), new("explorer")];

    /// <summary>When a layout is opened, start the apps whose windows are missing (from the exe paths saved with it).</summary>
    public bool LaunchMissingApps { get; set; }

    /// <summary>At startup, open the groups that were open when Dockyard last exited (matched like a layout).</summary>
    public bool OpenLastGroups { get; set; }
}
