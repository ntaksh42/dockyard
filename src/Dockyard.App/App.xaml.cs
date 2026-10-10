using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dockyard.App.Controllers;
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
    private const string LayoutsFile = "layouts.json";
    /// <summary>
    /// The open groups with their exact windows, kept current while Dockyard runs and removed when it puts the
    /// windows back. Finding it at startup means Dockyard went down without doing so.
    /// </summary>
    private const string SessionFile = "session.json";
    /// <summary>The groups open at the last exit, kept like saved layouts to open them again at startup.</summary>
    private const string LastGroupsFile = "last-groups.json";
    /// <summary>Set while quitting: the groups are closed one by one then, which must not empty the last groups.</summary>
    private bool _quitting;
    private readonly DispatcherTimer _sessionSave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private SettingsWindow? _settingsWindow;
    private LayoutsWindow? _layoutsWindow;
    /// <summary>
    /// Top-level windows created while Dockyard runs and not offered to waiting tabs yet. Only these can fill a
    /// waiting tab: a window that already existed, such as one a minimized group lets go and shows again, is not
    /// the app's window opening.
    /// </summary>
    private readonly HashSet<nint> _fresh = [];

    private Dictionary<ShortcutAction, ShortcutKey> _shortcuts = null!;

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
        _shortcuts = Shortcuts.Load(_settings.Shortcuts);
        _keyboardHook = new KeyboardHook { KeyPressed = HandleShortcut };
        _externalDragPoll.Tick += (_, _) => CheckExternalDrag();
        // Safety net for foreground changes Windows does not report (see WinEventHook.CheckForeground).
        _foregroundPoll.Tick += (_, _) => _hook.CheckForeground();
        _foregroundPoll.Start();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Group", null, (_, _) => NewGroup());
        var open = new Forms.ToolStripMenuItem("Open Layout");
        // Rebuilt on opening, so layouts saved meanwhile show up.
        open.DropDownOpening += (_, _) =>
        {
            open.DropDownItems.Clear();
            foreach (var saved in JsonFile.Read<List<SavedGroup>>(LayoutsFile) ?? [])
                open.DropDownItems.Add(saved.Name, null, (_, _) => OpenLayout(saved));
            if (open.DropDownItems.Count == 0) open.DropDownItems.Add(new Forms.ToolStripMenuItem("(No saved layouts)") { Enabled = false });
        };
        // A placeholder: without any item the menu shows no submenu to open.
        open.DropDownItems.Add("");
        menu.Items.Add(open);
        menu.Items.Add("Manage Layouts…", null, (_, _) => ShowLayouts());
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

        _sessionSave.Tick += (_, _) =>
        {
            // Dragging a frame, a splitter or a member changes the layout many times: write once it is released.
            if ((Native.GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0) return;
            _sessionSave.Stop();
            SaveSession();
        };
        if (!RestoreSession() && !(_settings.OpenLastGroups && OpenLastGroups())) NewGroup();
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

    private bool HandleShortcut(int key, KeyModifiers modifiers)
    {
        var foreground = Native.GetForegroundWindow();
        if (_settingsWindow is { IsCapturing: true } settings && settings.Handle == foreground)
        {
            // Modifier keys alone are not a shortcut; wait for the key that completes it.
            if (key is >= 0x10 and <= 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) return false;
            Dispatcher.BeginInvoke(() => settings.Capture(new ShortcutKey(modifiers, key)));
            return true;
        }
        if (Shortcuts.Match(_shortcuts, key, modifiers) is not { } action) return false;
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
        if (evt is WindowEvent.Created or WindowEvent.Shown or WindowEvent.TitleChanged or WindowEvent.Destroyed) OfferToWaitingTabs(evt, window);
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

    /// <summary>
    /// A newly opened window may be the one a restored tab waits for (its app was not open when the layout was
    /// restored). It is offered once it is shown with a title: many windows get their title only after they
    /// appear.
    /// </summary>
    private void OfferToWaitingTabs(WindowEvent evt, nint window)
    {
        switch (evt)
        {
            case WindowEvent.Created:
                if (Native.GetAncestor(window, 2 /* GA_ROOT */) == window) _fresh.Add(window);
                return;
            case WindowEvent.Destroyed:
                _fresh.Remove(window);
                return;
        }
        if (!_fresh.Contains(window) || WindowCatalog.Describe(window) is not { Title.Length: > 0 } info) return;
        _fresh.Remove(window);
        if (!info.Controllable || IsGrouped(window)) return;
        _ = _groups.Any(g => g.CanDock && g.Controller.FillWaiting(window, info.ProcessName, info.Title));
    }

    private GroupWindow NewGroup(string? name = null, PixelRect? bounds = null)
    {
        var g = new GroupWindow(name ?? $"Group {++_groupCount}", _windows, IsGrouped, _settings.TabBarPlacement)
        {
            SavedLayouts = () => JsonFile.Read<List<SavedGroup>>(LayoutsFile) ?? [],
            ShortcutBindings = () => _shortcuts,
        };
        g.Controller.SetTitleBarApps(_settings.TitleBarApps ?? Settings.DefaultTitleBarApps);
        g.SaveLayoutRequested += SaveLayout;
        g.OpenLayoutRequested += (target, saved) => OpenLayout(saved, target);
        g.Controller.Changed += RequestSessionSave;
        g.LocationChanged += (_, _) => RequestSessionSave();
        g.TabDragStarted += (source, window) => _dockDrag.Begin(window, source);
        g.TabDragEnded += cancel => _dockDrag.Finish(cancel);
        g.SettingsRequested += ShowSettings;
        g.Closed += (_, _) =>
        {
            _groups.Remove(g);
            RequestSessionSave();
        };
        _groups.Add(g);
        g.ShowAt(bounds);
        return g;
    }

    /// <summary>Saves the group's layout under its name, replacing a layout saved with the same name (Q3).</summary>
    private void SaveLayout(GroupWindow g)
    {
        var layouts = JsonFile.Read<List<SavedGroup>>(LayoutsFile) ?? [];
        var replaced = LayoutLibrary.Put(layouts, g.Save());
        JsonFile.Write(LayoutsFile, layouts);
        _tray?.ShowBalloonTip(2000, "Dockyard",
            replaced ? $"Replaced the saved layout \"{g.GroupName}\"." : $"Saved layout \"{g.GroupName}\".", Forms.ToolTipIcon.Info);
    }

    private void ShowLayouts()
    {
        if (_layoutsWindow is null)
        {
            _layoutsWindow = new LayoutsWindow(LayoutsFile, saved => OpenLayout(saved));
            _layoutsWindow.Closed += (_, _) => _layoutsWindow = null;
            _layoutsWindow.Show();
        }
        _layoutsWindow.Activate();
    }

    /// <summary>
    /// Opens a saved layout, filling its tabs with matching windows that are open now: into
    /// <paramref name="into"/> if that group has no windows (keeping where it is), otherwise as a new group.
    /// </summary>
    private void OpenLayout(SavedGroup saved, GroupWindow? into = null)
    {
        var claimed = _groups.SelectMany(g => g.Controller.Members).ToHashSet();
        var candidates = WindowCatalog.GetCandidates().Where(w => w.Controllable)
            .Select(w => new WindowCandidate(w.Handle, w.ProcessName, w.Title)).ToList();
        var g = into is not null && !into.Controller.Members.Any() ? into : NewGroup(saved.Name, saved.Bounds);
        g.Restore(saved, tab => WindowMatcher.Match(tab, candidates, claimed, sameWindowOnly: false));
        // Their windows then fill the waiting tabs as they appear.
        if (_settings.LaunchMissingApps)
            foreach (var tab in g.Controller.WaitingTabs)
                Launch(tab);
    }

    private static void Launch(SavedTab tab)
    {
        if (tab.Path is not { } path || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Launch {path}", ex);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings.TabBarPlacement, _shortcuts, placement =>
        {
            _settings.TabBarPlacement = placement;
            _settings.Save();
            foreach (var g in _groups) g.SetTabBarPlacement(placement);
        }, shortcuts =>
        {
            _shortcuts = shortcuts.ToDictionary();
            _settings.Shortcuts = Shortcuts.Save(_shortcuts);
            _settings.Save();
        }, _settings.TitleBarApps ?? Settings.DefaultTitleBarApps, apps =>
        {
            _settings.TitleBarApps = [.. apps];
            _settings.Save();
            foreach (var g in _groups) g.Controller.SetTitleBarApps(apps);
        }, _settings, _settings.Save);

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ReleaseAll()
    {
        foreach (var g in _groups) g.Controller.ReleaseAll();
        _sessionSave.Stop();
        JsonFile.Delete(SessionFile);
    }

    /// <summary>Writes the session file once changes stop for a moment (see the timer's tick).</summary>
    private void RequestSessionSave()
    {
        _sessionSave.Stop();
        _sessionSave.Start();
    }

    private void SaveSession()
    {
        if (_quitting) return;
        var groups = _groups.Where(g => g.Controller.Members.Any())
            .Select(g => g.Save()).ToList();
        if (groups.Count == 0) JsonFile.Delete(SessionFile);
        else JsonFile.Write(SessionFile, groups);
        // Groups still waiting for all their apps (e.g. just opened at login) count as open too.
        JsonFile.Write(LastGroupsFile, _groups.Where(g => g.Controller.Members.Any() || g.Controller.WaitingTabs.Count > 0)
            .Select(g => g.Save()).ToList());
    }

    /// <summary>
    /// Opens the groups from the last run with the rules of a saved layout (exe and title), since after a
    /// restart their windows are new ones. Returns false if there were none.
    /// </summary>
    private bool OpenLastGroups()
    {
        if (JsonFile.Read<List<SavedGroup>>(LastGroupsFile) is not { Count: > 0 } groups) return false;
        foreach (var g in groups) OpenLayout(g);
        return true;
    }

    /// <summary>
    /// After a crash or a forced exit the grouped windows are still where the panes were: put them back into
    /// their groups, keeping where each was before it joined. Only the very same windows are taken (a handle
    /// still owned by the same app); windows hidden by a minimized group count too. Returns false if nothing
    /// was restored.
    /// </summary>
    private bool RestoreSession()
    {
        if (JsonFile.Read<List<SavedGroup>>(SessionFile) is not { Count: > 0 } saved) return false;
        var claimed = new HashSet<nint>();
        var restored = false;
        foreach (var s in saved)
        {
            var g = NewGroup(s.Name, s.Bounds);
            var count = g.Restore(s, tab =>
            {
                var h = (nint)tab.Handle;
                return _windows.Exists(h)
                    ? WindowMatcher.Match(tab, [new WindowCandidate(h, _windows.GetProcessName(h), "")], claimed, sameWindowOnly: true)
                    : 0;
            });
            if (count == 0) g.Close();
            restored |= count > 0;
        }
        return restored;
    }


    private void Quit()
    {
        _quitting = true;
        foreach (var g in _groups.ToList()) g.Close();
        Shutdown();
    }
}
