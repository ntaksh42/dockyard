using System.Diagnostics;
using System.IO;
using Dockyard.Application;

namespace Dockyard.Infrastructure.Internal;

/// <summary><see cref="IAppLauncher"/> that starts the exe like a double-click would.</summary>
internal sealed class ProcessLauncher : IAppLauncher
{
    public void Launch(string exePath)
    {
        if (!File.Exists(exePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Launch {exePath}", ex);
        }
    }
}
