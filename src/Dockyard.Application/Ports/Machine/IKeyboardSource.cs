namespace Dockyard.Application;

/// <summary>Key presses with Ctrl, Alt or Win held, wherever the focus is.</summary>
public interface IKeyboardSource
{
    /// <summary>Gets the key and the modifiers held; returns true to take the key from the app that would get it.</summary>
    Func<int, KeyModifiers, bool>? KeyPressed { get; set; }
}
