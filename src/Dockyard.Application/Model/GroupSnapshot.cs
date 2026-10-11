using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>What the frame has to draw.</summary>
/// <param name="Waiting">Restored tabs still waiting for their app's window, by pane (empty panes show them).</param>
public sealed record GroupSnapshot(
    LayoutResult Layout, IReadOnlyDictionary<nint, string> Titles, string ActiveTitle, IReadOnlyDictionary<Pane, IReadOnlyList<SavedTab>> Waiting);
