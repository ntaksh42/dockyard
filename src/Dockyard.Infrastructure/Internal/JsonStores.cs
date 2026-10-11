using Dockyard.Application;

namespace Dockyard.Infrastructure.Internal;

/// <summary>Saved layouts in layouts.json.</summary>
internal sealed class JsonLayoutStore : ILayoutStore
{
    private const string FileName = "layouts.json";

    public List<SavedGroup> Load() => JsonFile.Read<List<SavedGroup>>(FileName) ?? [];

    public void Save(IReadOnlyList<SavedGroup> layouts) => JsonFile.Write(FileName, layouts);
}

/// <summary>The open groups in session.json, and the groups of the last exit in last-groups.json.</summary>
internal sealed class JsonSessionStore : ISessionStore
{
    private const string SessionFile = "session.json";
    private const string LastGroupsFile = "last-groups.json";

    public List<SavedGroup>? LoadSession() => JsonFile.Read<List<SavedGroup>>(SessionFile);

    public void SaveSession(IReadOnlyList<SavedGroup> groups) => JsonFile.Write(SessionFile, groups);

    public void ClearSession() => JsonFile.Delete(SessionFile);

    public List<SavedGroup>? LoadLastGroups() => JsonFile.Read<List<SavedGroup>>(LastGroupsFile);

    public void SaveLastGroups(IReadOnlyList<SavedGroup> groups) => JsonFile.Write(LastGroupsFile, groups);
}

/// <summary>User preferences in settings.json.</summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";

    public Settings Load()
    {
        var settings = JsonFile.Read<Settings>(FileName) ?? new();
        // A number in the file reads as a placement that does not exist.
        if (!Enum.IsDefined(settings.TabBarPlacement)) settings.TabBarPlacement = default;
        return settings;
    }

    public void Save(Settings settings) => JsonFile.Write(FileName, settings);
}
