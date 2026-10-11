namespace Dockyard.Application;

/// <summary>
/// The name of an app's process, its exe name without ".exe" (e.g. "notepad"). Windows does not tell
/// "Notepad" and "notepad" apart, so neither does this.
/// </summary>
public sealed record ProcessName(string Value)
{
    public bool Equals(ProcessName? other) => other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
