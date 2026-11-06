using System.Windows;
using Dockyard.Application;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>Adds a Remote Desktop connection, or changes one.</summary>
internal partial class RemoteDesktopEditor : Window
{
    private readonly RemoteDesktopConnection? _current;
    private readonly IReadOnlyList<RemoteDesktopConnection> _all;

    /// <param name="current">The connection to change; null for a new one.</param>
    /// <param name="all">Every saved connection, whose names a new name must not take.</param>
    public RemoteDesktopEditor(RemoteDesktopConnection? current, IReadOnlyList<RemoteDesktopConnection> all)
    {
        InitializeComponent();
        _current = current;
        _all = all;
        NameBox.Text = current?.Name ?? "";
        HostBox.Text = current?.Host ?? "";
        PortBox.Text = (current?.Port ?? RemoteDesktopConnection.DefaultPort).ToString();
        UserBox.Text = current?.UserName ?? "";
        DomainBox.Text = current?.Domain ?? "";
        if (current is not null)
        {
            PasswordNote.Text = "Leave empty to keep the saved password, if there is one.";
            ForgetPasswordBox.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => (current is null ? NameBox : HostBox).Focus();
    }

    /// <summary>The connection as entered, once saved.</summary>
    public RemoteDesktopConnection? Result { get; private set; }

    /// <summary>The password to keep: null leaves the saved one as it is, empty forgets it.</summary>
    public string? Password { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        if (name.Length == 0 || host.Length == 0)
        {
            ShowError("Enter a name and the computer to connect to.");
            return;
        }
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowError("The port is a number from 1 to 65535.");
            return;
        }
        if (_all.Any(c => c.Name == name && c != _current))
        {
            ShowError($"There is already a connection named \"{name}\".");
            return;
        }
        Result = new RemoteDesktopConnection(name, host, port, Optional(UserBox.Text), Optional(DomainBox.Text));
        Password = PasswordBox.Password.Length > 0 ? PasswordBox.Password
            : ForgetPasswordBox.IsChecked == true || _current is null ? "" : null;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string? Optional(string text) => text.Trim() is { Length: > 0 } value ? value : null;
}
