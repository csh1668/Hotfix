using System;
using UnityEngine;

namespace Rimact;

internal enum Axis : byte { Row, Column, Stack }

/// <summary>Immutable UI description, rebuilt every render.</summary>
internal abstract record Node
{
    public string? Key { get; init; }
    public LayoutProps Layout { get; init; } = LayoutProps.Default;
    public EventProps? Events { get; init; }
    public string? Tooltip { get; init; }
    public bool Disabled { get; init; }
    /// <summary>Multiplies the alpha of this node and its subtree. Changing it doesn't re-run layout.</summary>
    public float Opacity { get; init; } = 1f;

    // Host nodes apply f now. Component nodes defer it to their output root
    internal virtual Node Modify(Func<Node, Node> f) => f(this);
}

/// <summary>Row, Column or Stack container.</summary>
internal sealed record BoxNode(Axis Axis, Node[] Children) : Node
{
    public Visual? Normal { get; init; }
    public Visual? Hover { get; init; }
    public Visual? Pressed { get; init; }

    public bool Equals(BoxNode? other) =>
        other is not null && base.Equals(other) && Axis == other.Axis && Normal == other.Normal &&
        Hover == other.Hover && Pressed == other.Pressed && NodeArrays.Equal(Children, other.Children);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Axis, Children.Length);
}

internal sealed record TextNode(string Text) : Node
{
    public FontSize? Font { get; init; }
    /// <summary>null uses the nearest box's Fg, hover and pressed state included.</summary>
    public Color? Color { get; init; }
    public TextAlign Align { get; init; } = TextAlign.MiddleLeft;
    public bool Wrap { get; init; }
    public bool Truncate { get; init; }
    /// <summary>Size in pixels. null uses the font's own.</summary>
    public float? Size { get; init; }
    public bool Bold { get; init; }

    internal TextStyle Style => new(Font ?? FontSize.Small, Align, Wrap, Truncate, Size ?? 0f, Bold);
}

internal sealed record ImageNode(Icon Icon) : Node
{
    public Color? Tint { get; init; }
}

internal sealed record CanvasNode(Action<IPainter, Rect> Paint) : Node;

internal sealed record NativeNode(Action<Rect> Draw) : Node;

/// <summary>Has its own fiber and key, but its children join the parent's layout.</summary>
internal sealed record FragmentNode(Node[] Children) : Node
{
    public static readonly FragmentNode Empty = new([]);

    public bool Equals(FragmentNode? other) =>
        other is not null && base.Equals(other) && NodeArrays.Equal(Children, other.Children);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Children.Length);
}

internal sealed record ProviderNode(IContext Context, object? Value, Node Child) : Node;

internal enum Placement : byte
{
    Below,
    Above,
    Right,
    Left,
    /// <summary>Laid out in the host rect like a Stack child, so AlignSelf works. Only the child's rect takes input.</summary>
    Fill,
}

/// <summary>Drawn on the overlay layer beside its anchor, the nearest box ancestor.</summary>
/// <remarks>Flips to the other side if it would overflow, then clamps to the host rect. Fill ignores the anchor.</remarks>
internal sealed record OverlayNode(Node Child, Placement Placement) : Node
{
    public Action? OnPointerDownOutside { get; init; }
    public Action? OnEscape { get; init; }
    public float SideOffset { get; init; } = 4f;
    /// <summary>Fill only. Keeps the child's own size and places it at this fraction of the free space (0 = left/top).</summary>
    public Vector2? FillAlign { get; init; }
}

/// <summary>Component base. Props are record properties, Build returns the output.</summary>
internal abstract record UIComponent : Node
{
    public Func<Node, Node>[]? Decorators { get; init; }
    public bool IsMemo { get; init; }

    protected internal abstract Node? Build();

    /// <summary>True makes this an error boundary for its subtree.</summary>
    protected internal virtual bool CatchesErrors => false;

    internal override Node Modify(Func<Node, Node> f) => this with { Decorators = NodeArrays.Append(Decorators, f) };
}

internal static class NodeArrays
{
    public static bool Equal(Node[] a, Node[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(a[i], b[i])) return false;
        return true;
    }

    public static T[] Append<T>(T[]? arr, T item)
    {
        if (arr is null) return [item];
        var r = new T[arr.Length + 1];
        Array.Copy(arr, r, arr.Length);
        r[arr.Length] = item;
        return r;
    }

    // null children become empty fragments, so siblings keep their slots and state
    public static Node[] Normalize(Node?[]? children)
    {
        if (children is null || children.Length == 0) return [];
        var r = new Node[children.Length];
        for (int i = 0; i < children.Length; i++) r[i] = children[i] ?? FragmentNode.Empty;
        return r;
    }
}
