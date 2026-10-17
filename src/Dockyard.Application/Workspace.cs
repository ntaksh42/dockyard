using Dockyard.Domain;
using Dockyard.Application.Internal;

namespace Dockyard.Application;

/// <summary>
/// The entry point of the application layer: all open groups and what happens between them. It routes window
/// events and shortcuts to the right group, docks dragged windows, opens and saves layouts, and keeps the
/// session file current. The machine comes in through an <see cref="IPlatform"/>, the windows through an
/// <see cref="IShell"/>.
/// </summary>
public sealed class Workspace : IFrameContext
{
    private readonly IPlatform _platform;
    private readonly IShell _shell;
    private readonly IWindowSystem _windows;
    private readonly IInputState _input;
    private readonly Settings _settings;
    private readonly List<OpenGroup> _groups = [];
    private readonly DockDragSession _dockDrag;
    private readonly IRestartableTimer _foregroundPoll;
    private readonly IRestartableTimer _externalDragPoll;
    private readonly IRestartableTimer _sessionSave;
    private Dictionary<ShortcutAction, ShortcutKey> _shortcuts;
    /// <summary>The layouts' hotkeys by key, kept here because the keyboard hook asks on every key press.</summary>
    private Dictionary<ShortcutKey, LayoutName> _layoutHotkeys = [];
    private bool _externalPolling;
    private WindowHandle _externalDrag;
    private WindowHandle _finishedExternalDrag;
    private (WindowHandle Window, PixelRect Bounds)? _dragOrigin;
    /// <summary>Set while quitting: the groups are closed one by one then, which must not empty the last groups.</summary>
    private bool _quitting;
    /// <summary>Set from the moment Windows asks to end the session: apps then close, which must not be written down.</summary>
    private bool _sessionEnding;
    private readonly IRestartableTimer _sessionEndGuard;
    /// <summary>The group the user last worked in, to give the focus back to when one of Dockyard's own windows closes.</summary>
    private OpenGroup? _lastGroup;
    /// <summary>
    /// Top-level windows created while Dockyard runs and not offered to waiting tabs yet. Only these can fill a
    /// waiting tab: a window that already existed, such as one a minimized group lets go and shows again, is not
    /// the app's window opening.
    /// </summary>
    private readonly HashSet<WindowHandle> _fresh = [];

    public Workspace(IPlatform platform, IShell shell)
    {
        _platform = platform;
        _shell = shell;
        _windows = platform.Windows;
        _input = platform.Input;
        _settings = platform.Settings.Load();
        _shortcuts = Shortcuts.Load(_settings.Shortcuts);
        var scheduler = shell.Scheduler;
        _dockDrag = new DockDragSession(_windows, _input, scheduler, shell.DockGuide, () => _groups);
        _foregroundPoll = scheduler.CreateTimer(TimeSpan.FromMilliseconds(200), PollForeground);
        _externalDragPoll = scheduler.CreateTimer(TimeSpan.FromMilliseconds(30), PollExternalDrag);
        _sessionSave = scheduler.CreateTimer(TimeSpan.FromMilliseconds(500), WriteSessionWhenIdle);
        _sessionEndGuard = scheduler.CreateTimer(TimeSpan.FromSeconds(10), ResumeAfterCancelledSessionEnd);
    }

    /// <summary>Safety net for foreground changes Windows does not report.</summary>
    private void PollForeground()
    {
        _platform.WindowEvents.CheckForeground();
        _foregroundPoll.Restart();
    }

    private void PollExternalDrag()
    {
        CheckExternalDrag();
        if (_externalPolling) _externalDragPoll.Restart();
    }

    private void WriteSessionWhenIdle()
    {
        // Dragging a frame, a splitter or a member changes the layout many times: write once it is released.
        if (_input.IsLeftButtonDown)
        {
            _sessionSave.Restart();
            return;
        }
        SaveSession();
    }

    /// <summary>The preferences. Change them through the methods here, which also apply them to open groups.</summary>
    public Settings Settings => _settings;

