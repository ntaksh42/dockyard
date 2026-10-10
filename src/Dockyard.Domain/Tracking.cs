namespace Dockyard.Domain;

[Flags]
public enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>What a user drag on a member window amounts to.</summary>
public readonly record struct DragChange(bool Moved, Edges Resized);

public static class DragInterpreter
{
    /// <summary>
    /// Compares the window's bounds at drag start with its current bounds. Same size means a move; otherwise
    /// an edge counts as resized only when the opposite edge stayed put.
    /// </summary>
    public static DragChange Interpret(Rect start, Rect current)
    {
        if (current.Width == start.Width && current.Height == start.Height)
            return new DragChange(current.X != start.X || current.Y != start.Y, Edges.None);

        var edges = Edges.None;
        bool leftSame = current.X == start.X, rightSame = Right(current) == Right(start);
        bool topSame = current.Y == start.Y, bottomSame = Bottom(current) == Bottom(start);
        if (leftSame && !rightSame) edges |= Edges.Right;
        if (rightSame && !leftSame) edges |= Edges.Left;
        if (topSame && !bottomSame) edges |= Edges.Bottom;
        if (bottomSame && !topSame) edges |= Edges.Top;
        return new DragChange(false, edges);
    }

    private static double Right(Rect r) => r.X + r.Width;
    private static double Bottom(Rect r) => r.Y + r.Height;
}

public enum SettleAction
{
    /// <summary>The window is where we put it (or we have given up on it).</summary>
    None,
    /// <summary>Send the target again; the app may just have been slow.</summary>
    Retry,
    /// <summary>The window kept a larger size twice: treat that size as its minimum.</summary>
    LearnMinimum,
}

/// <summary>
/// Decides what to do once a window has had time to apply the bounds we asked for. Retries are capped per
/// target so a window that refuses to move (e.g. an elevated app) cannot cause an endless re-sync loop.
/// </summary>
public sealed class SettleTracker(double tolerance = 8, int maxRetries = 2)
{
    private Rect? _target;
    private Size? _suspect;
    private int _retries;

    public SettleAction Evaluate(Rect target, Rect actual)
    {
        if (_target != target)
        {
            _target = target;
            _suspect = null;
            _retries = 0;
        }

        bool samePosition = Near(actual.X, target.X) && Near(actual.Y, target.Y);
        bool larger = actual.Width > target.Width + tolerance || actual.Height > target.Height + tolerance;
        bool sameSize = Near(actual.Width, target.Width, tolerance) && Near(actual.Height, target.Height, tolerance);

        // Within tolerance: windows that snap to a character grid land a few pixels off; that is fine.
        if (samePosition && !larger && sameSize)
        {
            _suspect = null;
            return SettleAction.None;
        }

        if (samePosition && larger)
        {
            var size = new Size(actual.Width, actual.Height);
            if (_suspect == size)
            {
                _suspect = null;
                return SettleAction.LearnMinimum;
            }
            _suspect = size;
        }
        else
        {
            _suspect = null;
        }

        if (_retries >= maxRetries) return SettleAction.None;
        _retries++;
        return SettleAction.Retry;
    }

    private static bool Near(double a, double b, double tolerance = 1) => Math.Abs(a - b) <= tolerance;
}
