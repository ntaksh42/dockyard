using Dockyard.Application;
using Dockyard.Infrastructure.Internal;

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

    public IWindowSystem Windows { get; } = new Win32WindowSystem();
    public IWindowCatalog Catalog { get; } = new Win32WindowCatalog();
    public IInputState Input { get; } = new Win32Input();
    public IWindowEventSource WindowEvents => _windowEvents;
    public IKeyboardSource Keyboard => _keyboard;
    public ILayoutStore Layouts { get; } = new JsonLayoutStore();
    public ISessionStore Sessions { get; } = new JsonSessionStore();
    public ISettingsStore Settings { get; } = new JsonSettingsStore();
    public IAppLauncher Launcher { get; } = new ProcessLauncher();
    public IAutostart Autostart { get; } = new Autostart();
    public ILog Log { get; } = new FileLog();

    public void Dispose()
    {
        _keyboard.Dispose();
        _windowEvents.Dispose();
    }
}
