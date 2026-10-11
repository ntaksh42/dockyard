namespace Dockyard.Architecture.Tests;

/// <summary>
/// Everything each assembly exposes, in one place. Public types sit in the assembly's root namespace; everything
/// else is in <c>Internal</c> and not public. Adding one means adding it here, which is the moment to ask
/// whether it belongs in the facade (a port, a value that crosses layers) or should stay internal.
/// </summary>
internal static class PublicApi
{
    /// <summary>
    /// The shared vocabulary of the layout: no entry point of its own, because every layer speaks it. It depends
    /// on nothing, so anything here can be used and tested without Windows.
    /// </summary>
    public static readonly string[] Domain =
    [
        "Dockyard.Domain.DockSide",
        "Dockyard.Domain.DockTarget",
        "Dockyard.Domain.Docking",
        "Dockyard.Domain.DragChange",
        "Dockyard.Domain.DragInterpreter",
        "Dockyard.Domain.Edges",
        "Dockyard.Domain.LayoutEngine",
        "Dockyard.Domain.LayoutMetrics",
        "Dockyard.Domain.LayoutNode",
        "Dockyard.Domain.LayoutResult",
        "Dockyard.Domain.LayoutTree",
        "Dockyard.Domain.Navigation",
        "Dockyard.Domain.Orientation",
        "Dockyard.Domain.Pane",
        "Dockyard.Domain.PaneLayout",
        "Dockyard.Domain.Placement",
        "Dockyard.Domain.Rect",
        "Dockyard.Domain.SettleAction",
        "Dockyard.Domain.SettleTracker",
        "Dockyard.Domain.Size",
        "Dockyard.Domain.SplitNode",
        "Dockyard.Domain.SplitterLayout",
        "Dockyard.Domain.TabBarPlacement",
        "Dockyard.Domain.WindowHandle",
    ];

    /// <summary>
    /// The entry point is <c>Workspace</c>. The rest are the ports it is given (what the machine and the windows
    /// provide) and the values that cross them. The group controller and the policies behind it stay internal.
    /// </summary>
    public static readonly string[] Application =
    [
        "Dockyard.Application.Workspace",

        // ports to the machine (implemented by Infrastructure)
        "Dockyard.Application.IPlatform",
        "Dockyard.Application.IWindowSystem",
        "Dockyard.Application.IWindowCatalog",
        "Dockyard.Application.IInputState",
        "Dockyard.Application.IWindowEventSource",
        "Dockyard.Application.IKeyboardSource",
        "Dockyard.Application.ILayoutStore",
        "Dockyard.Application.ISessionStore",
        "Dockyard.Application.ISettingsStore",
        "Dockyard.Application.IAppLauncher",
        "Dockyard.Application.ILayoutShortcuts",
        "Dockyard.Application.IAutostart",
        "Dockyard.Application.ILog",

        // ports to the windows (implemented by Presentation)
        "Dockyard.Application.IShell",
        "Dockyard.Application.IGroupFrame",
        "Dockyard.Application.IGroupView",
        "Dockyard.Application.IGroupControl",
        "Dockyard.Application.IFrameContext",
        "Dockyard.Application.IDockGuide",
        "Dockyard.Application.IScheduler",
        "Dockyard.Application.IRestartableTimer",

        // values that cross the ports
        "Dockyard.Application.PixelRect",
        "Dockyard.Application.WindowInfo",
        "Dockyard.Application.LayoutName",
        "Dockyard.Application.ProcessName",
        "Dockyard.Application.WindowEvent",
        "Dockyard.Application.GroupSnapshot",
        "Dockyard.Application.FrameZChange",
        "Dockyard.Application.ZPlacement",
        "Dockyard.Application.SavedGroup",
        "Dockyard.Application.SavedNode",
        "Dockyard.Application.SavedTab",
        "Dockyard.Application.LayoutLibrary",
        "Dockyard.Application.LaunchCommand",
        "Dockyard.Application.Settings",
        "Dockyard.Application.KeyModifiers",
        "Dockyard.Application.ShortcutAction",
        "Dockyard.Application.ShortcutKey",
        "Dockyard.Application.Shortcuts",
        "Dockyard.Application.Diagnostics",
    ];

    /// <summary>The Windows implementation of <c>IPlatform</c>; every Win32 and file detail behind it is internal.</summary>
    public static readonly string[] Infrastructure =
    [
        "Dockyard.Infrastructure.PlatformServices",
    ];

    /// <summary>The WPF implementation of <c>IShell</c>; the windows and the tray behind it are internal.</summary>
    public static readonly string[] Presentation =
    [
        "Dockyard.Presentation.WpfShell",
    ];
}
