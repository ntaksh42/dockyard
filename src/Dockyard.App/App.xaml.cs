using System.Windows;
using System.Windows.Threading;
using Dockyard.Application;
using Dockyard.Infrastructure;
using Forms = System.Windows.Forms;

namespace Dockyard.App;

/// <summary>Composition root: wires the Win32 infrastructure to each group's controller and runs the tray icon.</summary>
public partial class App : System.Windows.Application
{
    private readonly List<GroupWindow> _groups = [];
    private readonly Win32WindowSystem _windows = new();
    private readonly DispatcherTimer _foregroundPoll = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private WinEventHook? _hook;
    private Forms.NotifyIcon? _tray;
    private int _groupCount;
    private DockDrag _dockDrag = null!;
    private nint _externalDrag;
    private (nint Window, PixelRect Bounds)? _dragOrigin;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A bug in one handler should not take every grouped window down with it: log and keep running.
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("UI", e.Exception);
            e.Handled = true;
        };
        // Exceptions off the UI thread cannot be recovered; put grouped windows back before the process dies.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Fatal", (Exception)e.ExceptionObject);
            Dispatcher.Invoke(ReleaseAll);
        };
        Diagnostics.Trace = Log.Trace;
        _dockDrag = new DockDrag(() => _groups);

        _hook = new WinEventHook();
        _hook.ShiftMouseDown += (window, bounds) => _dragOrigin = window == 0 ? null : (window, bounds);
        _hook.Received += Dispatch;
        // Safety net for foreground changes Windows does not report (see WinEventHook.CheckForeground).
        _foregroundPoll.Tick += (_, _) => _hook.CheckForeground();
        _foregroundPoll.Start();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Group", null, (_, _) => NewGroup());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Dockyard",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => NewGroup();

        NewGroup();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dockDrag.Finish(true);
        ReleaseAll();
        _hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private bool IsGrouped(nint window) => _groups.Any(g => g.Controller.Owns(window));

    private void Dispatch(WindowEvent evt, nint window)
    {
        if (evt == WindowEvent.MoveSizeStart && (Native.GetAsyncKeyState(0x10) & 0x8000) != 0 &&
            (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 &&
            Native.IsCaptionDrag(window) &&
            WindowCatalog.GetCandidates().Any(w => w.Handle == window && w.Controllable))
        {
            _externalDrag = window;
            _dockDrag.Begin(window, _groups.FirstOrDefault(g => g.Controller.Owns(window)),
                _dragOrigin is { } origin && origin.Window == window ? origin.Bounds : null);
            return;
        }
        if (_externalDrag == window && evt is WindowEvent.MoveSizeEnd or WindowEvent.Destroyed)
        {
            _externalDrag = 0;
            _dockDrag.Finish(evt == WindowEvent.Destroyed);
            if (evt != WindowEvent.Destroyed) return;
        }
        if (_externalDrag == window && evt is WindowEvent.LocationChanged or WindowEvent.Foreground) return;
        if (_groups.Select(g => g.Controller).FirstOrDefault(c => c.Owns(window)) is not { } controller) return;
        switch (evt)
        {
            case WindowEvent.Foreground: controller.OnForeground(window); break;
            case WindowEvent.MoveSizeStart: controller.OnMoveSizeStart(window); break;
            case WindowEvent.MoveSizeEnd: controller.OnMoveSizeEnd(window); break;
            case WindowEvent.LocationChanged: controller.OnLocationChanged(window); break;
            case WindowEvent.TitleChanged: controller.OnTitleChanged(window); break;
            case WindowEvent.Destroyed: controller.OnDestroyed(window); break;
        }
    }

    private void NewGroup()
    {
        var g = new GroupWindow($"Group {++_groupCount}", _windows, IsGrouped);
        g.TabDragStarted += (source, window) => _dockDrag.Begin(window, source);
        g.TabDragEnded += cancel => _dockDrag.Finish(cancel);
        g.Closed += (_, _) => _groups.Remove(g);
        _groups.Add(g);
        g.Show();
    }

    private void ReleaseAll()
    {
        foreach (var g in _groups) g.Controller.ReleaseAll();
    }

    private void Quit()
    {
        foreach (var g in _groups.ToList()) g.Close();
        Shutdown();
    }
}
