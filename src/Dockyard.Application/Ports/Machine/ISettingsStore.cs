namespace Dockyard.Application;

public interface ISettingsStore
{
    Settings Load();
    void Save(Settings settings);
}
