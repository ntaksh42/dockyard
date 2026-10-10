using System.Windows;
using Dockyard.Interop;

namespace Dockyard;

internal partial class WindowPicker : Window
{
    public WindowPicker()
    {
        InitializeComponent();
        List.ItemsSource = WindowList.GetCandidates().Where(w => !App.IsGrouped(w.Handle)).ToList();
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    public WindowInfo? Selected => List.SelectedItem as WindowInfo;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not null) DialogResult = true;
    }
}
