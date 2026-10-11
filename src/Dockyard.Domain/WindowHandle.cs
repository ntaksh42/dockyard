namespace Dockyard.Domain;

/// <summary>
/// A top-level window's handle (HWND). To Windows it is a plain number; a type of its own keeps it from being
/// mixed up with the other numbers around it (sizes, indexes, key codes).
/// </summary>
public readonly record struct WindowHandle(nint Value) : IFormattable
{
    /// <summary>No window: what the system reports when there is none.</summary>
    public static WindowHandle None => default;

    public bool IsNone => Value == 0;

    public override string ToString() => Value.ToString();

    /// <summary>So that a trace can write a handle in hex: <c>$"{window:X}"</c>.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null) => Value.ToString(format, formatProvider);
}
