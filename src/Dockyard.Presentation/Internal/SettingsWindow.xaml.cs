using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dockyard.Application;
using Dockyard.Domain;

namespace Dockyard.Presentation.Internal;

/// <summary>Preferences and the saved layouts. Changes apply to every group right away.</summary>
internal partial class SettingsWindow : Window
{
    /// <summary>What waits for the keys the keyboard hook delivers (see <see cref="Capture"/>).</summary>
    private abstract record CaptureTarget;
    private sealed record ActionCapture(ShortcutAction Action) : CaptureTarget;
    private sealed record LayoutCapture(string Layout) : CaptureTarget;

    private const double HotkeyButtonWidth = 190;
    private static readonly Thickness RowButtonPadding = new(8, 3, 8, 3);
    private static readonly Thickness RowButtonMargin = new(0, 3, 4, 3);

    private readonly Workspace _workspace;
    private readonly IAutostart _autostart;
    private readonly ILayoutStore _layoutStore;
    private readonly Dictionary<ShortcutAction, ShortcutKey> _bindings;
    private readonly Dictionary<ShortcutAction, Button> _bindingButtons = [];
    /// <summary>The action or the layout waiting for its new keys; only one at a time.</summary>
    private CaptureTarget? _capturing;
    /// <summary>The layout whose name is being edited in its row.</summary>
    private string? _renaming;
    /// <summary>The layout the user last worked on, drawn lighter; the one a clicked notification names.</summary>
    private string? _selectedLayout;

    private string _titleBarAppsText;
    /// <summary>False while the initial state is set, so opening the window does not report a change.</summary>
    private bool _placementReported;

    public SettingsWindow(IAutostart autostart, Workspace workspace, ILayoutStore layouts)
    {
        InitializeComponent();
        _workspace = workspace;
        _autostart = autostart;
        _layoutStore = layouts;
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
        BuildLayoutRows();
        Deactivated += (_, _) => StopCapture();
        // A layout may have been saved from a group meanwhile.
        Activated += (_, _) =>
        {
            if (_renaming is null) BuildLayoutRows();
        };
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
    /// True while a shortcut or a layout hotkey waits for its keys. The keyboard hook then sends key presses here
    /// instead: many shortcuts (anything with Win) never reach a WPF window.
    /// </summary>
    public bool IsCapturing => _capturing is not null;

    /// <summary>Brings the Layouts tab forward, with the layout of that name chosen when given.</summary>
    public void ShowLayoutsTab(string? layoutName)
    {
        SettingsTabs.SelectedItem = LayoutsTab;
        if (layoutName is not null) _selectedLayout = layoutName;
        BuildLayoutRows();
    }

    public void Capture(ShortcutKey binding)
    {
        var target = _capturing;
        _capturing = null;
        switch (target)
        {
            case ActionCapture capture:
                _workspace.BindShortcut(capture.Action, binding);
                break;
            case LayoutCapture capture:
                _workspace.SetLayoutHotkey(capture.Layout, binding);
                break;
            default:
                return;
        }
        // The workspace took the key from whatever action or layout had it: show both lists as they are now.
        ReloadBindings();
        ShowBindings();
        BuildLayoutRows();
    }

    private void StartCapture(CaptureTarget target)
    {
        _capturing = target;
        ShowBindings();
        BuildLayoutRows();
    }

    // ---- layouts --------------------------------------------------------------------------------

    private void BuildLayoutRows()
    {
        var layouts = _layoutStore.Load().OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        LayoutGrid.Children.Clear();
        LayoutGrid.RowDefinitions.Clear();
        NoLayoutsText.Visibility = layouts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var layout in layouts) AddLayoutRow(layout);
    }

    private void AddLayoutRow(SavedGroup layout)
    {
        var row = LayoutGrid.RowDefinitions.Count;
        LayoutGrid.RowDefinitions.Add(new RowDefinition());
        var background = RowBackground(layout.Name);
        Grid.SetColumnSpan(background, 3);
        AddLayoutCell(background, row, column: 0);
        AddLayoutCell(NameCell(layout.Name), row, column: 0);
        AddLayoutCell(HotkeyCell(layout), row, column: 1);
        AddLayoutCell(ActionsCell(layout.Name), row, column: 2);
    }

    private void AddLayoutCell(UIElement cell, int row, int column)
    {
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        LayoutGrid.Children.Add(cell);
    }

