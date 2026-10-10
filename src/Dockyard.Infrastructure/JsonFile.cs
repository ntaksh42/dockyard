using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dockyard.Infrastructure;

/// <summary>JSON files next to the log in %LOCALAPPDATA%\Dockyard.</summary>
public static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Local files meant to be readable by hand: keep "+" in "Ctrl+Win+Left" as it is.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
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

    public static void Write<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name))!);
            File.WriteAllText(PathOf(name), JsonSerializer.Serialize(value, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Write {name}", ex);
        }
    }
}
