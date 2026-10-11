using System.Windows;
using Dockyard.Application;
using Dockyard.Domain;
using Forms = System.Windows.Forms;
using Dockyard.Presentation.Internal;
using Dockyard.Presentation.Internal.Views;

namespace Dockyard.Presentation;

/// <summary>
/// The only public entry point of this assembly: the windows and the tray of Dockyard in WPF. Everything else
/// here is internal. It implements <see cref="IShell"/> for the workspace and calls the workspace back for what
/// the user asks for in the tray menu and the dialogs.
/// </summary>
public sealed class WpfShell : IShell, IDisposable
{
    private readonly IPlatform _platform;
    private readonly Action _exit;
    private readonly WpfScheduler _scheduler = new(System.Windows.Threading.Dispatcher.CurrentDispatcher);
    private readonly DockGuideOverlay _dockGuide = new();
    private Workspace? _workspace;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    /// <summary>What a click on the tray balloon shown last does; cleared once it is gone.</summary>
    private Action? _balloonClick;

    /// <param name="exit">Ends the program (the host's shutdown).</param>
    public WpfShell(IPlatform platform, Action exit)
    {
        _platform = platform;
        _exit = exit;
    }

    /// <summary>Connects the workspace this shell reports to, and puts the tray icon up.</summary>
    public void Attach(Workspace workspace)
    {
        _workspace = workspace;
        CreateTray();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _trayIcon?.Dispose();
    }

    // ---- IShell ---------------------------------------------------------------------------------

    IScheduler IShell.Scheduler => _scheduler;

    IDockGuide IShell.DockGuide => _dockGuide;

    IGroupFrame IShell.CreateFrame(string name, TabBarPlacement placement, IFrameContext context) =>
        new GroupWindow(name, _platform, context, placement);

    bool IShell.IsCapturingShortcut(nint foreground) =>
        _settingsWindow is { IsCapturing: true } settings && settings.Handle == foreground;

    void IShell.CaptureShortcut(ShortcutKey key) => _settingsWindow?.Capture(key);

    void IShell.ShowSettings() => ShowSettingsWindow();

    void IShell.ShowLayouts(string? layoutName) => ShowSettingsWindow().ShowLayoutsTab(layoutName);

    void IShell.Notify(string message, Action? onClick)
    {
        _balloonClick = onClick;
        _tray?.ShowBalloonTip(2000, "Dockyard", message, Forms.ToolTipIcon.Info);
    }

    void IShell.Exit() => _exit();

    /// <summary>Opens the settings window, or brings the open one forward.</summary>
    private SettingsWindow ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            var workspace = _workspace!;
            _settingsWindow = new SettingsWindow(_platform.Autostart, workspace, _platform.Layouts);
            _settingsWindow.Closing += (_, _) => workspace.ReturnFocusToGroup();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
        return _settingsWindow;
    }

    // ---- tray -----------------------------------------------------------------------------------

    private void CreateTray()
    {
        var workspace = _workspace!;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Group", null, (_, _) => workspace.NewGroup());
        var open = new Forms.ToolStripMenuItem("Open Layout");
        // Rebuilt on opening, so layouts saved meanwhile show up.
        open.DropDownOpening += (_, _) =>
        {
            open.DropDownItems.Clear();
            foreach (var saved in _platform.Layouts.Load())
                open.DropDownItems.Add(saved.Name, null, (_, _) => workspace.OpenLayout(saved));
            if (open.DropDownItems.Count == 0) open.DropDownItems.Add(new Forms.ToolStripMenuItem("(No saved layouts)") { Enabled = false });
        };
        // A placeholder: without any item the menu shows no submenu to open.
        open.DropDownItems.Add("");
        menu.Items.Add(open);
        menu.Items.Add("Layouts…", null, (_, _) => ((IShell)this).ShowLayouts());
        menu.Items.Add("Settings…", null, (_, _) => ((IShell)this).ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => workspace.Quit());
        using var iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Dockyard.Presentation;component/Assets/Dockyard.ico"))!.Stream;
        // The small entry the tray draws at this DPI (16 px at 100%), rather than a larger one scaled down.
        using var icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize);
        _trayIcon = (System.Drawing.Icon)icon.Clone();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Dockyard",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => workspace.NewGroup();
        _tray.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
        _tray.BalloonTipClosed += (_, _) => _balloonClick = null;
    }
}
