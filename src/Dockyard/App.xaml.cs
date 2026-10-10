using System.Windows;
using Dockyard.Interop;
using Forms = System.Windows.Forms;

namespace Dockyard;

public partial class App : Application
{
    private readonly List<GroupWindow> _groups = [];
    private WinEventHook? _hook;
    private Forms.NotifyIcon? _tray;
    private int _groupCount;

    public static bool IsGrouped(nint hwnd) => ((App)Current)._groups.Any(g => g.Owns(hwnd));

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

        _hook = new WinEventHook();
        _hook.WindowEvent += (evt, hwnd) => _groups.FirstOrDefault(g => g.Owns(hwnd))?.OnMemberEvent(evt, hwnd);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("新しいグループ", null, (_, _) => NewGroup());
        menu.Items.Add("終了", null, (_, _) => Quit());
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
        ReleaseAll();
        _hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private void NewGroup()
    {
        var g = new GroupWindow($"グループ {++_groupCount}");
        g.Closed += (_, _) => _groups.Remove(g);
        _groups.Add(g);
        g.Show();
    }

    private void ReleaseAll()
    {
        foreach (var g in _groups) g.ReleaseAll();
    }

    private void Quit()
    {
        foreach (var g in _groups.ToList()) g.Close();
        Shutdown();
    }
}
