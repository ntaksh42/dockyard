namespace Dockyard.Application;

/// <summary>An app window the user could add to a group.</summary>
/// <param name="Controllable">False for windows Windows will not let this process move, such as elevated apps.</param>
public sealed record WindowInfo(nint Handle, string Title, string ProcessName, bool Controllable)
{
    public override string ToString() =>
        Controllable ? $"{Title}  —  {ProcessName}" : $"{Title}  —  {ProcessName}  (can't add apps running as administrator)";
}
