namespace Dockyard.Application;

/// <summary>Optional diagnostic trace. The host connects it to a log; it does nothing by default.</summary>
public static class Diagnostics
{
    public static Action<string>? Trace { get; set; }
}
