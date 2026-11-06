namespace Dockyard.Application;

/// <summary>
/// A computer the user connects to with Remote Desktop, saved in the settings. <see cref="Name"/> tells the
/// connections apart and is what a saved tab refers to. The password is kept by the machine, never here.
/// </summary>
public sealed record RemoteDesktopConnection(string Name, string Host, int Port = RemoteDesktopConnection.DefaultPort,
    string? UserName = null, string? Domain = null)
{
    public const int DefaultPort = 3389;
}
