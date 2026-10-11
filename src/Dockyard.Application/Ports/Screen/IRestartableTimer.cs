namespace Dockyard.Application;

/// <summary>A one-shot timer; restarting it pushes the tick back.</summary>
public interface IRestartableTimer
{
    void Restart();
    void Stop();
}