    /// <summary>The row's background, drawn lighter for the layout the user last worked on.</summary>
    private Border RowBackground(string name)
    {
        var background = new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 1, 0, 1) };
        if (name == _selectedLayout && TryFindResource("ControlFillColorSecondaryBrush") is Brush selected)
            background.Background = selected;
        return background;
    }

    private FrameworkElement NameCell(string name)
    {
        FrameworkElement cell = _renaming == name
            ? RenameBox(name)
            : new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
        cell.Margin = new Thickness(8, 4, 12, 4);
        cell.VerticalAlignment = VerticalAlignment.Center;
        return cell;
    }

    private StackPanel HotkeyCell(SavedGroup layout)
    {
        var name = layout.Name;
        var waiting = _capturing is LayoutCapture capture && capture.Layout == name;
        var hotkeyButton = new Button
        {
            Width = HotkeyButtonWidth,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = RowButtonPadding,
            Margin = RowButtonMargin,
            Content = waiting ? "Press keys…  (Esc: cancel)" : layout.Hotkey ?? "Click, then press keys",
        };
        if (layout.Hotkey is null && !waiting)
            hotkeyButton.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");
        System.Windows.Automation.AutomationProperties.SetName(hotkeyButton, $"Hotkey of {name}");
        hotkeyButton.Click += (_, _) =>
        {
            _selectedLayout = name;
            StartCapture(new LayoutCapture(name));
        };
        var clearButton = new Button
        {
            Content = "✕",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 3, 8, 3),
            Visibility = layout.Hotkey is null ? Visibility.Hidden : Visibility.Visible,
        };
        System.Windows.Automation.AutomationProperties.SetName(clearButton, $"Clear hotkey of {name}");
        clearButton.Click += (_, _) =>
        {
            _selectedLayout = name;
            _workspace.SetLayoutHotkey(name, null);
            BuildLayoutRows();
        };
        var cell = HorizontalCell();
        cell.Children.Add(hotkeyButton);
        cell.Children.Add(clearButton);
        return cell;
    }

    private StackPanel ActionsCell(string name)
    {
        var cell = HorizontalCell();
        cell.Children.Add(ActionButton("Open", $"Open {name}", () =>
        {
            // By name from the file just read: the row may be older than a layout saved over it since.
            if (_layoutStore.Load().Find(l => l.Name == name) is { } current) _workspace.OpenLayout(current);
        }));
        cell.Children.Add(ActionButton("Desktop shortcut", $"Desktop shortcut for {name}", () =>
        {
            _selectedLayout = name;
            _workspace.CreateLayoutShortcut(name);
        }));
        cell.Children.Add(MoreButton(name));
        return cell;
    }

    private static StackPanel HorizontalCell() => new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button ActionButton(string text, string automationName, Action click)
    {
        var button = new Button { Content = text, Padding = RowButtonPadding, Margin = RowButtonMargin };
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }

    private Button MoreButton(string name)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) =>
        {
            _renaming = name;
            _selectedLayout = name;
            BuildLayoutRows();
        };
        var delete = new MenuItem { Header = "Delete…" };
        delete.Click += (_, _) => DeleteLayout(name);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        var more = new Button { Content = "⋯", Padding = RowButtonPadding, Margin = RowButtonMargin, ContextMenu = menu };
        System.Windows.Automation.AutomationProperties.SetName(more, $"More actions for {name}");
        more.Click += (_, _) =>
        {
            menu.PlacementTarget = more;
            menu.IsOpen = true;
        };
        return more;
    }

    /// <summary>The name of a layout as a box in its row: Enter renames it, Esc or leaving the box cancels.</summary>
    private TextBox RenameBox(string name)
    {
        var box = new TextBox { Text = name };
        System.Windows.Automation.AutomationProperties.SetName(box, "Layout name");
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitRename(name, box.Text);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EndRename();
            }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (_renaming == name) EndRename();
        };
        return box;
    }

    private void EndRename()
    {
        _renaming = null;
        LayoutMessage.Visibility = Visibility.Collapsed;
        BuildLayoutRows();
    }

    private void CommitRename(string from, string to)
    {
        var layouts = _layoutStore.Load();
        if (!LayoutLibrary.Rename(layouts, from, to))
        {
            LayoutMessage.Text = to.Trim().Length == 0 ? "Enter a name." : $"A layout named \"{to.Trim()}\" already exists.";
            LayoutMessage.Visibility = Visibility.Visible;
            return;
        }
        _layoutStore.Save(layouts);
        _workspace.LayoutsChanged();
        _selectedLayout = to.Trim();
        EndRename();
    }

    private void DeleteLayout(string name)
    {
        var answer = MessageBox.Show(
            this, $"Delete the layout \"{name}\"?", "Dockyard", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        var layouts = _layoutStore.Load();
        layouts.RemoveAll(l => l.Name == name);
        _layoutStore.Save(layouts);
        _workspace.LayoutsChanged();
        BuildLayoutRows();
    }

    // ---- shortcuts ------------------------------------------------------------------------------

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
                StartCapture(new ActionCapture(action));
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
        var waiting = IsCapturing;
        _capturing = null;
        ShowBindings();
        if (waiting) BuildLayoutRows();
    }

    private void ReloadBindings()
    {
        _bindings.Clear();
        foreach (var (action, binding) in _workspace.ShortcutBindings) _bindings[action] = binding;
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
        // A layout may have lost its hotkey to a default.
        BuildLayoutRows();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
