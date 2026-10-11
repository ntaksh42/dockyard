using System.Text.Json;
using System.Text.Json.Serialization;
using Dockyard.Application;
using Dockyard.Domain;

namespace Dockyard.Infrastructure.Internal.Storage;

// The saved files keep writing the identifiers as the plain number or text they always were; these only turn
// them into their types on the way in.

/// <summary>A window handle as a number.</summary>
internal sealed class WindowHandleJsonConverter : JsonConverter<WindowHandle>
{
    public override WindowHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new((nint)reader.GetInt64());

    public override void Write(Utf8JsonWriter writer, WindowHandle value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)value.Value);
}

/// <summary>A layout name as text.</summary>
internal sealed class LayoutNameJsonConverter : JsonConverter<LayoutName>
{
    public override LayoutName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, LayoutName value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>A process name as text.</summary>
internal sealed class ProcessNameJsonConverter : JsonConverter<ProcessName>
{
    public override ProcessName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, ProcessName value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
