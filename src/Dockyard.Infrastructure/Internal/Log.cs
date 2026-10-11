using System.IO;

namespace Dockyard.Infrastructure.Internal;

/// <summary>Appends errors to %LOCALAPPDATA%\Dockyard\dockyard.log.</summary>
internal static class Log
{
    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dockyard", "dockyard.log");

#if DEBUG
    private static readonly bool Tracing = true;
#else
    private static readonly bool Tracing = Environment.GetEnvironmentVariable("DOCKYARD_TRACE") == "1";
#endif

    /// <summary>Diagnostic trace, written always in Debug builds and otherwise only when DOCKYARD_TRACE=1.</summary>
    public static void Trace(string message)
    {
        if (!Tracing) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(Path.ChangeExtension(FilePath, ".trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Error(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] {ex}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
