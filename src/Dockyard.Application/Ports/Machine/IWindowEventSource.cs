namespace Dockyard.Application;

/// <summary>Events of other processes' top-level windows. Raised on the UI thread.</summary>
public interface IWindowEventSource
{
    event Action<WindowEvent, nint>? Received;

    /// <summary>The left button went down over a window while Shift was held; (0, default) when Shift was not held.</summary>
    event Action<nint, PixelRect>? ShiftMouseDown;

    /// <summary>Safety net for foreground changes Windows does not report: call it periodically.</summary>
    void CheckForeground();
}
