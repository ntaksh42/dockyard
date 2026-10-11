namespace Dockyard.Application;

/// <summary>Starting Dockyard with Windows.</summary>
public interface IAutostart
{
    Task<bool> IsEnabledAsync();

    /// <summary>Returns what the setting is afterwards: Windows may refuse (a policy, or the user turned it off in Task Manager).</summary>
    Task<bool> SetAsync(bool enable);
}
