using Dockyard.Application;
using Dockyard.Infrastructure.Internal.Launching;
using Dockyard.Infrastructure.Internal.Logging;
using Dockyard.Infrastructure.Internal.Storage;
using Dockyard.Infrastructure.Internal.Win32;

namespace Dockyard.Infrastructure;

/// <summary>
/// The only public entry point of this assembly: the Windows implementation of every port the application
/// needs. Everything else here is internal. Creating it installs the window event and keyboard hooks; disposing
/// it removes them.
/// </summary>
public sealed class PlatformServices : IPlatform, IDisposable
{
    private readonly WinEventHook _windowEvents = new();
    private readonly KeyboardHook _keyboard = new();

    public PlatformServices()
    {
        _windowEvents.ForegroundChecked += _keyboard.StayAheadOf;
    }

    public IWindowSystem Windows { get; } = new Win32WindowSystem();
    public IWindowCatalog Catalog { get; } = new Win32WindowCatalog();
    public IInputState Input { get; } = new Win32Input();
    public IWindowEventSource WindowEvents => _windowEvents;
    public IKeyboardSource Keyboard => _keyboard;
    public ILayoutStore Layouts { get; } = new JsonLayoutStore();
    public ISessionStore Sessions { get; } = new JsonSessionStore();
    public ISettingsStore Settings { get; } = new JsonSettingsStore();
    public IAppLauncher Launcher { get; } = new ProcessLauncher();
    public ILayoutShortcuts LayoutShortcuts { get; } = new LayoutShortcuts();
    public IAutostart Autostart { get; } = new Autostart();
    public ILog Log { get; } = new FileLog();

    public void Dispose()
    {
        _keyboard.Dispose();
        _windowEvents.Dispose();
        // Hooks are gone: give responsive apps a short, shared exit budget to restore their captions. A hung
        // app cannot hold shutdown indefinitely; its recovery marker is retained for the next start.
        if (!((Win32WindowSystem)Windows).RestoreTitleBarsAsync().Wait(TimeSpan.FromMilliseconds(250)))
        {
            Log.Trace("Title bar restoration still pending at exit");
        }
    }
}
