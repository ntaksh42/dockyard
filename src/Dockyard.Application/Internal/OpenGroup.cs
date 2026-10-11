
namespace Dockyard.Application.Internal;

/// <summary>An open group: its frame and the controller that places the member windows.</summary>
internal sealed class OpenGroup(IGroupFrame frame, GroupController controller)
{
    public IGroupFrame Frame { get; } = frame;
    public GroupController Controller { get; } = controller;
}
