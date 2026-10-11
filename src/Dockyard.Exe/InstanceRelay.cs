using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Dockyard.Application;

namespace Dockyard.Exe;

/// <summary>
/// Hands a layout name from a second Dockyard, started by a shortcut file, to the first one over a named pipe,
/// so that the second can end at once. The pipe is the user's own: nobody else can write to it.
/// </summary>
internal sealed class InstanceRelay : IDisposable
{
    /// <summary>The first copy may still be starting when a second one is launched right behind it.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();

    private static string PipeName
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return $"Dockyard.{Environment.UserName}.{process.SessionId}";
        }
    }

    /// <summary>Asks the running Dockyard to open the layout. Returns false if it cannot be reached.</summary>
    public static bool Send(string layoutName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeout);
            client.Write(Encoding.UTF8.GetBytes(layoutName));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    /// <summary>Starts listening; <paramref name="received"/> gets each layout name, on a background thread.</summary>
    public static InstanceRelay Listen(Action<string> received, ILog log)
    {
        var relay = new InstanceRelay();
        _ = relay.ListenAsync(received, log);
        return relay;
    }

    public void Dispose() => _stop.Cancel();

    private async Task ListenAsync(Action<string> received, ILog log)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var name = await reader.ReadToEndAsync(_stop.Token);
                if (name.Length > 0) received(name);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                // A client that went away mid-message: listen for the next one, but not at once in case it keeps failing.
                log.Error("Instance relay", ex);
                await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
            }
        }
    }
}
