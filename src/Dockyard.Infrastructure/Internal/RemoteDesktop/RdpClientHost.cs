using System.Runtime.InteropServices;
using System.Windows.Forms;
using Dockyard.Application;
using Dockyard.Infrastructure.Internal.Logging;

namespace Dockyard.Infrastructure.Internal.RemoteDesktop;

/// <summary>
/// The Remote Desktop ActiveX control (mstscax.dll, the one mstsc uses) hosted as a WinForms control. It is
/// driven through IDispatch with <c>dynamic</c>, so no interop assembly has to be generated for it.
/// </summary>
internal sealed class RdpClientHost : AxHost
{
    // Newest first; the first one this PC can create wins. Their ProgIDs are not always registered.
    private static readonly string[] Clsids =
    [
        "3F859AA3-C2D4-4FAA-B0E4-FD0C9C4E5E3A", // MsRdpClient13NotSafeForScripting
        "1DF7C823-B2D4-4B54-975A-F2AC5D7CF8B8", // 12
        "A0C63C30-F08D-4AB4-907C-34905D770C7D", // 11
        "8B918B82-7985-4C24-89DF-C33AD2BBFBCD", // 10
        "A3BC03A0-041D-42E3-AD22-882B7865C9C5", // 9
    ];

    // The control's event interface (IMsTscAxEvents) and the DISPID of OnDisconnected.
    private static readonly Guid EventsInterfaceId = new("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6");
    private const int DispidOnDisconnected = 4;

    private const int ConnectedState = 1;
    private const int MinRemoteSize = 200;
    private const int MaxRemoteSize = 8192;

    private static string? s_clsid;

    private delegate void DisconnectedHandler(int reason);
    // Kept so the delegate registered with the connection point is not collected.
    private DisconnectedHandler? _disconnectedSink;
    private dynamic? _ocx;

    public RdpClientHost() : base(ResolveClsid())
    {
    }

    /// <summary>The session ended, with a description of why. Raised on the control's thread.</summary>
    public event Action<string>? Disconnected;

    public bool IsConnected
    {
        get
        {
            try { return _ocx is { } o && (int)o.Connected == ConnectedState; }
            catch (COMException) { return false; }
        }
    }

    /// <summary>Whether the control can be created on this PC at all.</summary>
    public static bool IsAvailable => ResolveClsid().Length > 0;

    private static string ResolveClsid()
    {
        if (s_clsid is not null) return s_clsid;
        foreach (var clsid in Clsids)
        {
            try
            {
                if (Type.GetTypeFromCLSID(new Guid(clsid)) is not { } type || Activator.CreateInstance(type) is not { } probe) continue;
                Marshal.FinalReleaseComObject(probe);
                return s_clsid = clsid;
            }
            catch (COMException)
            {
                // This version is registered but cannot be created here: try an older one.
            }
        }
        return s_clsid = "";
    }

    private dynamic Client()
    {
        if (!IsHandleCreated) CreateControl();
        return _ocx ??= GetOcx() ?? throw new InvalidOperationException("The Remote Desktop control did not start.");
    }

    /// <summary>Connects with a remote screen of the given size, in pixels.</summary>
    public void Connect(RemoteDesktopConnection connection, string? password, System.Drawing.Size size)
    {
        var ocx = Client();
        ocx.Server = connection.Host;
        if (!string.IsNullOrEmpty(connection.UserName)) ocx.UserName = connection.UserName;
        if (!string.IsNullOrEmpty(connection.Domain)) ocx.Domain = connection.Domain;
        var (width, height) = RemoteSize(size);
        ocx.DesktopWidth = width;
        ocx.DesktopHeight = height;
        TrySet(() => ocx.ColorDepth = 32, "ColorDepth");

        var advanced = ocx.AdvancedSettings9;
        if (connection.Port != RemoteDesktopConnection.DefaultPort) advanced.RDPPort = connection.Port;
        if (!string.IsNullOrEmpty(password)) advanced.ClearTextPassword = password;
        TrySet(() => advanced.EnableCredSspSupport = true, "EnableCredSspSupport");
        // Scales the picture when the server cannot follow a resize of the pane.
        TrySet(() => advanced.SmartSizing = true, "SmartSizing");
        TrySet(() => advanced.DisplayConnectionBar = false, "DisplayConnectionBar");
        TrySet(() => advanced.RedirectClipboard = true, "RedirectClipboard");
        // Warn about a server whose identity cannot be verified, and let the user decide.
        TrySet(() => advanced.AuthenticationLevel = 2u, "AuthenticationLevel");
        // Windows key combinations go to the remote computer, as in a full-screen mstsc.
        TrySet(() => ocx.SecuredSettings3.KeyboardHookMode = 1, "KeyboardHookMode");
        AttachDisconnectedSink((object)ocx);
        ocx.Connect();
    }

    /// <summary>Changes the remote resolution to the pane's size (servers from RDP 8.1 on); older ones keep scaling.</summary>
    public void ResizeRemote(System.Drawing.Size size)
    {
        if (!IsConnected) return;
        var (width, height) = RemoteSize(size);
        var scale = DeviceDpi * 100 / 96;
        try
        {
            _ocx!.UpdateSessionDisplaySettings((uint)width, (uint)height, (uint)width, (uint)height, 0u, DesktopScale(scale), DeviceScale(scale));
        }
        catch (COMException ex)
        {
            Log.Trace($"remote desktop resolution unchanged: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        try
        {
            if (_ocx is { } o && (int)o.Connected != 0) o.Disconnect();
        }
        catch (COMException ex)
        {
            Log.Trace($"remote desktop disconnect: {ex.Message}");
        }
    }

    private void AttachDisconnectedSink(object ocx)
    {
        if (_disconnectedSink is not null) return;
        DisconnectedHandler sink = reason => Disconnected?.Invoke(Describe(reason));
        try
        {
            ComEventsHelper.Combine(ocx, EventsInterfaceId, DispidOnDisconnected, sink);
            _disconnectedSink = sink;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // The session still works; the window just cannot tell that it ended.
            Log.Error("Remote desktop events", ex);
        }
    }

    private string Describe(int reason)
    {
        try
        {
            return (string)_ocx!.GetErrorDescription((uint)reason, (uint)(int)_ocx.ExtendedDisconnectReason);
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return $"Disconnected (reason {reason}).";
        }
    }

    private static (int Width, int Height) RemoteSize(System.Drawing.Size size) =>
        // The server wants an even width.
        (Math.Clamp(size.Width & ~1, MinRemoteSize, MaxRemoteSize), Math.Clamp(size.Height & ~1, MinRemoteSize, MaxRemoteSize));

    /// <summary>RDP accepts only these steps for the desktop scale.</summary>
    private static uint DesktopScale(int percent) =>
        (uint)new[] { 500, 450, 400, 350, 300, 250, 225, 200, 175, 150, 125 }.FirstOrDefault(step => percent >= step, 100);

    /// <summary>And only these for the device scale.</summary>
    private static uint DeviceScale(int percent) => percent >= 180 ? 180u : percent >= 140 ? 140u : 100u;

    /// <summary>Settings older controls lack: the session works without them.</summary>
    private static void TrySet(Action set, string name)
    {
        try { set(); }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Trace($"remote desktop setting {name} not applied: {ex.Message}");
        }
    }
}
