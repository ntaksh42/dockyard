using System.Windows;
using System.Windows.Controls;
using Dockyard.Domain;

namespace Dockyard.App;

/// <summary>Preferences. Changes apply to every group right away.</summary>
internal partial class SettingsWindow : Window
{
    private readonly Action<TabBarPlacement> _changed;

    public SettingsWindow(TabBarPlacement placement, Action<TabBarPlacement> changed)
    {
        InitializeComponent();
        ((RadioButton)FindName($"{placement}Option")).IsChecked = true;
        // Set after the initial check so opening the window does not report a change.
        _changed = changed;
    }

    private void Placement_Checked(object sender, RoutedEventArgs e) =>
        _changed?.Invoke(Enum.Parse<TabBarPlacement>((string)((RadioButton)sender).Tag));

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
