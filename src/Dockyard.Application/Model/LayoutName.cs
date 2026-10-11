namespace Dockyard.Application;

/// <summary>
/// The name of a group. A group's layout is saved under it, so it also tells the saved layouts apart: it is what
/// a hotkey, a shortcut file and the layouts list refer to.
/// </summary>
public sealed record LayoutName(string Value)
{
    public override string ToString() => Value;
}
