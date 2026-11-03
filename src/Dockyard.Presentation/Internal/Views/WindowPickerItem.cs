using System.ComponentModel;
using Dockyard.Application;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>A row of the window picker: the window, and whether it is checked to be added along with the others.</summary>
internal sealed class WindowPickerItem(WindowInfo window) : INotifyPropertyChanged
{
    private bool _isChecked;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WindowInfo Window { get; } = window;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    // UI Automation names the list item after this, as it did when the list held the windows themselves.
    public override string ToString() => Window.ToString();
}
