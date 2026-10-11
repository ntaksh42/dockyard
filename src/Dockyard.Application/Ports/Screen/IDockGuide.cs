using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>Where a dragged window would land, drawn over the frame it is above.</summary>
public interface IDockGuide
{
    void Show(IGroupFrame group, DockTarget drop);
    void Hide();
}
