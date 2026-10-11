using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>What a frame asks the workspace for while it is open.</summary>
public interface IFrameContext
{
    /// <summary>Whether a window already belongs to some group, so the picker can leave it out.</summary>
    bool IsGrouped(WindowHandle window);

    /// <summary>The saved layouts, read when a menu offering them opens.</summary>
    IReadOnlyList<SavedGroup> SavedLayouts();

    /// <summary>The current shortcut bindings, listed in the "+" menu.</summary>
    IReadOnlyDictionary<ShortcutAction, ShortcutKey> ShortcutBindings();
}
