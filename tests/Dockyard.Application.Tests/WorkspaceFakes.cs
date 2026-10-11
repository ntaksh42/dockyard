using Dockyard.Domain;
using Dockyard.Application.Internal;

namespace Dockyard.Application.Tests;

/// <summary>A frame that records what the workspace does to it and lets a test play the user.</summary>
internal sealed class FakeFrame(FakeView view, ManualScheduler scheduler) : IGroupFrame
{
    public GroupController? Controller { get; private set; }
    public bool Closed { get; private set; }
    public PixelRect? ShownAt { get; private set; }
    public bool Shown { get; private set; }
    public TabBarPlacement Placement { get; private set; }
    public List<ShortcutAction> ShortcutsRun { get; } = [];

    public string GroupName { get; set; } = "";
    public bool CanDock { get; set; } = true;
    public bool IsEditingText { get; set; }
    public PixelRect FrameBounds { get; set; } = new(100, 100, 1100, 800);

    // ---- IGroupView, as the inner view -------------------------------------------------------------
    public nint Handle => view.Handle;
    public bool IsOnScreen => view.IsOnScreen;
    public bool IsMinimized => view.IsMinimized;
    public Size SurfaceSize => view.SurfaceSize;
    public double Scale => view.Scale;
    public nint ZAnchor => view.ZAnchor;
    public nint TabOverlay => view.TabOverlay;
    public PixelRect ToScreen(Rect surfaceRect) => view.ToScreen(surfaceRect);
    public Rect FromScreen(PixelRect screenRect) => view.FromScreen(screenRect);
    public void Render(GroupSnapshot snapshot) => view.Render(snapshot);
    public void MoveBy(double dx, double dy) => view.MoveBy(dx, dy);
    public void ResizeEdge(DockSide edge, double delta) => view.ResizeEdge(edge, delta);
    public void SetMinimumContentSize(Size min) => view.SetMinimumContentSize(min);
    public void Minimize() => view.Minimize();
    public void Restore() => view.Restore();

    // ---- IGroupFrame ----------------------------------------------------------------------------
    public void Bind(IGroupControl control)
    {
        Controller = (GroupController)control;
        view.Connect(Controller, scheduler);
        Controller.OnSurfaceResized();
    }

    public void ShowAt(PixelRect? bounds)
    {
        Shown = true;
        ShownAt = bounds;
    }

    public void SetTabBarPlacement(TabBarPlacement placement)
    {
        Placement = placement;
        Controller?.SetTabBarPlacement(placement);
    }

    public void RunShortcut(ShortcutAction action) => ShortcutsRun.Add(action);

    public void Close()
    {
        if (Closed) return;
        Closed = true;
        Controller?.OnClosing();
        FrameClosed?.Invoke();
    }

    public event Action? FrameActivated;
    public event Action? FrameMoved;
    public event Action? FrameClosed;
    public event Action<IGroupFrame, nint>? TabDragStarted;
    public event Action<bool>? TabDragEnded;
    public event Action? SettingsRequested;
    public event Action<IGroupFrame>? SaveLayoutRequested;
    public event Action<IGroupFrame, SavedGroup>? OpenLayoutRequested;

    public void UserActivates() => FrameActivated?.Invoke();
    public void UserMovesFrame() => FrameMoved?.Invoke();
    public void UserStartsTabDrag(nint window) => TabDragStarted?.Invoke(this, window);
    public void UserEndsTabDrag(bool cancel) => TabDragEnded?.Invoke(cancel);
    public void UserAsksForSettings() => SettingsRequested?.Invoke();
    public void UserSavesLayout() => SaveLayoutRequested?.Invoke(this);
    public void UserOpensLayout(SavedGroup saved) => OpenLayoutRequested?.Invoke(this, saved);
}

internal sealed class FakeDockGuide : IDockGuide
{
    public int Shown { get; private set; }
    public int Hidden { get; private set; }
    public void Show(IGroupFrame group, DockTarget drop) => Shown++;
    public void Hide() => Hidden++;
}

internal sealed class FakeShell(ManualScheduler scheduler) : IShell
{
    private nint _next = 1000;

    public FakeWindowSystem? Windows { get; set; }
    public List<FakeFrame> Frames { get; } = [];
    public List<string> Notifications { get; } = [];
    public FakeDockGuide Guide { get; } = new();
    public bool Capturing { get; set; }
    public List<ShortcutKey> Captured { get; } = [];
    public int SettingsShown { get; private set; }
    public int LayoutsShown { get; private set; }
    public bool Exited { get; private set; }

    public IScheduler Scheduler => scheduler;
    public IDockGuide DockGuide => Guide;

    public IGroupFrame CreateFrame(string name, TabBarPlacement placement, IFrameContext context)
    {
        var handle = _next++;
        Windows?.AddToZOrder(handle);
        var frame = new FakeFrame(new FakeView(handle), scheduler) { GroupName = name };
        Frames.Add(frame);
        return frame;
    }

