namespace Dockyard.Application;

/// <summary>Deferred work on the UI thread.</summary>
public interface IScheduler
{
    /// <summary>Runs <paramref name="action"/> after the current message has been processed.</summary>
    void Post(Action action);

    IRestartableTimer CreateTimer(TimeSpan delay, Action tick);
}
