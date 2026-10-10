using Dockyard.Domain;

namespace Dockyard.Infrastructure;

/// <summary>User preferences, kept in settings.json.</summary>
public sealed class Settings
{
    private const string FileName = "settings.json";

    public TabBarPlacement TabBarPlacement { get; set; }

    public static Settings Load() => JsonFile.Read<Settings>(FileName) ?? new();

    public void Save() => JsonFile.Write(FileName, this);
}
