namespace Dockyard.Application;

/// <summary>The mouse and keyboard as they are right now.</summary>
public interface IInputState
{
    bool IsLeftButtonDown { get; }
    bool IsShiftDown { get; }
    bool IsEscapeDown { get; }
    bool TryGetCursor(out int x, out int y);
}
