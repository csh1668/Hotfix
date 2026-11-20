using UnityEngine;

namespace Rimact;

internal enum SizeKind : byte { Auto, Px, Percent, Fill }

/// <summary>A size on one axis. A float converts to Px. Fill takes the leftover space but measures as its content.</summary>
internal readonly record struct Size(SizeKind Kind, float Value)
{
    public static readonly Size Auto = new(SizeKind.Auto, 0);
    public static readonly Size Fill = new(SizeKind.Fill, 0);
    public static Size Px(float v) => new(SizeKind.Px, v);
    public static Size Percent(float v) => new(SizeKind.Percent, v);
    public static implicit operator Size(float px) => Px(px);
    public static implicit operator Size(int px) => Px(px);

    /// <summary>The fixed size, or null. Percent is fixed only when parentInner is finite.</summary>
    public float? Resolve(float parentInner) => Kind switch
    {
        SizeKind.Px => Value,
        SizeKind.Percent when !float.IsInfinity(parentInner) => parentInner * Value / 100f,
        _ => null,
    };
}

internal readonly record struct Edges(float Left, float Top, float Right, float Bottom)
{
    public static readonly Edges Zero = default;
    public static Edges All(float v) => new(v, v, v, v);
    public static Edges Sym(float horizontal, float vertical) => new(horizontal, vertical, horizontal, vertical);
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
    public static implicit operator Edges(float v) => All(v);
    public static implicit operator Edges(int v) => All(v);

    public Rect Shrink(Rect r) =>
        new(r.x + Left, r.y + Top, Mathf.Max(0, r.width - Horizontal), Mathf.Max(0, r.height - Vertical));
}

internal enum Align : byte { Start, Center, End, Stretch }

internal enum Justify : byte { Start, Center, End, SpaceBetween }

internal sealed record LayoutProps
{
    public static readonly LayoutProps Default = new();

    public Size Width { get; init; } = Size.Auto;
    public Size Height { get; init; } = Size.Auto;
    public float MinWidth { get; init; }
    public float MaxWidth { get; init; } = float.PositiveInfinity;
    public float MinHeight { get; init; }
    public float MaxHeight { get; init; } = float.PositiveInfinity;
    public float Grow { get; init; }
    public float Shrink { get; init; }
    public Edges Padding { get; init; }
    public Edges Margin { get; init; }
    public float Gap { get; init; }
    /// <summary>Breaks children onto new lines when they overflow. Grow, shrink and justify apply per line.</summary>
    public bool Wrap { get; init; }
    /// <summary>Width / height. The unset side follows the other. With neither set, width is the available width.</summary>
    public float? AspectRatio { get; init; }
    /// <summary>Space between wrapped lines. null uses Gap.</summary>
    public float? LineGap { get; init; }
    /// <summary>Places children on the cross axis. On a Stack, on both axes.</summary>
    public Align Align { get; init; } = Align.Stretch;
    public Align? AlignSelf { get; init; }
    public Justify Justify { get; init; }
    /// <summary>Shifts the box after arrange. Siblings don't move.</summary>
    public Vector2 Offset { get; init; }
    public bool Clip { get; init; }
    /// <summary>Non-null makes a column a vertical scroll container, scrolled by this many pixels.</summary>
    public float? ScrollY { get; init; }
    /// <summary>Non-null makes a row a horizontal scroll container, scrolled by this many pixels.</summary>
    public float? ScrollX { get; init; }

    public bool ClipsChildren => Clip || ScrollY.HasValue || ScrollX.HasValue;
}
