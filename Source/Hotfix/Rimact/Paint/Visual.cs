using System;
using UnityEngine;

namespace Rimact;

internal enum FontSize : byte { Tiny, Small, Medium }

internal enum TextAlign : byte { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

/// <summary>Size is in pixels and overrides the font's own (0 keeps it). Bold is synthesized.</summary>
internal readonly record struct TextStyle(FontSize Font, TextAlign Align, bool Wrap, bool Truncate, float Size = 0f, bool Bold = false);

internal enum GradientDirection : byte
{
    Vertical,
    Horizontal,
    /// <summary>From both sides to the center.</summary>
    HorizontalMirror,
}

/// <summary>Square bevel inside a box's edge: Light on the top and right, Dark on the left and bottom.</summary>
internal readonly record struct Bevel(Color Light, Color Dark, float Width)
{
    /// <summary>Light and Dark swapped, so a raised face looks pressed in.</summary>
    public Bevel Inverted => new(Dark, Light, Width);
}

/// <summary>Size is the blur reach in pixels.</summary>
internal readonly record struct Shadow(float Size, Color Color);

internal enum VanillaPart : byte
{
    None,
    Button, // shows hover and pressed state
    ButtonSubtle,
    Tab,
    TabSelected,
    Highlight,
    LightHighlight,
    MenuSection,
    WindowBackground,
    SliderRail,
    SliderHandle,
}

[Flags]
internal enum InteractionState : byte
{
    None = 0,
    Hovered = 1,
    Pressed = 2,
    Disabled = 4,
}

internal enum UiSound : byte { Click, Reject, CheckboxOn, CheckboxOff, TabSelect, MenuOpen, Tick, DragSlider }

internal enum BuiltinIcon : byte { None, CheckboxOn, CheckboxOff, CheckboxPartial, RadioOn, RadioOff, Close, Plus, Minus, Search, ChevronDown, ChevronRight, Info, Warning }

/// <summary>A texture, built-in icon, Glyph, def, thing or pawn portrait. A def converts implicitly.</summary>
/// <remarks>Game icons keep their own colors. A tint only fades them through its alpha.</remarks>
internal sealed record Icon(Texture2D? Texture, BuiltinIcon Builtin)
{
    public Glyph? Glyph { get; init; }
    /// <summary>Drawn with Widgets.DefIcon, in Stuff's color if set.</summary>
    public Verse.Def? Def { get; init; }
    public Verse.ThingDef? Stuff { get; init; }
    public Verse.Thing? Thing { get; init; }
    /// <summary>Draws Thing (a pawn) as its portrait, facing south.</summary>
    public bool Portrait { get; init; }

    public bool IsGame => Def != null || Thing != null;

    public static Icon Of(Verse.Def def, Verse.ThingDef? stuff = null) => new(null, BuiltinIcon.None) { Def = def, Stuff = stuff };
    public static Icon Of(Verse.Thing thing) => new(null, BuiltinIcon.None) { Thing = thing };
    public static Icon PortraitOf(Verse.Pawn pawn) => new(null, BuiltinIcon.None) { Thing = pawn, Portrait = true };

    public static implicit operator Icon(Texture2D tex) => new(tex, BuiltinIcon.None);
    public static implicit operator Icon(BuiltinIcon b) => new(null, b);
    public static implicit operator Icon(Glyph g) => new(null, BuiltinIcon.None) { Glyph = g };
    public static implicit operator Icon(Verse.Def def) => Of(def);

    public override string ToString() =>
        Texture != null ? $"tex:{Texture.name}"
        : Def != null ? $"def:{Def.defName}{(Stuff != null ? "/" + Stuff.defName : "")}"
        : Thing != null ? $"{(Portrait ? "portrait" : "thing")}:{Thing.ThingID}"
        : Glyph?.ToString() ?? Builtin.ToString();
}

/// <summary>A box's look. null fields are unset, so Merge keeps the value underneath.</summary>
internal sealed record Visual
{
    public static readonly Visual Empty = new();

