namespace Dockyard.Application;

/// <summary>Starts an app that a layout needs and that is not running.</summary>
public interface IAppLauncher
{
    void Launch(string exePath);
}
