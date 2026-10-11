using System.Text.Json.Serialization;
using Dockyard.Domain;

namespace Dockyard.Application;

/// <summary>A rectangle in physical screen pixels, as Windows reports window bounds.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore] public int Width => Right - Left;
    [JsonIgnore] public int Height => Bottom - Top;
    public Rect ToRect() => new(Left, Top, Width, Height);
}
