using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dockyard.Application;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>
/// Picks the computer to open in a pane with Remote Desktop, and keeps the list of connections: they are added,
/// changed and deleted here.
/// </summary>
internal partial class RemoteDesktopPicker : Window
{
    private readonly IFrameContext _context;

    public RemoteDesktopPicker(IFrameContext context)
    {
        InitializeComponent();
        _context = context;
        ShowConnections(null);
        Loaded += (_, _) => FocusList();
    }

    public RemoteDesktopConnection? Selected => List.SelectedItem as RemoteDesktopConnection;

    private void ShowConnections(string? select)
    {
        var connections = _context.RemoteDesktops().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        List.ItemsSource = connections;
        List.DisplayMemberPath = nameof(RemoteDesktopConnection.Name);
        List.SelectedItem = connections.FirstOrDefault(c => c.Name == select) ?? connections.FirstOrDefault();
        EmptyText.Visibility = connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var any = Selected is not null;
        ConnectButton.IsEnabled = EditButton.IsEnabled = DeleteButton.IsEnabled = any;
    }

    /// <summary>Puts the keyboard focus on the selected row, so the arrows move through the list.</summary>
    private void FocusList()
    {
        if (List.SelectedItem is { } item && List.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row) row.Focus();
        else List.Focus();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Selected is not null) Edit();
        else if (e.Key == Key.Delete && Selected is not null) Delete();
        else return;
        e.Handled = true;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not null) DialogResult = true;
    }

    private void New_Click(object sender, RoutedEventArgs e) => Edit(isNew: true);

    private void Edit_Click(object sender, RoutedEventArgs e) => Edit();

    private void Delete_Click(object sender, RoutedEventArgs e) => Delete();

    private void Edit(bool isNew = false)
    {
        var current = isNew ? null : Selected;
        var editor = new RemoteDesktopEditor(current, _context.RemoteDesktops()) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is { } connection)
        {
            _context.SaveRemoteDesktop(connection, current?.Name, editor.Password);
            ShowConnections(connection.Name);
        }
        FocusList();
    }

    private void Delete()
    {
        if (Selected is not { } connection) return;
        var answer = MessageBox.Show(this, $"Delete the connection \"{connection.Name}\" and its saved password?", "Remote Desktop",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _context.DeleteRemoteDesktop(connection.Name);
        ShowConnections(null);
        FocusList();
    }
}