    public Color? Bg { get; init; }
    public Color? Border { get; init; }
    public float? BorderWidth { get; init; }
    public Color? Fg { get; init; }
    public VanillaPart? Part { get; init; }
    public float? Radius { get; init; }
    public Shadow? Shadow { get; init; }
    /// <summary>Gradient from this color at the top to clear at the bottom.</summary>
    public Color? Sheen { get; init; }
    /// <summary>1px line along the inside of the top edge.</summary>
    public Color? TopEdge { get; init; }
    /// <summary>Translucent layer over the background, for hover, pressed and selected states.</summary>
    public Color? Overlay { get; init; }
    /// <summary>Drawn inside the border.</summary>
    public Bevel? Bevel { get; init; }

    public Visual Merge(Visual? over) => over is null ? this : new Visual
    {
        Bg = over.Bg ?? Bg,
        Border = over.Border ?? Border,
        BorderWidth = over.BorderWidth ?? BorderWidth,
        Fg = over.Fg ?? Fg,
        Part = over.Part ?? Part,
        Radius = over.Radius ?? Radius,
        Shadow = over.Shadow ?? Shadow,
        Sheen = over.Sheen ?? Sheen,
        TopEdge = over.TopEdge ?? TopEdge,
        Overlay = over.Overlay ?? Overlay,
        Bevel = over.Bevel ?? Bevel,
    };

    /// <summary>Blends a toward a.Merge(b). One-sided colors fade in from clear. Most other fields switch halfway.</summary>
    public static Visual Blend(Visual a, Visual? b, float t)
    {
        if (b is null || t <= 0f) return a;
        var m = a.Merge(b);
        if (t >= 1f) return m;
        return new Visual
        {
            Bg = Mix(a.Bg, m.Bg, t),
            Border = Mix(a.Border, m.Border, t),
            BorderWidth = t < 0.5f ? a.BorderWidth : m.BorderWidth,
            Fg = Mix(a.Fg, m.Fg, t),
            Part = t < 0.5f ? a.Part : m.Part,
            Radius = a.Radius ?? m.Radius,
            Shadow = t < 0.5f ? a.Shadow : m.Shadow,
            Sheen = Mix(a.Sheen, m.Sheen, t),
            TopEdge = Mix(a.TopEdge, m.TopEdge, t),
            Overlay = Mix(a.Overlay, m.Overlay, t),
            Bevel = t < 0.5f ? a.Bevel : m.Bevel,
        };
    }

    private static Color? Mix(Color? from, Color? to, float t)
    {
        if (from is null && to is null) return null;
        var f = from ?? Clear(to!.Value);
        var e = to ?? Clear(f);
        return Color.Lerp(f, e, t);
    }

    private static Color Clear(Color c) => new(c.r, c.g, c.b, 0f);
}

internal static class Faces
{
    /// <summary>A raised face like a button's: fill, bevel and 1px outline.</summary>
    public static void DrawRaised(this IPainter p, Rect r, Color face, Theme t, float bevelWidth)
    {
        p.FillRect(r, face);
        if (r.width > 2f && r.height > 2f && bevelWidth > 0f)
        {
            var b = t.Raised(face, bevelWidth);
            p.DrawBevel(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), b.Light, b.Dark, b.Width);
        }
        p.DrawBorder(r, t.Outline, 1f);
    }

    /// <summary>A recessed well like a track or field: fill, Theme.Recessed bevel and 1px outline.</summary>
    public static void DrawRecessed(this IPainter p, Rect r, Color face, Theme t, float bevelWidth)
    {
        p.FillRect(r, face);
        if (r.width > 2f && r.height > 2f && bevelWidth > 0f)
        {
            var b = t.Recessed(face, bevelWidth);
            p.DrawBevel(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), b.Light, b.Dark, b.Width);
        }
        p.DrawBorder(r, t.Outline, 1f);
    }
}
