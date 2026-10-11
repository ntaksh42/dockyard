using Dockyard.Application;
using Dockyard.Infrastructure.Internal.Logging;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Dockyard.Infrastructure.Internal.Launching;

/// <summary>
/// Starting Dockyard with Windows. The MSIX package declares a startup task (installer/AppxManifest.xml),
/// which the user can also turn off in Task Manager; run from the build output, the Run key is used instead.
/// </summary>
internal sealed class Autostart : IAutostart
{
    private const string TaskId = "DockyardStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public async Task<bool> IsEnabledAsync()
    {
        try
        {
            if (!AppPackage.IsPackaged)
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("Dockyard") is not null;
            }
            var task = await StartupTask.GetAsync(TaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart", ex);
            return false;
        }
    }

    /// <summary>Turns starting with Windows on or off. Returns whether it is on afterwards (the user or a policy can block it).</summary>
    public async Task<bool> SetAsync(bool enable)
    {
        try
        {
            if (!AppPackage.IsPackaged)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enable) key.SetValue("Dockyard", $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue("Dockyard", throwOnMissingValue: false);
                return enable;
            }
            var task = await StartupTask.GetAsync(TaskId);
            if (!enable) task.Disable();
            else if (task.State == StartupTaskState.Disabled) await task.RequestEnableAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Autostart", ex);
        }
        return await IsEnabledAsync();
    }
}
