using System.Windows;
using Dockyard.App.Interop;
using Forms = System.Windows.Forms;

namespace Dockyard.App;

public partial class App : Application
{
    private readonly List<GroupWindow> _groups = [];
    private WinEventHook? _hook;
    private Forms.NotifyIcon? _tray;
    private int _groupCount;
    private nint _lastForeground;
    private readonly System.Windows.Threading.DispatcherTimer _foregroundPoll = new() { Interval = TimeSpan.FromMilliseconds(200) };

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
        _hook.WindowEvent += (evt, hwnd) =>
        {
            if (evt == Native.EVENT_SYSTEM_FOREGROUND) _lastForeground = hwnd;
            _groups.FirstOrDefault(g => g.Owns(hwnd))?.OnMemberEvent(evt, hwnd);
        };
        // Safety net: Windows does not always deliver EVENT_SYSTEM_FOREGROUND (seen when switching between
        // console windows). Without it a clicked member comes forward alone and the rest of its group stays behind.
        _foregroundPoll.Tick += (_, _) =>
        {
            var fg = Native.GetForegroundWindow();
            if (fg == _lastForeground) return;
            _lastForeground = fg;
            _groups.FirstOrDefault(g => g.Owns(fg))?.OnMemberEvent(Native.EVENT_SYSTEM_FOREGROUND, fg);
        };
        _foregroundPoll.Start();

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
