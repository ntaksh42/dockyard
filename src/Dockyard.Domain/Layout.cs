namespace Dockyard.Domain;

public readonly record struct Rect(double X, double Y, double Width, double Height);

public readonly record struct Size(double Width, double Height);

/// <summary>Horizontal: children side by side (left | right). Vertical: stacked (top / bottom).</summary>
public enum Orientation { Horizontal, Vertical }

public enum DockSide { Left, Right, Top, Bottom }

public abstract class LayoutNode
{
    public SplitNode? Parent { get; internal set; }
}

/// <summary>A leaf of the layout tree. Holds external windows as tabs.</summary>
public sealed class Pane : LayoutNode
{
    public List<nint> Windows { get; } = [];
    public int ActiveIndex { get; set; }
    public nint? ActiveWindow => Windows.Count == 0 ? null : Windows[ActiveIndex];
}

public sealed class SplitNode : LayoutNode
{
    public SplitNode(Orientation orientation, LayoutNode first, LayoutNode second)
    {
        Orientation = orientation;
        First = first;
        Second = second;
        first.Parent = this;
        second.Parent = this;
    }

    public Orientation Orientation { get; }
    public LayoutNode First { get; internal set; }
    public LayoutNode Second { get; internal set; }

    /// <summary>Share of the available length (excluding the splitter) given to <see cref="First"/>.</summary>
    public double Ratio { get; set; } = 0.5;

    internal void Replace(LayoutNode oldChild, LayoutNode newChild)
    {
        if (First == oldChild) First = newChild;
        else if (Second == oldChild) Second = newChild;
        else throw new ArgumentException("Not a child of this split.", nameof(oldChild));
        newChild.Parent = this;
    }
}

public sealed class LayoutTree
{
    public LayoutNode Root { get; private set; } = new Pane();

    public IEnumerable<Pane> Panes => Enumerate(Root);

    public IEnumerable<nint> Windows => Panes.SelectMany(p => p.Windows);

    public Pane? FindPane(nint window) => Panes.FirstOrDefault(p => p.Windows.Contains(window));

    /// <summary>Replaces the whole tree, e.g. with a saved layout.</summary>
    public void Load(LayoutNode root)
    {
        if (root.Parent is not null) throw new ArgumentException("Not a root node.", nameof(root));
        Root = root;
    }

    public void AddTab(Pane pane, nint window)
    {
        pane.Windows.Add(window);
        pane.ActiveIndex = pane.Windows.Count - 1;
    }

    /// <summary>Splits <paramref name="target"/> and puts <paramref name="window"/> in a new pane on <paramref name="side"/>.</summary>
    public Pane Split(Pane target, DockSide side, nint window)
    {
        var pane = new Pane();
        pane.Windows.Add(window);

        var parent = target.Parent;
        var orientation = side is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
        var newFirst = side is DockSide.Left or DockSide.Top;
        var split = newFirst ? new SplitNode(orientation, pane, target) : new SplitNode(orientation, target, pane);

        if (parent is null) Root = split;
        else parent.Replace(target, split);
        return pane;
    }

    /// <summary>
    /// Removes the window. An emptied pane is collapsed unless it is the last one, or <paramref name="collapse"/>
    /// is false because something else still belongs there.
    /// </summary>
    public void Remove(nint window, bool collapse = true)
    {
        var pane = FindPane(window) ?? throw new ArgumentException("Window is not in this layout.", nameof(window));
        var index = pane.Windows.IndexOf(window);
        pane.Windows.RemoveAt(index);
        if (pane.ActiveIndex > index || pane.ActiveIndex >= pane.Windows.Count)
            pane.ActiveIndex = Math.Max(0, pane.ActiveIndex - 1);

        if (pane.Windows.Count > 0 || pane.Parent is null || !collapse) return;

        var split = pane.Parent;
        var sibling = split.First == pane ? split.Second : split.First;
        if (split.Parent is null)
        {
            Root = sibling;
            sibling.Parent = null;
        }
        else
        {
            split.Parent.Replace(split, sibling);
        }
    }

    public void Activate(nint window)
    {
        var pane = FindPane(window) ?? throw new ArgumentException("Window is not in this layout.", nameof(window));
        pane.ActiveIndex = pane.Windows.IndexOf(window);
    }

    /// <summary>The split whose divider forms the given edge of <paramref name="node"/>, or null if that edge is the outer border.</summary>
    public static SplitNode? FindDivider(LayoutNode node, DockSide edge)
    {
        var orientation = edge is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
        var nodeIsFirst = edge is DockSide.Right or DockSide.Bottom;
        for (var child = node; child.Parent is { } parent; child = parent)
            if (parent.Orientation == orientation && (parent.First == child) == nodeIsFirst) return parent;
        return null;
    }

    private static IEnumerable<Pane> Enumerate(LayoutNode node) => node switch
    {
        Pane p => [p],
        SplitNode s => Enumerate(s.First).Concat(Enumerate(s.Second)),
        _ => throw new InvalidOperationException(),
    };
}
