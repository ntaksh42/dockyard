using System.Windows;
using Dockyard.App.Interop;

namespace Dockyard.App;

internal partial class WindowPicker : Window
{
    public WindowPicker()
    {
        InitializeComponent();
        var candidates = WindowList.GetCandidates().Where(w => !App.IsGrouped(w.Handle)).ToList();
        List.ItemsSource = candidates;
        List.SelectedItem = candidates.FirstOrDefault(w => w.Controllable);
    }

    public WindowInfo? Selected => List.SelectedItem as WindowInfo;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Controllable: true }) DialogResult = true;
    }
}
