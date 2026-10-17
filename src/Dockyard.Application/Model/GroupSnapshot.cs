using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>What the frame has to draw.</summary>
/// <param name="Waiting">Restored tabs still waiting for their app's window, by pane (empty panes show them).</param>
/// <param name="Attention">Members that flashed for the user's attention and have not been looked at since.</param>
public sealed record GroupSnapshot(
    LayoutResult Layout, IReadOnlyDictionary<WindowHandle, string> Titles, string ActiveTitle, IReadOnlyDictionary<Pane, IReadOnlyList<SavedTab>> Waiting,
    IReadOnlySet<WindowHandle> Attention);
