using Dockyard.Application;
using static Dockyard.Infrastructure.Internal.Native;

namespace Dockyard.Infrastructure.Internal;

/// <summary><see cref="IInputState"/> on top of Win32.</summary>
internal sealed class Win32Input : IInputState
{
    private const int VK_LBUTTON = 0x01, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B;

    public bool IsLeftButtonDown => IsDown(VK_LBUTTON);
    public bool IsShiftDown => IsDown(VK_SHIFT);
    public bool IsEscapeDown => IsDown(VK_ESCAPE);

    public bool TryGetCursor(out int x, out int y)
    {
        var ok = GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return ok;
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
}
