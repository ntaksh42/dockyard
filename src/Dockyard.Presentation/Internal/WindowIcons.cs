using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dockyard.Application;

namespace Dockyard.Presentation.Internal;

/// <summary>
/// The icons tabs show, by window. Looked up once per window: tab bars are rebuilt often, and asking another app
/// for its icon takes a message round trip.
/// </summary>
internal sealed class WindowIcons(IWindowSystem windows)
{
    private readonly Dictionary<nint, ImageSource> _icons = [];

    /// <summary>The window's own icon, else its exe's; null when it has neither (asked again next time: a busy app may answer later).</summary>
    public ImageSource? Get(nint window)
    {
        if (_icons.TryGetValue(window, out var icon)) return icon;
        icon = FromWindow(window) ?? FromExe(windows.GetProcessPath(window));
        if (icon is not null) _icons[window] = icon;
        return icon;
    }

    /// <summary>Forgets the windows that are gone: Windows reuses handles, and a new window must not get an old icon.</summary>
    public void Keep(IEnumerable<nint> alive)
    {
        var keep = alive.ToHashSet();
        foreach (var window in _icons.Keys.Where(w => !keep.Contains(w)).ToList()) _icons.Remove(window);
    }

    private static ImageSource? FromWindow(nint window)
    {
        var handle = Native.GetWindowIcon(window);
        return handle == 0 ? null : FromHandle(handle);
    }

    // UWP apps run behind ApplicationFrameHost and have no icon of their own here: that is why the window comes first.
    private static ImageSource? FromExe(string? path)
    {
        if (path is null) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : FromHandle(icon.Handle);
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Copies the icon, so the handle stays with its owner.</summary>
    private static ImageSource? FromHandle(nint icon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
