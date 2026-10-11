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
    private int _groupCount;
    private bool _externalPolling;
    private nint _externalDrag;
    private nint _finishedExternalDrag;
    private (nint Window, PixelRect Bounds)? _dragOrigin;
    /// <summary>Set while quitting: the groups are closed one by one then, which must not empty the last groups.</summary>
    private bool _quitting;
    /// <summary>The group the user last worked in, to give the focus back to when one of Dockyard's own windows closes.</summary>
    private OpenGroup? _lastGroup;
    /// <summary>
    /// Top-level windows created while Dockyard runs and not offered to waiting tabs yet. Only these can fill a
    /// waiting tab: a window that already existed, such as one a minimized group lets go and shows again, is not
    /// the app's window opening.
    /// </summary>
    private readonly HashSet<nint> _fresh = [];

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

    /// <summary>Starts listening and opens the groups: those of a crashed run, the last ones, or an empty one.</summary>
    public void Start()
    {
        _platform.WindowEvents.ShiftMouseDown += (window, bounds) =>
        {
            _dragOrigin = window == 0 ? null : (window, bounds);
            if (window != 0) StartExternalPoll();
        };
        _platform.WindowEvents.Received += Dispatch;
        _platform.Keyboard.KeyPressed = HandleShortcut;
        _foregroundPoll.Restart();
        if (!RestoreSession() && !(_settings.OpenLastGroups && OpenLastGroups())) NewGroup();
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
    public void OpenLayout(SavedGroup saved) => OpenLayout(saved, null);

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

    public void SetShortcuts(IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings)
    {
        _shortcuts = bindings.ToDictionary();
        _settings.Shortcuts = Shortcuts.Save(_shortcuts);
        SaveSettings();
    }

    public void SetTitleBarApps(IReadOnlyList<string> apps)
    {
        _settings.TitleBarApps = [.. apps];
        SaveSettings();
        foreach (var g in _groups) g.Controller.SetTitleBarApps(apps);
    }

    /// <summary>Saves options that were changed in place and need no more than that.</summary>
    public void SaveSettings() => _platform.Settings.Save(_settings);

    // ---- IFrameContext --------------------------------------------------------------------------

    bool IFrameContext.IsGrouped(nint window) => IsGrouped(window);

    IReadOnlyList<SavedGroup> IFrameContext.SavedLayouts() => _platform.Layouts.Load();

    IReadOnlyDictionary<ShortcutAction, ShortcutKey> IFrameContext.ShortcutBindings() => _shortcuts;

    // ---- groups ---------------------------------------------------------------------------------

    private bool IsGrouped(nint window) => _groups.Any(g => g.Controller.Owns(window));

    private OpenGroup NewGroup(string? name, PixelRect? bounds)
    {
        var frame = _shell.CreateFrame(name ?? $"Group {++_groupCount}", _settings.TabBarPlacement, this);
        var controller = new GroupController(frame, _windows, _shell.Scheduler);
        var g = new OpenGroup(frame, controller);
        frame.Bind(controller);
        controller.SetTitleBarApps(_settings.TitleBarApps ?? Settings.DefaultTitleBarApps);
        frame.FrameActivated += () => _lastGroup = g;
        frame.SaveLayoutRequested += _ => SaveLayout(g);
        frame.OpenLayoutRequested += (target, saved) => OpenLayout(saved, _groups.FirstOrDefault(x => x.Frame == target));
        controller.Changed += RequestSessionSave;
        frame.FrameMoved += RequestSessionSave;
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

    private static SavedGroup Save(OpenGroup g) =>
        new(g.Frame.GroupName, g.Frame.FrameBounds, g.Controller.SaveLayout(), g.Controller.Metrics.TabBarPlacement);

    /// <summary>Takes a saved group's name, tab bar placement and layout (see <see cref="GroupController.RestoreLayout"/>).</summary>
    private static int Restore(OpenGroup g, SavedGroup saved, Func<SavedTab, nint> resolve)
    {
        g.Frame.GroupName = saved.Name;
        if (saved.TabBarPlacement is { } placement) g.Frame.SetTabBarPlacement(placement);
        return g.Controller.RestoreLayout(saved.Layout, resolve);
    }

    /// <summary>Saves the group's layout under its name, replacing a layout saved with the same name (Q3).</summary>
    private void SaveLayout(OpenGroup g)
    {
        var layouts = _platform.Layouts.Load();
        var replaced = LayoutLibrary.Put(layouts, Save(g));
        _platform.Layouts.Save(layouts);
        _shell.Notify(replaced ? $"Replaced the saved layout \"{g.Frame.GroupName}\"." : $"Saved layout \"{g.Frame.GroupName}\".");
    }

    /// <summary>
    /// Opens a saved layout, filling its tabs with matching windows that are open now: into
    /// <paramref name="into"/> if that group has no windows (keeping where it is), otherwise as a new group.
    /// </summary>
    private void OpenLayout(SavedGroup saved, OpenGroup? into)
    {
        var claimed = _groups.SelectMany(g => g.Controller.Members).ToHashSet();
        var candidates = _platform.Catalog.GetCandidates().Where(w => w.Controllable)
            .Select(w => new WindowCandidate(w.Handle, w.ProcessName, w.Title)).ToList();
        var g = into is not null && !into.Controller.Members.Any() ? into : NewGroup(saved.Name, saved.Bounds);
        Restore(g, saved, tab => WindowMatcher.Match(tab, candidates, claimed, sameWindowOnly: false));
        // Their windows then fill the waiting tabs as they appear.
        if (_settings.LaunchMissingApps)
            foreach (var tab in g.Controller.WaitingTabs)
                if (tab.Path is { } path) _platform.Launcher.Launch(path);
    }

    // ---- session --------------------------------------------------------------------------------

    /// <summary>Writes the session file once changes stop for a moment (see the timer's tick).</summary>
    private void RequestSessionSave() => _sessionSave.Restart();

    private void SaveSession()
    {
        if (_quitting) return;
        var groups = _groups.Where(g => g.Controller.Members.Any()).Select(Save).ToList();
        if (groups.Count == 0) _platform.Sessions.ClearSession();
        else _platform.Sessions.SaveSession(groups);
        // Groups still waiting for all their apps (e.g. just opened at login) count as open too.
        _platform.Sessions.SaveLastGroups(_groups.Where(g => g.Controller.Members.Any() || g.Controller.WaitingTabs.Count > 0)
            .Select(Save).ToList());
    }

    /// <summary>
    /// Opens the groups from the last run with the rules of a saved layout (exe and title), since after a
    /// restart their windows are new ones. Returns false if there were none.
    /// </summary>
    private bool OpenLastGroups()
    {
        if (_platform.Sessions.LoadLastGroups() is not { Count: > 0 } groups) return false;
        foreach (var g in groups) OpenLayout(g, null);
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
        var claimed = new HashSet<nint>();
        var restored = false;
        foreach (var s in saved)
        {
            var g = NewGroup(s.Name, s.Bounds);
            var count = Restore(g, s, tab =>
            {
                var h = (nint)tab.Handle;
                return _windows.Exists(h)
                    ? WindowMatcher.Match(tab, [new WindowCandidate(h, _windows.GetProcessName(h), "")], claimed, sameWindowOnly: true)
                    : 0;
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

    private void BeginExternalDrag(nint window, PixelRect? originalBounds)
    {
        _externalDrag = window;
        _finishedExternalDrag = 0;
        _dockDrag.Begin(window, _groups.FirstOrDefault(g => g.Controller.Owns(window)), originalBounds);
        StartExternalPoll();
    }

    private void FinishExternalDrag(bool cancel)
    {
        _finishedExternalDrag = _externalDrag;
        _externalDrag = 0;
        _dragOrigin = null;
        StopExternalPoll();
        _dockDrag.Finish(cancel || _input.IsEscapeDown);
    }

    private void CheckExternalDrag()
    {
        var window = _externalDrag != 0 ? _externalDrag : _dragOrigin?.Window ?? 0;
        if (window == 0 || !_windows.Exists(window) || _input.IsEscapeDown)
        {
            if (_externalDrag != 0) FinishExternalDrag(true);
            _dragOrigin = null;
            StopExternalPoll();
            return;
        }
        if (!_input.IsLeftButtonDown)
        {
            if (_externalDrag != 0) FinishExternalDrag(false);
            _dragOrigin = null;
            StopExternalPoll();
            return;
        }
        if (_externalDrag != 0 || !_input.IsShiftDown || _dragOrigin is not { } origin) return;
        // Custom title bars can move windows without a standard caption hit or move/size events.
        // Same-size movement distinguishes that drag from a resize or a click in the client area.
        if (DragInterpreter.Interpret(origin.Bounds.ToRect(), _windows.GetBounds(window).ToRect()).Moved &&
            CanControl(window))
            BeginExternalDrag(window, origin.Bounds);
    }

    private bool CanControl(nint window) =>
        _platform.Catalog.GetCandidates().Any(w => w.Handle == window && w.Controllable);

    private void Dispatch(WindowEvent evt, nint window)
    {
        if (evt == WindowEvent.MoveSizeStart) _finishedExternalDrag = 0;
        if (evt == WindowEvent.MoveSizeEnd && _finishedExternalDrag == window)
        {
            _finishedExternalDrag = 0;
            return;
        }
        if (_externalDrag == 0 && evt == WindowEvent.MoveSizeStart && _input.IsShiftDown && _input.IsLeftButtonDown &&
            _windows.IsCaptionDrag(window) && CanControl(window))
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
                if (_windows.IsTopLevel(window)) _fresh.Add(window);
                return;
            case WindowEvent.Destroyed:
                _fresh.Remove(window);
                return;
        }
        if (!_fresh.Contains(window) || _platform.Catalog.Describe(window) is not { Title.Length: > 0 } info) return;
        _fresh.Remove(window);
        if (!info.Controllable || IsGrouped(window)) return;
        _ = _groups.Any(g => g.Frame.CanDock && g.Controller.FillWaiting(window, info.ProcessName, info.Title));
    }
}
