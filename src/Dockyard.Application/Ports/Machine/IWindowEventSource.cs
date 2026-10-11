using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>Events of other processes' top-level windows. Raised on the UI thread.</summary>
public interface IWindowEventSource
{
    event Action<WindowEvent, WindowHandle>? Received;

    /// <summary>The left button went down over a window while Shift was held; (<see cref="WindowHandle.None"/>, default) when Shift was not held.</summary>
    event Action<WindowHandle, PixelRect>? ShiftMouseDown;

    /// <summary>Safety net for foreground changes Windows does not report: call it periodically.</summary>
    void CheckForeground();
}
