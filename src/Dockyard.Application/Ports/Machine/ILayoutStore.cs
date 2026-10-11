namespace Dockyard.Application;

/// <summary>Saved layouts: the groups the user chose to keep, by name.</summary>
public interface ILayoutStore
{
    List<SavedGroup> Load();
    void Save(IReadOnlyList<SavedGroup> layouts);
}