    public bool IsCapturingShortcut(nint foreground) => Capturing;
    public void CaptureShortcut(ShortcutKey key) => Captured.Add(key);
    public void ShowSettings() => SettingsShown++;
    public void ShowLayouts() => LayoutsShown++;
    public void Notify(string message) => Notifications.Add(message);
    public void Exit() => Exited = true;
}

internal sealed class FakeCatalog : IWindowCatalog
{
    public List<WindowInfo> Windows { get; } = [];

    public IReadOnlyList<WindowInfo> GetCandidates() => Windows.Where(w => w.Title.Length > 0).ToList();
    public WindowInfo? Describe(nint window) => Windows.FirstOrDefault(w => w.Handle == window);
}

internal sealed class FakeInput : IInputState
{
    public bool IsLeftButtonDown { get; set; }
    public bool IsShiftDown { get; set; }
    public bool IsEscapeDown { get; set; }
    public (int X, int Y) Cursor { get; set; }

    public bool TryGetCursor(out int x, out int y)
    {
        (x, y) = Cursor;
        return true;
    }
}

internal sealed class FakeWindowEvents : IWindowEventSource
{
    public event Action<WindowEvent, nint>? Received;
    public event Action<nint, PixelRect>? ShiftMouseDown;
    public int ForegroundChecks { get; private set; }

    public void CheckForeground() => ForegroundChecks++;
    public void Raise(WindowEvent evt, nint window) => Received?.Invoke(evt, window);
    public void RaiseShiftMouseDown(nint window, PixelRect bounds) => ShiftMouseDown?.Invoke(window, bounds);
}

internal sealed class FakeKeyboard : IKeyboardSource
{
    public Func<int, KeyModifiers, bool>? KeyPressed { get; set; }

    public bool Press(int key, KeyModifiers modifiers) => KeyPressed?.Invoke(key, modifiers) ?? false;
}

internal sealed class MemoryLayoutStore : ILayoutStore
{
    public List<SavedGroup> Layouts { get; private set; } = [];

    public List<SavedGroup> Load() => [.. Layouts];
    public void Save(IReadOnlyList<SavedGroup> layouts) => Layouts = [.. layouts];
}

internal sealed class MemorySessionStore : ISessionStore
{
    public List<SavedGroup>? Session { get; set; }
    public List<SavedGroup>? LastGroups { get; set; }
    public int SessionCleared { get; private set; }

    public List<SavedGroup>? LoadSession() => Session;
    public void SaveSession(IReadOnlyList<SavedGroup> groups) => Session = [.. groups];

    public void ClearSession()
    {
        Session = null;
        SessionCleared++;
    }

    public List<SavedGroup>? LoadLastGroups() => LastGroups;
    public void SaveLastGroups(IReadOnlyList<SavedGroup> groups) => LastGroups = [.. groups];
}

internal sealed class MemorySettingsStore : ISettingsStore
{
    public Settings Settings { get; set; } = new();
    public int Saves { get; private set; }

    public Settings Load() => Settings;

    public void Save(Settings settings)
    {
        Settings = settings;
        Saves++;
    }
}

internal sealed class RecordingLauncher : IAppLauncher
{
    public List<string> Launched { get; } = [];
    public void Launch(string exePath) => Launched.Add(exePath);
}

internal sealed class FakeAutostart : IAutostart
{
    public Task<bool> IsEnabledAsync() => Task.FromResult(false);
    public Task<bool> SetAsync(bool enable) => Task.FromResult(enable);
}

internal sealed class NullLog : ILog
{
    public void Error(string context, Exception ex) { }
    public void Trace(string message) { }
}

internal sealed class FakePlatform : IPlatform
{
    public FakeWindowSystem Desktop { get; } = new();
    public FakeCatalog CatalogFake { get; } = new();
    public FakeInput InputFake { get; } = new();
    public FakeWindowEvents Events { get; } = new();
    public FakeKeyboard KeyboardFake { get; } = new();
    public MemoryLayoutStore LayoutStore { get; } = new();
    public MemorySessionStore SessionStore { get; } = new();
    public MemorySettingsStore SettingsStore { get; } = new();
    public RecordingLauncher LauncherFake { get; } = new();

    public IWindowSystem Windows => Desktop;
    public IWindowCatalog Catalog => CatalogFake;
    public IInputState Input => InputFake;
    public IWindowEventSource WindowEvents => Events;
    public IKeyboardSource Keyboard => KeyboardFake;
    public ILayoutStore Layouts => LayoutStore;
    public ISessionStore Sessions => SessionStore;
    public ISettingsStore Settings => SettingsStore;
    public IAppLauncher Launcher => LauncherFake;
    public IAutostart Autostart { get; } = new FakeAutostart();
    public ILog Log { get; } = new NullLog();
}
