using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>User preferences. Read and written by an <see cref="ISettingsStore"/>.</summary>
public sealed class Settings
{
    public TabBarPlacement TabBarPlacement { get; set; }

    /// <summary>Shortcut bindings by action name, e.g. "NextTab": "Ctrl+Alt+Right". Empty means unbound; missing means the default.</summary>
    public Dictionary<string, string>? Shortcuts { get; set; }

    /// <summary>Apps (exe names) that keep their title bar with tab bar placement B. Null means the defaults.</summary>
    public List<string>? TitleBarApps { get; set; }

    /// <summary>Browsers and other apps known to put tabs or menus in their own title bar.</summary>
    public static IReadOnlyList<string> DefaultTitleBarApps { get; } =
        ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "Code", "WindowsTerminal", "explorer"];

    /// <summary>When a layout is opened, start the apps whose windows are missing (from the exe paths saved with it).</summary>
    public bool LaunchMissingApps { get; set; }

    /// <summary>At startup, open the groups that were open when Dockyard last exited (matched like a layout).</summary>
    public bool OpenLastGroups { get; set; }
}
