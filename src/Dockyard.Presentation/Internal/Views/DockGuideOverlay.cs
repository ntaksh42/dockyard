using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Dockyard.Application;
using Dockyard.Domain;
using static Dockyard.Presentation.Internal.Native;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>Shows where a dragged window would land: a click-through overlay over the frame it is above.</summary>
internal sealed class DockGuideOverlay : IDockGuide
{
    private Window? _overlay;
    private (IGroupFrame Group, DockTarget Drop, PixelRect Bounds, double Scale)? _shown;

    public void Hide()
    {
        _overlay?.Close();
        _overlay = null;
        _shown = null;
    }

    public void Show(IGroupFrame group, DockTarget drop)
    {
        var bounds = group.ToScreen(drop.Layout.Bounds);
        var scale = group.Scale;
        var state = (group, drop, bounds, scale);
        if (_shown == state) return;
        _shown = state;
        if (_overlay is null)
        {
            _overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                IsHitTestVisible = false,
            };
            _overlay.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(_overlay).Handle;
                SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)((long)GetWindowLongPtr(handle, GWL_EXSTYLE) | 0x20 | 0x08000000 | WS_EX_TOOLWINDOW));
            };
            _overlay.Show();
        }
        var canvas = new Canvas();
        var preview = drop.Preview;
        var highlight = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(2),
            Width = preview.Width,
            Height = preview.Height,
        };
        Canvas.SetLeft(highlight, preview.X - drop.Layout.Bounds.X);
        Canvas.SetTop(highlight, preview.Y - drop.Layout.Bounds.Y);
        canvas.Children.Add(highlight);
        var r = drop.Layout.Bounds;
        foreach (var (text, x, y) in new[] { ("Tab", .5, .5), ("←", .125, .5), ("→", .875, .5), ("↑", .5, .125), ("↓", .5, .875) })
        {
            var guide = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Background = Brushes.DodgerBlue,
                TextAlignment = TextAlignment.Center,
                Width = 38,
                Height = 28,
                Padding = new Thickness(2, 5, 2, 0)
            };
            Canvas.SetLeft(guide, Math.Max(0, r.Width * x - 19));
            Canvas.SetTop(guide, Math.Max(0, r.Height * y - 14));
            canvas.Children.Add(guide);
        }
        _overlay.Content = canvas;
        // Position in physical pixels so cross-monitor drags do not inherit the source monitor's DPI.
        SetWindowPos(new WindowInteropHelper(_overlay).Handle, HWND_TOPMOST, bounds.Left, bounds.Top,
            bounds.Width, bounds.Height, SWP_NOACTIVATE);
        var overlayScale = VisualTreeHelper.GetDpi(_overlay).DpiScaleX;
        canvas.LayoutTransform = new ScaleTransform(scale / overlayScale, scale / overlayScale);
    }
}