    public IReadOnlyDictionary<ShortcutAction, ShortcutKey> ShortcutBindings => _shortcuts;

    /// <summary>
    /// Starts listening and opens the groups: those of a crashed run, the last ones, the saved layout
    /// <paramref name="openLayout"/> if one is asked for (the shortcut file that started Dockyard), or an empty one.
    /// <paramref name="newGroup"/> also opens an empty group when saved groups were restored.
    /// </summary>
    public void Start(LayoutName? openLayout = null, bool newGroup = false)
    {
        _platform.WindowEvents.ShiftMouseDown += (window, bounds) =>
        {
            _dragOrigin = window.IsNone ? null : (window, bounds);
            if (!window.IsNone) StartExternalPoll();
        };
        _platform.WindowEvents.Received += Dispatch;
        RefreshLayoutHotkeys();
        _platform.Keyboard.KeyPressed = HandleShortcut;
        _foregroundPoll.Restart();
        var restored = RestoreSession() || (_settings.OpenLastGroups && OpenLastGroups());
        var opened = openLayout is not null && OpenLayoutByName(openLayout);
        if (newGroup || (!restored && !opened)) NewGroup();
    }

    /// <summary>Stops listening and puts every member window back where it was before it joined a group.</summary>
    public void Stop()
    {
        _platform.Keyboard.KeyPressed = null;
        _foregroundPoll.Stop();
        StopExternalPoll();
        _dockDrag.Finish(true);
        ReleaseAll();
    }

