using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using Dockyard.Application;
using Dockyard.Infrastructure;
using Dockyard.Presentation;

namespace Dockyard.Exe;

/// <summary>Starts Dockyard: one copy per user, error hooks, and the wiring of the three layers.</summary>
public partial class App : System.Windows.Application
{
    private const string NewGroupOption = "--new-group";
    /// <summary>Held while this is the user's Dockyard; null in a second copy, which only exits.</summary>
    private Mutex? _instance;
    private PlatformServices? _platform;
    private WpfShell? _shell;
    private Workspace? _workspace;
    private InstanceRelay? _relay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A shortcut file starts Dockyard with the layout to open (see LaunchCommand).
        var layout = LaunchCommand.ParseLayout(e.Args);
        var newGroup = e.Args.Contains(NewGroupOption, StringComparer.OrdinalIgnoreCase);
        // Two copies would both take the same windows and write the same session file.
        var instance = new Mutex(true, @"Local\Dockyard", out var first);
        if (!first)
        {
            instance.Dispose();
            // The running Dockyard opens the layout; this copy has nothing more to do.
            if (layout is not null || newGroup) InstanceRelay.Send(layout);
            Shutdown();
            return;
        }
        _instance = instance;
        // Hardware rendering costs some 250 MB of driver memory (D3D9On12 on the GPU's D3D12 driver) for a frame that
        // draws flat rectangles and text; software rendering keeps the process near 150 MB.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Installs the window event and keyboard hooks.
        _platform = new PlatformServices();
        var log = _platform.Log;
        // A bug in one handler should not take every grouped window down with it: log and keep running.
        DispatcherUnhandledException += (_, e) =>
        {
            log.Error("UI", e.Exception);
            e.Handled = true;
        };
        // Exceptions off the UI thread cannot be recovered; put grouped windows back before the process dies. Not
        // waiting long for the UI thread: it may be the one that is stuck.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Error("Fatal", (Exception)e.ExceptionObject);
            try { Dispatcher.Invoke(() => _workspace?.ReleaseAll(), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { log.Error("Fatal cleanup", ex); }
        };
        Diagnostics.Trace = log.Trace;
        log.Trace($"start pid={Environment.ProcessId}");

        _shell = new WpfShell(_platform, Shutdown);
        _workspace = new Workspace(_platform, _shell);
        _shell.Attach(_workspace);
        // Before the apps close, or their closing would be written over the groups to reopen at the next start.
        SessionEnding += (_, _) => _workspace.SessionEnding();
        _workspace.Start(layout, newGroup);
        // Later shortcut files reach this copy: their layout is opened on the UI thread.
        _relay = InstanceRelay.Listen(name => Dispatcher.BeginInvoke(() =>
        {
            if (name is null) _workspace.NewGroup();
            else _workspace.OpenLayoutByName(name);
        }), log);
        CreateJumpList();
    }

    private void CreateJumpList()
    {
        var executable = Environment.ProcessPath!;
        var applicationPath = executable;
        try
        {
            // Packaged executables must be launched through the execution alias.
            _ = global::Windows.ApplicationModel.Package.Current;
            applicationPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "dockyard.exe");
        }
        catch (InvalidOperationException)
        {
            // An unpackaged build has no package identity.
        }
        var jumpList = new JumpList();
        jumpList.JumpItems.Add(new JumpTask
        {
            Title = "New Group",
            Description = "Open a new empty group",
            ApplicationPath = applicationPath,
            Arguments = NewGroupOption,
            IconResourcePath = executable,
            IconResourceIndex = 0,
        });
        JumpList.SetJumpList(this, jumpList);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance is null)
        {
            base.OnExit(e);
            return;
        }
        _relay?.Dispose();
        _workspace?.Stop();
        _shell?.Dispose();
        _platform?.Dispose();
        _instance.Dispose();
        base.OnExit(e);
    }
}
