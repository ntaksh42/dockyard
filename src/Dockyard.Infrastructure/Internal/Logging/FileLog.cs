using Dockyard.Application;

namespace Dockyard.Infrastructure.Internal.Logging;

/// <summary><see cref="ILog"/> that writes to %LOCALAPPDATA%\Dockyard\dockyard.log.</summary>
internal sealed class FileLog : ILog
{
    public void Error(string context, Exception ex) => Log.Error(context, ex);

    public void Trace(string message) => Log.Trace(message);
}
