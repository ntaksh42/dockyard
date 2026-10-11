using System.Windows;
using Dockyard.Application;

namespace Dockyard.Presentation.Internal;

internal partial class WindowPicker : Window
{
    /// <param name="isGrouped">Windows already in a group are left out.</param>
    public WindowPicker(Func<nint, bool> isGrouped, IWindowCatalog catalog)
    {
        InitializeComponent();
        var candidates = catalog.GetCandidates().Where(w => !isGrouped(w.Handle)).ToList();
        List.ItemsSource = candidates;
        List.SelectedItem = candidates.FirstOrDefault(w => w.Controllable);
        EmptyText.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = Selected is { Controllable: true };
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = Selected is { Controllable: true };

    public WindowInfo? Selected => List.SelectedItem as WindowInfo;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Controllable: true }) DialogResult = true;
    }
}
