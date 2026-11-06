using System.Drawing;
using System.Windows.Forms;
using Dockyard.Application;

namespace Dockyard.Infrastructure.Internal.RemoteDesktop;

/// <summary>
/// A Remote Desktop session in a window without a title bar or border, for a group to place in a pane. It
/// connects once the group has placed it, with a remote screen of the pane's size, and follows the pane's size
/// after that. A session that ends leaves a message and a Reconnect button in the pane.
/// </summary>
internal sealed class RemoteDesktopWindow : Form
{
    /// <summary>The pane's size is applied once it stops changing for this long: each change is a round trip to the server.</summary>
    private const int ResizeSettleMs = 500;

    private static readonly Rectangle InitialBounds = new(100, 100, 1280, 800);

    private readonly RdpClientHost _client = new() { Dock = DockStyle.Fill };
    private readonly Panel _ended = new() { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(32, 32, 32) };
    private readonly Label _endedText = new() { Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _reconnect = new() { Text = "&Reconnect", Dock = DockStyle.Bottom, Height = 40, FlatStyle = FlatStyle.System };
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = ResizeSettleMs };
    private readonly string? _password;
    private bool _started;
    private bool _closing;

    public RemoteDesktopWindow(RemoteDesktopConnection connection, string? password)
    {
        Connection = connection;
        _password = password;
        Text = connection.Name;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = InitialBounds;
        BackColor = Color.Black;

        _ended.Controls.Add(_endedText);
        _ended.Controls.Add(_reconnect);
        _reconnect.Click += (_, _) => Reconnect();
        Controls.Add(_ended);
        Controls.Add(_client);

        _settle.Tick += (_, _) => ApplySize();
        Resize += (_, _) =>
        {
            _settle.Stop();
            _settle.Start();
        };
        _client.Disconnected += ShowEnded;
        // The keys typed into the pane are for the remote computer.
        Activated += (_, _) => (_ended.Visible ? (Control)_reconnect : _client).Focus();
        FormClosing += (_, _) =>
        {
            _closing = true;
            _settle.Stop();
            _client.Disconnect();
        };
    }

    public RemoteDesktopConnection Connection { get; }

    /// <summary>Shown like an app window, so it is listed by Alt+Tab while it sits behind its group's frame.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x00040000; // WS_EX_APPWINDOW
            return p;
        }
    }

    private void ApplySize()
    {
        _settle.Stop();
        if (_closing || _ended.Visible) return;
        if (_started)
        {
            _client.ResizeRemote(ClientSize);
            return;
        }
        // The first size that holds still is the pane's: connect with it.
        _started = true;
        _client.Connect(Connection, _password, ClientSize);
    }

    private void ShowEnded(string reason)
    {
        if (_closing) return;
        _endedText.Text = $"{Connection.Name} ({Connection.Host}): {reason}";
        Text = $"{Connection.Name} (disconnected)";
        _client.Visible = false;
        _ended.Visible = true;
        if (ContainsFocus) _reconnect.Focus();
    }

    private void Reconnect()
    {
        _ended.Visible = false;
        _client.Visible = true;
        Text = Connection.Name;
        _client.Connect(Connection, _password, ClientSize);
        _client.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _settle.Dispose();
        base.Dispose(disposing);
    }
}
