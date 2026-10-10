using System.Windows;
using Dockyard.Infrastructure;

namespace Dockyard.App;

internal partial class WindowPicker : Window
{
    /// <param name="isGrouped">Windows already in a group are left out.</param>
    public WindowPicker(Func<nint, bool> isGrouped)
    {
        InitializeComponent();
        var candidates = WindowCatalog.GetCandidates().Where(w => !isGrouped(w.Handle)).ToList();
        List.ItemsSource = candidates;
        List.SelectedItem = candidates.FirstOrDefault(w => w.Controllable);
    }

    public WindowInfo? Selected => List.SelectedItem as WindowInfo;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Controllable: true }) DialogResult = true;
    }
}
