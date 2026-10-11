using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dockyard.Application;
using Dockyard.Domain;

namespace Dockyard.Presentation.Internal;

/// <summary>Preferences. Changes apply to every group right away.</summary>
internal partial class SettingsWindow : Window
{
    private readonly Workspace _workspace;
    private readonly IAutostart _autostart;
    private readonly Dictionary<ShortcutAction, ShortcutKey> _bindings;
    private readonly Dictionary<ShortcutAction, Button> _bindingButtons = [];
    /// <summary>The action waiting for its new keys, which the keyboard hook delivers (see <see cref="Capture"/>).</summary>
    private ShortcutAction? _capturing;

    private string _titleBarAppsText;
    /// <summary>False while the initial state is set, so opening the window does not report a change.</summary>
    private bool _placementReported;

    public SettingsWindow(IAutostart autostart, Workspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        _autostart = autostart;
        var settings = workspace.Settings;
        LaunchMissingAppsBox.IsChecked = settings.LaunchMissingApps;
        OpenLastGroupsBox.IsChecked = settings.OpenLastGroups;
        // Checked/Unchecked rather than Click: toggling through UI Automation (screen readers) raises no Click.
        // Subscribed after the initial state is set, so showing it is not taken for a change.
        foreach (var box in new[] { LaunchMissingAppsBox, OpenLastGroupsBox })
        {
            box.Checked += Options_Changed;
            box.Unchecked += Options_Changed;
        }
        Loaded += async (_, _) =>
        {
            AutostartBox.IsChecked = await _autostart.IsEnabledAsync();
            AutostartBox.IsEnabled = true;
            AutostartBox.Checked += Autostart_Changed;
            AutostartBox.Unchecked += Autostart_Changed;
        };
        TitleBarAppsBox.Text = _titleBarAppsText = string.Join(", ", settings.TitleBarApps ?? Settings.DefaultTitleBarApps);
        // Closing with the box focused does not always move the focus first.
        Closing += (_, _) => CommitTitleBarApps();
        ((RadioButton)FindName($"{settings.TabBarPlacement}Option")).IsChecked = true;
        // Set after the initial check so opening the window does not report a change.
        _placementReported = true;
        _bindings = workspace.ShortcutBindings.ToDictionary();
        BuildShortcutRows();
        Deactivated += (_, _) => StopCapture();
        PreviewKeyDown += (_, e) =>
        {
            if (_capturing is null) return;
            e.Handled = true;
            // Shortcut keys come from the keyboard hook (see IsCapturing); here only Esc, which cancels.
            if (e.Key == Key.Escape) StopCapture();
        };
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// True while a shortcut waits for its keys. The keyboard hook then sends key presses here instead: many
    /// shortcuts (anything with Win) never reach a WPF window.
    /// </summary>
    public bool IsCapturing => _capturing is not null;

    public void Capture(ShortcutKey binding)
    {
        if (_capturing is not { } action) return;
        // A key combination does one thing: taking it for this action unbinds whatever had it.
        foreach (var other in _bindings.Where(b => b.Value == binding && b.Key != action).Select(b => b.Key).ToList())
            _bindings.Remove(other);
        _bindings[action] = binding;
        StopCapture();
        _workspace.SetShortcuts(_bindings);
    }

    private void BuildShortcutRows()
    {
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            var row = ShortcutGrid.RowDefinitions.Count;
            ShortcutGrid.RowDefinitions.Add(new RowDefinition());
            var label = new TextBlock { Text = Shortcuts.Names[action], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4) };
            var binding = new Button { Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2), HorizontalContentAlignment = HorizontalAlignment.Left };
            binding.Click += (_, _) =>
            {
                StopCapture();
                _capturing = action;
                binding.Content = "Press keys…";
            };
            var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(8, 2, 8, 2) };
            System.Windows.Automation.AutomationProperties.SetName(clear, $"Clear {Shortcuts.Names[action]}");
            clear.Click += (_, _) =>
            {
                StopCapture();
                if (_bindings.Remove(action)) _workspace.SetShortcuts(_bindings);
                ShowBindings();
            };
            Grid.SetRow(label, row);
            Grid.SetRow(binding, row);
            Grid.SetRow(clear, row);
            Grid.SetColumn(binding, 1);
            Grid.SetColumn(clear, 2);
            ShortcutGrid.Children.Add(label);
            ShortcutGrid.Children.Add(binding);
            ShortcutGrid.Children.Add(clear);
            _bindingButtons[action] = binding;
        }
        ShowBindings();
    }

    private void ShowBindings()
    {
        foreach (var (action, button) in _bindingButtons)
            button.Content = _bindings.TryGetValue(action, out var b) ? b.ToString() : "(none)";
    }

    private void StopCapture()
    {
        _capturing = null;
        ShowBindings();
    }

    private void Placement_Checked(object sender, RoutedEventArgs e)
    {
        if (_placementReported) _workspace.SetTabBarPlacement(Enum.Parse<TabBarPlacement>((string)((RadioButton)sender).Tag));
    }

    private void TitleBarAppsBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitTitleBarApps();

    private void CommitTitleBarApps()
    {
        if (TitleBarAppsBox.Text == _titleBarAppsText) return;
        _titleBarAppsText = TitleBarAppsBox.Text;
        _workspace.SetTitleBarApps(TitleBarAppsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).ToList());
    }

    private async void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (!AutostartBox.IsEnabled) return; // our own update below
        AutostartBox.IsEnabled = false;
        // Windows may refuse (the user turned it off in Task Manager, or a policy): show what it is now.
        AutostartBox.IsChecked = await _autostart.SetAsync(AutostartBox.IsChecked == true);
        AutostartBox.IsEnabled = true;
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        _workspace.Settings.LaunchMissingApps = LaunchMissingAppsBox.IsChecked == true;
        _workspace.Settings.OpenLastGroups = OpenLastGroupsBox.IsChecked == true;
        _workspace.SaveSettings();
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _bindings.Clear();
        foreach (var (action, binding) in Shortcuts.Defaults) _bindings[action] = binding;
        StopCapture();
        _workspace.SetShortcuts(_bindings);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
