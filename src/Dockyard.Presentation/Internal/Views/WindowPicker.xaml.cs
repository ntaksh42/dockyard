using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dockyard.Application;
using Dockyard.Domain;
using static Dockyard.Presentation.Internal.Native;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>
/// Picks a window to add: typing narrows the list, and the selected window shows as a live thumbnail.
/// Shift+Enter and Ctrl+Enter add it in a new pane to the right or below instead of the way the picker was opened.
/// </summary>
internal partial class WindowPicker : Window
{
    private readonly IWindowSystem _windows;
    private readonly List<WindowInfo> _candidates;
    private nint _thumbnail;
    private WindowHandle _thumbnailSource = WindowHandle.None;

    /// <param name="isGrouped">Windows already in a group are left out.</param>
    /// <param name="side">How the window is added unless the keys used to pick it say otherwise: null for a tab.</param>
    public WindowPicker(Func<WindowHandle, bool> isGrouped, IWindowCatalog catalog, IWindowSystem windows, DockSide? side)
    {
        InitializeComponent();
        _windows = windows;
        Side = side;
        _candidates = catalog.GetCandidates().Where(w => !isGrouped(w.Handle)).ToList();
        ShowCandidates();
        Loaded += (_, _) =>
        {
            FilterBox.Focus();
            UpdatePreview();
        };
        Closed += (_, _) => ClearThumbnail();
    }

    public WindowInfo? Selected => List.SelectedItem as WindowInfo;

    /// <summary>Where the picked window goes: null for a tab, otherwise the side of the pane to split off.</summary>
    public DockSide? Side { get; private set; }

    private void ShowCandidates()
    {
        var shown = _candidates.Where(w => w.Matches(FilterBox.Text)).ToList();
        List.ItemsSource = shown;
        List.SelectedItem = shown.FirstOrDefault(w => w.Controllable);
        EmptyText.Text = _candidates.Count == 0
            ? "No other windows to add. Open the app you want, then try again."
            : "No windows match.";
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = Selected is { Controllable: true };
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ShowCandidates();

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddButton.IsEnabled = Selected is { Controllable: true };
        if (List.SelectedItem is not null) List.ScrollIntoView(List.SelectedItem);
        UpdatePreview();
    }

    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreview();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The arrows move through the list while the focus stays in the filter box, so typing can go on.
        if (e.Key is Key.Down or Key.Up && FilterBox.IsKeyboardFocused && List.Items.Count > 0)
        {
            var step = e.Key == Key.Down ? 1 : -1;
            List.SelectedIndex = Math.Clamp(List.SelectedIndex + step, 0, List.Items.Count - 1);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers == ModifierKeys.Shift) Side = DockSide.Right;
        else if (Keyboard.Modifiers == ModifierKeys.Control) Side = DockSide.Bottom;
        else return;
        Accept();
        e.Handled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (Selected is { Controllable: true }) DialogResult = true;
    }

    /// <summary>Draws the selected window's live thumbnail over the preview area, fitted and centred.</summary>
    private void UpdatePreview()
    {
        var source = Selected?.Handle ?? WindowHandle.None;
        if (!IsLoaded || PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
        {
            return;
        }
        if (source.IsNone || !_windows.Exists(source) || _windows.IsMinimized(source))
        {
            ClearThumbnail();
            PreviewText.Text = source.IsNone ? "" : "No preview: the window is minimized.";
            return;
        }
        PreviewText.Text = "";
        if (source != _thumbnailSource)
        {
            ClearThumbnail();
            if (DwmRegisterThumbnail(new WindowInteropHelper(this).Handle, source.Value, out _thumbnail) != 0) return;
            _thumbnailSource = source;
        }
        var full = _windows.GetBounds(source);
        var visible = _windows.GetVisibleBounds(source);
        if (visible.Width <= 0 || visible.Height <= 0) return;
        // The thumbnail is placed in the picker's client area, in pixels.
        var scale = target.TransformToDevice.M11;
        var origin = Preview.TransformToAncestor(this).Transform(new Point(0, 0));
        double boxWidth = Preview.ActualWidth * scale, boxHeight = Preview.ActualHeight * scale;
        var fit = Math.Min(1, Math.Min(boxWidth / visible.Width, boxHeight / visible.Height));
        int width = (int)(visible.Width * fit), height = (int)(visible.Height * fit);
        var left = (int)(origin.X * scale + (boxWidth - width) / 2);
        var top = (int)(origin.Y * scale + (boxHeight - height) / 2);
        // Leave out the invisible resize border most windows have.
        int sourceLeft = visible.Left - full.Left, sourceTop = visible.Top - full.Top;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
            rcDestination = new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height },
            rcSource = new RECT { Left = sourceLeft, Top = sourceTop, Right = sourceLeft + visible.Width, Bottom = sourceTop + visible.Height },
            opacity = 255,
            fVisible = true,
        };
        DwmUpdateThumbnailProperties(_thumbnail, ref properties);
    }

    private void ClearThumbnail()
    {
        if (_thumbnail != 0) DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
        _thumbnailSource = WindowHandle.None;
    }
}
