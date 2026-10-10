using System.Windows;
using System.Windows.Input;
using Dockyard.App.Controllers;
using Dockyard.Infrastructure;

namespace Dockyard.App;

/// <summary>Lists the saved layouts to open, rename or delete them. Changes go to the file right away.</summary>
internal partial class LayoutsWindow : Window
{
    private readonly string _file;
    private readonly Action<SavedGroup> _open;
    private List<SavedGroup> _layouts = [];

    public LayoutsWindow(string file, Action<SavedGroup> open)
    {
        InitializeComponent();
        _file = file;
        _open = open;
        // Re-read on activation: a layout may have been saved from a group meanwhile.
        Activated += (_, _) => Reload((List.SelectedItem as SavedGroup)?.Name);
    }

    private SavedGroup? Selected => List.SelectedItem as SavedGroup;

    private void Reload(string? select)
    {
        _layouts = JsonFile.Read<List<SavedGroup>>(_file) ?? [];
        var sorted = _layouts.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        // The same names as shown: keep the list as it is, so activating the window does not reset its items.
        if (List.ItemsSource is IEnumerable<SavedGroup> shown && shown.Select(l => l.Name).SequenceEqual(sorted.Select(l => l.Name)))
        {
            UpdateActions();
            return;
        }
        List.ItemsSource = sorted;
        List.SelectedItem = ((IEnumerable<SavedGroup>)List.ItemsSource).FirstOrDefault(l => l.Name == select)
            ?? ((IEnumerable<SavedGroup>)List.ItemsSource).FirstOrDefault();
        // With nothing to select the selection does not change: update here too.
        UpdateActions();
    }

    private void UpdateActions()
    {
        OpenButton.IsEnabled = RenameButton.IsEnabled = DeleteButton.IsEnabled = NameBox.IsEnabled = Selected is not null;
        EmptyText.Visibility = _layouts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        NameBox.Text = Selected?.Name ?? "";
        Message.Visibility = Visibility.Collapsed;
        UpdateActions();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        // By name from the file just read: the shown item may be older than a layout saved over it since.
        if (Selected is { } layout && _layouts.Find(l => l.Name == layout.Name) is { } current) _open(current);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } layout) return;
        if (!LayoutLibrary.Rename(_layouts, layout.Name, NameBox.Text))
        {
            Message.Text = NameBox.Text.Trim().Length == 0 ? "Enter a name." : $"A layout named \"{NameBox.Text.Trim()}\" already exists.";
            Message.Visibility = Visibility.Visible;
            return;
        }
        JsonFile.Write(_file, _layouts);
        Reload(NameBox.Text.Trim());
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Rename_Click(sender, e);
        e.Handled = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } layout) return;
        if (MessageBox.Show(this, $"Delete the layout \"{layout.Name}\"?", "Dockyard", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _layouts.RemoveAll(l => l.Name == layout.Name);
        JsonFile.Write(_file, _layouts);
        Reload(null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
