using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dockyard.Infrastructure.Internal.Logging;

namespace Dockyard.Infrastructure.Internal.Storage;

/// <summary>JSON files next to the log in %LOCALAPPDATA%\Dockyard.</summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Local files meant to be readable by hand: keep "+" in "Ctrl+Alt+Left" as it is.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new WindowHandleJsonConverter(),
            new LayoutNameJsonConverter(),
            new ProcessNameJsonConverter(),
        },
    };

    public static string PathOf(string name) => Path.Combine(Path.GetDirectoryName(Log.FilePath)!, name);

    /// <summary>The file's contents, or null when it is missing or unreadable.</summary>
    public static T? Read<T>(string name) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(PathOf(name)), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (ex is not FileNotFoundException and not DirectoryNotFoundException) Log.Error($"Read {name}", ex);
            return null;
        }
    }

    public static void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Delete {name}", ex);
        }
    }

    public static void Write<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name))!);
            // Written beside and then moved over the old file: a crash halfway leaves the previous contents, not
            // a truncated file (the session file is what restores the groups after a crash).
            var temporary = PathOf(name) + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, PathOf(name), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Write {name}", ex);
        }
    }
}