    /// <summary>
    /// Puts every member window back and removes the session file. Also what an unrecoverable error does before
    /// the process dies.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var g in _groups) g.Controller.ReleaseAll();
        _sessionSave.Stop();
        _platform.Sessions.ClearSession();
    }

    // ---- commands the user gives ----------------------------------------------------------------

    public void NewGroup() => NewGroup(null, null);

    /// <summary>
    /// Opens a saved layout, filling its tabs with matching windows that are open now, as a new group.
    /// </summary>
    public void OpenLayout(SavedGroup saved) => OpenLayout(saved, null, _settings.LaunchMissingApps);

    /// <summary>
    /// Opens the saved layout of that name, for a hotkey or a shortcut file. Asking for a layout this way is asking
    /// for the whole workspace, so the apps it lacks are started whatever the setting says. If a group of that name
    /// is open already it is brought forward instead: pressing the hotkey again must not start every app twice.
    /// Returns false (and tells the user) if there is no such layout.
    /// </summary>
    public bool OpenLayoutByName(LayoutName name)
    {
        if (_platform.Layouts.Load().Find(l => l.Name == name) is not { } saved)
        {
            _shell.Notify($"There is no saved layout named \"{name}\".");
            return false;
        }
        if (_groups.FirstOrDefault(g => g.Frame.GroupName == name) is { } open)
        {
            if (open.Frame.IsMinimized) open.Frame.Restore();
            _windows.Focus(open.Frame.Handle);
            return true;
        }
        OpenLayout(saved, null, launchMissing: true);
        return true;
    }

    /// <summary>
    /// Gives the saved layout a hotkey that opens it from anywhere, or removes it with null. A key does one thing:
    /// this, <see cref="BindShortcut"/> and <see cref="SetShortcuts"/> take it from whatever action or layout had it.
    /// </summary>
    public void SetLayoutHotkey(LayoutName name, ShortcutKey? hotkey)
    {
        var layouts = _platform.Layouts.Load();
        if (!LayoutLibrary.SetHotkey(layouts, name, hotkey)) return;
        _platform.Layouts.Save(layouts);
        RefreshLayoutHotkeys();
        if (hotkey is { } key && _shortcuts.ContainsValue(key))
            SetShortcuts(_shortcuts.Where(b => b.Value != key).ToDictionary());
    }

    /// <summary>The saved layouts were changed outside the workspace (renamed, deleted): their hotkeys are read again.</summary>
    public void LayoutsChanged() => RefreshLayoutHotkeys();

    /// <summary>Puts a shortcut file for the saved layout on the desktop.</summary>
    public void CreateLayoutShortcut(LayoutName name)
    {
        var path = _platform.LayoutShortcuts.CreateOnDesktop(name);
        _shell.Notify(path is null
            ? $"Could not create a shortcut for \"{name}\"."
            : $"Created the shortcut \"{Path.GetFileNameWithoutExtension(path)}\" on the desktop.");
    }

    /// <summary>
    /// Windows asks to log off or shut down, before any app has closed. What is open now is what the next start
    /// should reopen: it is written once, and the closing of the apps that follows is not written over it.
    /// </summary>
    public void SessionEnding()
    {
        if (_quitting) return;
        _sessionSave.Stop();
        SaveSession();
        _sessionEnding = true;
        _sessionEndGuard.Restart();
    }

    /// <summary>The session did not end after all (another app or the user cancelled): Dockyard is still running.</summary>
    private void ResumeAfterCancelledSessionEnd()
    {
        _sessionEnding = false;
        RequestSessionSave();
    }

    public void Quit()
    {
        // A change made just now may still wait for the session timer: the next start opens what is saved here.
        _sessionSave.Stop();
        SaveSession();
        _quitting = true;
        foreach (var g in _groups.ToList()) g.Frame.Close();
        _shell.Exit();
    }

    /// <summary>
    /// Dockyard's own windows belong to no group: when one closes, Windows activates a window of its choosing,
    /// which can be an inactive tab behind a frame and would then become the active tab. Give the focus back
    /// to the tab the user last worked in first.
    /// </summary>
    public void ReturnFocusToGroup()
    {
        if (_lastGroup is { Frame.CanDock: true } g && _groups.Contains(g) && g.Controller.LastActive is { } tab) _windows.Focus(tab);
    }

    // ---- settings -------------------------------------------------------------------------------

    public void SetTabBarPlacement(TabBarPlacement placement)
    {
        _settings.TabBarPlacement = placement;
        SaveSettings();
        foreach (var g in _groups) g.Frame.SetTabBarPlacement(placement);
    }

    /// <summary>Sets the default for new groups and applies it to every open group, like the tab bar position.</summary>
    public void SetAutoHideTitleBars(bool enabled)
    {
        _settings.AutoHideTitleBars = enabled;
        SaveSettings();
        foreach (var g in _groups) g.Controller.SetAutoHideTitleBars(enabled);
    }

    public void SetTitleBarHideDelay(int milliseconds)
    {
        _settings.TitleBarHideDelayMs = Math.Clamp(milliseconds, 0, Settings.MaxTitleBarHideDelayMs);
        SaveSettings();
        foreach (var g in _groups) g.Controller.SetTitleBarHideDelay(TimeSpan.FromMilliseconds(_settings.TitleBarHideDelayMs));
    }

    /// <summary>Binds the key to the action, taking it from the other action or the layout that had it.</summary>
    public void BindShortcut(ShortcutAction action, ShortcutKey key)
    {
        var bindings = _shortcuts.Where(b => b.Value != key).ToDictionary();
        bindings[action] = key;
        SetShortcuts(bindings);
    }

    /// <summary>Replaces every binding; a layout whose hotkey an action is bound to now loses it.</summary>
    public void SetShortcuts(IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings)
    {
        _shortcuts = bindings.ToDictionary();
        _settings.Shortcuts = Shortcuts.Save(_shortcuts);
        SaveSettings();
        DropLayoutHotkeysUsedByActions();
    }

    public void SetTitleBarApps(IReadOnlyList<ProcessName> apps)
    {
        _settings.TitleBarApps = [.. apps];
        SaveSettings();
        foreach (var g in _groups) g.Controller.SetTitleBarApps(apps);
    }

    /// <summary>Saves options that were changed in place and need no more than that.</summary>
    public void SaveSettings() => _platform.Settings.Save(_settings);

    // ---- IFrameContext --------------------------------------------------------------------------

    bool IFrameContext.IsGrouped(WindowHandle window) => IsGrouped(window);

    IReadOnlyList<SavedGroup> IFrameContext.SavedLayouts() => _platform.Layouts.Load();

    IReadOnlyDictionary<ShortcutAction, ShortcutKey> IFrameContext.ShortcutBindings() => _shortcuts;

    // ---- groups ---------------------------------------------------------------------------------

    private bool IsGrouped(WindowHandle window) => _groups.Any(g => g.Controller.Owns(window));

    private OpenGroup NewGroup(LayoutName? name, PixelRect? bounds)
    {
        var frame = _shell.CreateFrame(name ?? NextGroupName(), _settings.TabBarPlacement, this);
        var controller = new GroupController(frame, _windows, _shell.Scheduler);
        var g = new OpenGroup(frame, controller);
        frame.Bind(controller);
        controller.SetTitleBarApps(_settings.TitleBarApps ?? Settings.DefaultTitleBarApps);
        controller.SetAutoHideTitleBars(_settings.AutoHideTitleBars);
        controller.SetTitleBarHideDelay(TimeSpan.FromMilliseconds(_settings.TitleBarHideDelayMs));
        frame.FrameActivated += () => _lastGroup = g;
        frame.SaveLayoutRequested += _ => SaveLayout(g);
        frame.OpenLayoutRequested += (target, saved) =>
            OpenLayout(saved, _groups.FirstOrDefault(x => x.Frame == target), _settings.LaunchMissingApps);
        controller.Changed += RequestSessionSave;
        frame.FrameMoved += RequestSessionSave;
        frame.Renamed += RequestSessionSave;
        frame.TabDragStarted += (source, window) => _dockDrag.Begin(window, _groups.FirstOrDefault(x => x.Frame == source));
        frame.TabDragEnded += cancel => _dockDrag.Finish(cancel);
        frame.SettingsRequested += _shell.ShowSettings;
        frame.FrameClosed += () =>
        {
            _groups.Remove(g);
            RequestSessionSave();
        };
        _groups.Add(g);
        frame.ShowAt(bounds);
        return g;
    }

    /// <summary>
    /// "Group N" with the smallest N no open group has. Layouts are saved under the group's name, so a second
    /// group of the same name would replace the first one's layout.
    /// </summary>
    private LayoutName NextGroupName()
    {
        var used = _groups.Select(g => g.Frame.GroupName).ToHashSet();
        var number = 1;
        while (used.Contains(new LayoutName($"Group {number}"))) number++;
        return new LayoutName($"Group {number}");
    }

    private static SavedGroup Save(OpenGroup g, bool includeWindows) =>
        new(g.Frame.GroupName, g.Frame.FrameBounds, g.Controller.SaveLayout(includeWindows), g.Controller.Metrics.TabBarPlacement,
            AutoHideTitleBars: g.Controller.AutoHideTitleBars);

    /// <summary>The layout without the windows of the run that saved it: see <see cref="IGroupControl.SaveLayout"/>.</summary>
    private static SavedNode WithoutWindows(SavedNode node) => node with
    {
        First = node.First is null ? null : WithoutWindows(node.First),
        Second = node.Second is null ? null : WithoutWindows(node.Second),
        Tabs = node.Tabs?.Select(tab => tab with { Handle = WindowHandle.None, OriginalBounds = null }).ToList(),
    };

    /// <summary>Takes a saved group's name, tab bar placement, title bar auto-hide and layout (see <see cref="GroupController.RestoreLayout"/>).</summary>
    private static int Restore(OpenGroup g, SavedGroup saved, Func<SavedTab, WindowHandle> resolve)
    {
        g.Frame.GroupName = saved.Name;
        if (saved.TabBarPlacement is { } placement) g.Frame.SetTabBarPlacement(placement);
        if (saved.AutoHideTitleBars is { } autoHide) g.Controller.SetAutoHideTitleBars(autoHide);
        return g.Controller.RestoreLayout(saved.Layout, resolve);
    }

    /// <summary>Saves the group's layout under its name, replacing a layout saved with the same name (Q3).</summary>
    private void SaveLayout(OpenGroup g)
    {
        var layouts = _platform.Layouts.Load();
        var replaced = LayoutLibrary.Put(layouts, Save(g, includeWindows: false));
        _platform.Layouts.Save(layouts);
        var name = g.Frame.GroupName;
        // A new layout is the moment to offer its hotkey; a replaced one has had its chance.
        if (replaced) _shell.Notify($"Replaced the saved layout \"{name}\".");
        else _shell.Notify($"Saved layout \"{name}\". Click to set a hotkey.", () => _shell.ShowLayouts(name));
    }

    /// <summary>
    /// Opens a saved layout, filling its tabs with matching windows that are open now: into
    /// <paramref name="into"/> if that group has no windows (keeping where it is), otherwise as a new group.
    /// </summary>
    private void OpenLayout(SavedGroup saved, OpenGroup? into, bool launchMissing)
    {
        // Files written before the windows were left out still have them: their handles may belong to others now.
        saved = saved with { Layout = WithoutWindows(saved.Layout) };
        var claimed = _groups.SelectMany(g => g.Controller.Members).ToHashSet();
        var candidates = _platform.Catalog.GetCandidates().Where(w => w.Controllable)
            .Select(w => new WindowCandidate(w.Handle, w.ProcessName, w.Title)).ToList();
        var g = into is not null && !into.Controller.Members.Any() ? into : NewGroup(saved.Name, saved.Bounds);
        var assigned = WindowMatcher.MatchAll([.. WindowMatcher.TabsOf(saved.Layout)], candidates, claimed, sameWindowOnly: false);
        Restore(g, saved, tab => assigned.GetValueOrDefault(tab));
        // Their windows then fill the waiting tabs as they appear.
        if (launchMissing)
            foreach (var tab in g.Controller.WaitingTabs)
                if (tab.Path is { } path) _platform.Launcher.Launch(path);
    }

    private void RefreshLayoutHotkeys() => _layoutHotkeys = LayoutLibrary.Hotkeys(_platform.Layouts.Load());

    /// <summary>A key does one thing: a layout loses its hotkey to an action that is bound to the same key now.</summary>
    private void DropLayoutHotkeysUsedByActions()
    {
        var layouts = _platform.Layouts.Load();
        var changed = false;
        foreach (var (key, name) in LayoutLibrary.Hotkeys(layouts))
        {
            if (!_shortcuts.ContainsValue(key)) continue;
            LayoutLibrary.SetHotkey(layouts, name, null);
            changed = true;
        }
        if (changed) _platform.Layouts.Save(layouts);
        RefreshLayoutHotkeys();
    }

    // ---- session --------------------------------------------------------------------------------

    /// <summary>Writes the session file once changes stop for a moment (see the timer's tick).</summary>
    private void RequestSessionSave() => _sessionSave.Restart();

    private void SaveSession()
    {
        if (_quitting || _sessionEnding) return;
        var groups = _groups.Where(g => g.Controller.Members.Any()).Select(g => Save(g, includeWindows: true)).ToList();
        if (groups.Count == 0) _platform.Sessions.ClearSession();
        else _platform.Sessions.SaveSession(groups);
        // Groups still waiting for all their apps (e.g. just opened at login) count as open too.
        _platform.Sessions.SaveLastGroups(_groups.Where(g => g.Controller.Members.Any() || g.Controller.WaitingTabs.Count > 0)
            .Select(g => Save(g, includeWindows: false)).ToList());
    }

    /// <summary>
    /// Opens the groups from the last run with the rules of a saved layout (exe and title), since after a
    /// restart their windows are new ones. Returns false if there were none.
    /// </summary>
    private bool OpenLastGroups()
    {
        if (_platform.Sessions.LoadLastGroups() is not { Count: > 0 } groups) return false;
        foreach (var g in groups) OpenLayout(g, null, _settings.LaunchMissingApps);
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
        if (_platform.Sessions.LoadSession() is not { Count: > 0 } saved) return false;
        var claimed = new HashSet<WindowHandle>();
        var restored = false;
        foreach (var s in saved)
        {
            var g = NewGroup(s.Name, s.Bounds);
            var count = Restore(g, s, tab =>
            {
                var h = tab.Handle;
                return _windows.Exists(h)
                    ? WindowMatcher.Match(tab, [new WindowCandidate(h, _windows.GetProcessName(h), "")], claimed, sameWindowOnly: true)
                    : WindowHandle.None;
            });
            if (count == 0) g.Frame.Close();
            restored |= count > 0;
        }
        return restored;
    }

    // ---- shortcuts ------------------------------------------------------------------------------

    private bool HandleShortcut(int key, KeyModifiers modifiers)
    {
        var foreground = _windows.GetForeground();
        if (_shell.IsCapturingShortcut(foreground))
        {
            // Modifier keys alone are not a shortcut; wait for the key that completes it.
            if (key is >= 0x10 and <= 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) return false;
            _shell.Scheduler.Post(() => _shell.CaptureShortcut(new ShortcutKey(modifiers, key)));
            return true;
        }
        // A layout's hotkey works wherever the focus is: opening a workspace is not an action on the focused group.
        if (_layoutHotkeys.TryGetValue(new ShortcutKey(modifiers, key), out var layoutName))
        {
            _shell.Scheduler.Post(() => OpenLayoutByName(layoutName));
            return true;
        }
        if (Shortcuts.Match(_shortcuts, key, modifiers) is not { } action) return false;
        var group = _groups.FirstOrDefault(g => g.Frame.CanDock &&
            (g.Controller.Owns(foreground) || g.Frame.Handle == foreground));
        if (group is null || (group.Frame.Handle == foreground && group.Frame.IsEditingText)) return false;
        if (Shortcuts.IsFrameAction(action))
        {
            _shell.Scheduler.Post(() =>
            {
                switch (action)
                {
                    case ShortcutAction.NewGroup: NewGroup(); break;
                    case ShortcutAction.ManageLayouts: _shell.ShowLayouts(); break;
                    case ShortcutAction.OpenSettings: _shell.ShowSettings(); break;
                    case ShortcutAction.Quit: Quit(); break;
                    default: group.Frame.RunShortcut(action); break;
                }
            });
            return true;
        }
        var window = group.Controller.Owns(foreground) ? foreground : group.Controller.LastActive;
        if (window is not { } member) return false;
        // Return from the native hook promptly; layout and focus changes run on the UI thread.
        _shell.Scheduler.Post(() => group.Controller.Execute(action, member));
        return true;
    }

    // ---- window events --------------------------------------------------------------------------

    private void StartExternalPoll()
    {
        if (_externalPolling) return;
        _externalPolling = true;
        _externalDragPoll.Restart();
    }

    private void StopExternalPoll()
    {
        _externalPolling = false;
        _externalDragPoll.Stop();
    }

    private void BeginExternalDrag(WindowHandle window, PixelRect? originalBounds)
    {
        _externalDrag = window;
        _finishedExternalDrag = WindowHandle.None;
        _dockDrag.Begin(window, _groups.FirstOrDefault(g => g.Controller.Owns(window)), originalBounds);
        StartExternalPoll();
    }

    private void FinishExternalDrag(bool cancel)
    {
        _finishedExternalDrag = _externalDrag;
        _externalDrag = WindowHandle.None;
        _dragOrigin = null;
        StopExternalPoll();
        _dockDrag.Finish(cancel || _input.IsEscapeDown);
    }

    private void CheckExternalDrag()
    {
        var window = !_externalDrag.IsNone ? _externalDrag : _dragOrigin?.Window ?? WindowHandle.None;
        if (window.IsNone || !_windows.Exists(window) || _input.IsEscapeDown)
        {
            if (!_externalDrag.IsNone) FinishExternalDrag(true);
            _dragOrigin = null;
            StopExternalPoll();
            return;
        }
        if (!_input.IsLeftButtonDown)
        {
            if (!_externalDrag.IsNone) FinishExternalDrag(false);
            _dragOrigin = null;
            StopExternalPoll();
            return;
        }
        if (!_externalDrag.IsNone || !_input.IsShiftDown || _dragOrigin is not { } origin) return;
        // Custom title bars can move windows without a standard caption hit or move/size events.
        // Same-size movement distinguishes that drag from a resize or a click in the client area.
        if (DragInterpreter.Interpret(origin.Bounds.ToRect(), _windows.GetBounds(window).ToRect()).Moved &&
            CanControl(window))
            BeginExternalDrag(window, origin.Bounds);
    }

    private bool CanControl(WindowHandle window) =>
        _platform.Catalog.GetCandidates().Any(w => w.Handle == window && w.Controllable);

    private void Dispatch(WindowEvent evt, WindowHandle window)
    {
        if (evt == WindowEvent.MoveSizeStart) _finishedExternalDrag = WindowHandle.None;
        if (evt == WindowEvent.MoveSizeEnd && _finishedExternalDrag == window)
        {
            _finishedExternalDrag = WindowHandle.None;
            return;
        }
        if (_externalDrag.IsNone && evt == WindowEvent.MoveSizeStart && _input.IsShiftDown && _input.IsLeftButtonDown &&
            _windows.IsCaptionDrag(window) && CanControl(window))
        {
            BeginExternalDrag(window,
                _dragOrigin is { } origin && origin.Window == window ? origin.Bounds : null);
            return;
        }
        if (evt == WindowEvent.LocationChanged && _externalDrag.IsNone && _dragOrigin?.Window == window)
            CheckExternalDrag();
        if (_externalDrag == window && evt is WindowEvent.MoveSizeEnd or WindowEvent.Destroyed)
        {
            FinishExternalDrag(evt == WindowEvent.Destroyed);
            if (evt != WindowEvent.Destroyed) return;
        }
        if (_externalDrag == window && evt is WindowEvent.LocationChanged or WindowEvent.Foreground) return;
        if (evt is WindowEvent.Created or WindowEvent.Shown or WindowEvent.TitleChanged or WindowEvent.Destroyed) OfferToWaitingTabs(evt, window);
        if (_groups.FirstOrDefault(g => g.Controller.Owns(window)) is not { } group) return;
        var controller = group.Controller;
        switch (evt)
        {
            case WindowEvent.Foreground:
                _lastGroup = group;
                controller.OnForeground(window);
                break;
            case WindowEvent.MoveSizeStart: controller.OnMoveSizeStart(window); break;
            case WindowEvent.MoveSizeEnd: controller.OnMoveSizeEnd(window); break;
            case WindowEvent.LocationChanged: controller.OnLocationChanged(window); break;
            case WindowEvent.TitleChanged: controller.OnTitleChanged(window); break;
            case WindowEvent.Hidden: controller.OnHidden(window); break;
            case WindowEvent.Destroyed: controller.OnDestroyed(window); break;
        }
    }

    /// <summary>
    /// A newly opened window may be the one a restored tab waits for (its app was not open when the layout was
    /// restored). It is offered once it is shown with a title: many windows get their title only after they
    /// appear.
    /// </summary>
    private void OfferToWaitingTabs(WindowEvent evt, WindowHandle window)
    {
        switch (evt)
        {
            case WindowEvent.Created:
                if (_windows.IsTopLevel(window)) _fresh.Add(window);
                return;
            case WindowEvent.Destroyed:
                _fresh.Remove(window);
                return;
        }
        if (!_fresh.Contains(window) || _platform.Catalog.Describe(window) is not { Title.Length: > 0 } info) return;
        _fresh.Remove(window);
        if (!info.Controllable || IsGrouped(window)) return;
        // Not only the groups windows can be dropped on: one that is minimized or has the window picker open would
        // never be offered the window again, as it is no longer new. It is placed once the group is back.
        _ = _groups.Any(g => g.Controller.FillWaiting(window, info.ProcessName, info.Title));
    }
}
