using System.Windows;
using System.Windows.Threading;
using Dockyard.Application;
using Dockyard.Infrastructure;
using Dockyard.Domain;
using Forms = System.Windows.Forms;

namespace Dockyard.App;

/// <summary>Composition root: wires the Win32 infrastructure to each group's controller and runs the tray icon.</summary>
public partial class App : System.Windows.Application
{
    private readonly List<GroupWindow> _groups = [];
    private readonly Win32WindowSystem _windows = new();
    private readonly DispatcherTimer _foregroundPoll = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _externalDragPoll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private WinEventHook? _hook;
    private KeyboardHook? _keyboardHook;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private int _groupCount;
    private DockDrag _dockDrag = null!;
    private nint _externalDrag;
    private nint _finishedExternalDrag;
    private (nint Window, PixelRect Bounds)? _dragOrigin;
    private readonly Settings _settings = Settings.Load();
    private SettingsWindow? _settingsWindow;

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
        _hook.ShiftMouseDown += (window, bounds) =>
        {
            _dragOrigin = window == 0 ? null : (window, bounds);
            if (window != 0) _externalDragPoll.Start();
        };
        _hook.Received += Dispatch;
        _keyboardHook = new KeyboardHook { KeyPressed = HandleShortcut };
        _externalDragPoll.Tick += (_, _) => CheckExternalDrag();
        // Safety net for foreground changes Windows does not report (see WinEventHook.CheckForeground).
        _foregroundPoll.Tick += (_, _) => _hook.CheckForeground();
        _foregroundPoll.Start();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Group", null, (_, _) => NewGroup());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/Dockyard.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(iconStream);
        _trayIcon = (System.Drawing.Icon)icon.Clone();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Dockyard",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => NewGroup();

        NewGroup();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
        _externalDragPoll.Stop();
        _dockDrag.Finish(true);
        ReleaseAll();
        _hook?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private bool IsGrouped(nint window) => _groups.Any(g => g.Controller.Owns(window));

    private bool HandleShortcut(int key, bool shift)
    {
        if (Shortcuts.Match(key, shift) is not { } action) return false;
        var foreground = Native.GetForegroundWindow();
        var group = _groups.FirstOrDefault(g => g.CanDock &&
            (g.Controller.Owns(foreground) || g.Handle == foreground));
        if (group is null || (group.Handle == foreground &&
            System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)) return false;
        var window = group.Controller.Owns(foreground) ? foreground : group.Controller.LastActive;
        if (window is not { } member) return false;
        // Return from the native hook promptly; layout and focus changes run on the UI dispatcher.
        Dispatcher.BeginInvoke(() => group.Controller.Execute(action, member));
        return true;
    }

    private void BeginExternalDrag(nint window, PixelRect? originalBounds)
    {
        _externalDrag = window;
        _finishedExternalDrag = 0;
        _dockDrag.Begin(window, _groups.FirstOrDefault(g => g.Controller.Owns(window)), originalBounds);
        _externalDragPoll.Start();
    }

    private void FinishExternalDrag(bool cancel)
    {
        _finishedExternalDrag = _externalDrag;
        _externalDrag = 0;
        _dragOrigin = null;
        _externalDragPoll.Stop();
        _dockDrag.Finish(cancel || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0);
    }

    private void CheckExternalDrag()
    {
        var window = _externalDrag != 0 ? _externalDrag : _dragOrigin?.Window ?? 0;
        if (window == 0 || !_windows.Exists(window) || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0)
        {
            if (_externalDrag != 0) FinishExternalDrag(true);
            _dragOrigin = null;
            _externalDragPoll.Stop();
            return;
        }
        if ((Native.GetAsyncKeyState(0x01) & 0x8000) == 0)
        {
            if (_externalDrag != 0) FinishExternalDrag(false);
            _dragOrigin = null;
            _externalDragPoll.Stop();
            return;
        }
        if (_externalDrag != 0 || (Native.GetAsyncKeyState(0x10) & 0x8000) == 0 || _dragOrigin is not { } origin) return;
        // Custom title bars can move windows without a standard caption hit or move/size events.
        // Same-size movement distinguishes that drag from a resize or a click in the client area.
        if (DragInterpreter.Interpret(origin.Bounds.ToRect(), _windows.GetBounds(window).ToRect()).Moved &&
            WindowCatalog.GetCandidates().Any(w => w.Handle == window && w.Controllable))
            BeginExternalDrag(window, origin.Bounds);
    }

    private void Dispatch(WindowEvent evt, nint window)
    {
        if (evt == WindowEvent.MoveSizeStart) _finishedExternalDrag = 0;
        if (evt == WindowEvent.MoveSizeEnd && _finishedExternalDrag == window)
        {
            _finishedExternalDrag = 0;
            return;
        }
        if (_externalDrag == 0 && evt == WindowEvent.MoveSizeStart && (Native.GetAsyncKeyState(0x10) & 0x8000) != 0 &&
            (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 &&
            Native.IsCaptionDrag(window) &&
            WindowCatalog.GetCandidates().Any(w => w.Handle == window && w.Controllable))
        {
            BeginExternalDrag(window,
                _dragOrigin is { } origin && origin.Window == window ? origin.Bounds : null);
            return;
        }
        if (evt == WindowEvent.LocationChanged && _externalDrag == 0 && _dragOrigin?.Window == window)
            CheckExternalDrag();
        if (_externalDrag == window && evt is WindowEvent.MoveSizeEnd or WindowEvent.Destroyed)
        {
            FinishExternalDrag(evt == WindowEvent.Destroyed);
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
        var g = new GroupWindow($"Group {++_groupCount}", _windows, IsGrouped, _settings.TabBarPlacement);
        g.TabDragStarted += (source, window) => _dockDrag.Begin(window, source);
        g.TabDragEnded += cancel => _dockDrag.Finish(cancel);
        g.Closed += (_, _) => _groups.Remove(g);
        _groups.Add(g);
        g.Show();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings.TabBarPlacement, placement =>
        {
            _settings.TabBarPlacement = placement;
            _settings.Save();
            foreach (var g in _groups) g.SetTabBarPlacement(placement);
        });
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
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
