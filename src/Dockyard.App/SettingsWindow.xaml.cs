using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dockyard.App.Controllers;
using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.App;

/// <summary>Preferences. Changes apply to every group right away.</summary>
internal partial class SettingsWindow : Window
{
    private readonly Action<TabBarPlacement> _placementChanged;
    private readonly Action<IReadOnlyDictionary<ShortcutAction, ShortcutKey>> _shortcutsChanged;
    private readonly Dictionary<ShortcutAction, ShortcutKey> _bindings;
    private readonly Dictionary<ShortcutAction, Button> _bindingButtons = [];
    /// <summary>The action waiting for its new keys, which the keyboard hook delivers (see <see cref="Capture"/>).</summary>
    private ShortcutAction? _capturing;

    private readonly Action<IReadOnlyList<string>> _titleBarAppsChanged;
    private string _titleBarAppsText;

    public SettingsWindow(TabBarPlacement placement, IReadOnlyDictionary<ShortcutAction, ShortcutKey> bindings,
        Action<TabBarPlacement> placementChanged, Action<IReadOnlyDictionary<ShortcutAction, ShortcutKey>> shortcutsChanged,
        IReadOnlyList<string> titleBarApps, Action<IReadOnlyList<string>> titleBarAppsChanged)
    {
        InitializeComponent();
        TitleBarAppsBox.Text = _titleBarAppsText = string.Join(", ", titleBarApps);
        _titleBarAppsChanged = titleBarAppsChanged;
        // Closing with the box focused does not always move the focus first.
        Closing += (_, _) => CommitTitleBarApps();
        ((RadioButton)FindName($"{placement}Option")).IsChecked = true;
        // Set after the initial check so opening the window does not report a change.
        _placementChanged = placementChanged;
        _shortcutsChanged = shortcutsChanged;
        _bindings = bindings.ToDictionary();
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
        _shortcutsChanged(_bindings);
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
            clear.Click += (_, _) =>
            {
                StopCapture();
                if (_bindings.Remove(action)) _shortcutsChanged(_bindings);
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

    private void Placement_Checked(object sender, RoutedEventArgs e) =>
        _placementChanged?.Invoke(Enum.Parse<TabBarPlacement>((string)((RadioButton)sender).Tag));

    private void TitleBarAppsBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitTitleBarApps();

    private void CommitTitleBarApps()
    {
        if (TitleBarAppsBox.Text == _titleBarAppsText) return;
        _titleBarAppsText = TitleBarAppsBox.Text;
        _titleBarAppsChanged(TitleBarAppsBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).ToList());
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _bindings.Clear();
        foreach (var (action, binding) in Shortcuts.Defaults) _bindings[action] = binding;
        StopCapture();
        _shortcutsChanged(_bindings);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
