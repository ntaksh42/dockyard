namespace Dockyard.Application;

public interface ILog
{
    void Error(string context, Exception ex);

    /// <summary>Diagnostic trace, written only when tracing is on.</summary>
    void Trace(string message);
}
