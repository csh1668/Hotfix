using System;
using UnityEngine;
using UColor = UnityEngine.Color;

namespace Rimact;

/// <summary>Node modifiers. Each returns a copy. On a component node they apply to the root of its output.</summary>
internal static class Modifiers
{
    private static T Map<T>(this T n, Func<Node, Node> f) where T : Node => (T)n.Modify(f);

    private static T Lay<T>(this T n, Func<LayoutProps, LayoutProps> f) where T : Node =>
        n.Map(x => x with { Layout = f(x.Layout) });

    private static T Ev<T>(this T n, EventProps add) where T : Node =>
        n.Map(x => x with { Events = (x.Events ?? new EventProps()).Merge(add) });

    private static T Box<T>(this T n, Func<BoxNode, BoxNode> f) where T : Node => n.Map(x => x is BoxNode b ? f(b) : x);

    #region Identity
    /// <summary>Reconciliation key. On list items, state follows the item through reorders, inserts and removals.</summary>
    public static T Key<T>(this T n, object key) where T : Node => n with { Key = key.ToString() };

    /// <summary>Skips re-rendering a component while its props are equal and its own state is unchanged.</summary>
    /// <remarks>Props compare by value, lambdas by captured values (see PropEquality). No effect on non-component nodes.</remarks>
    public static T Memo<T>(this T n) where T : Node => n switch
    {
        UIComponent c => (T)(Node)(c with { IsMemo = true }),
        _ => n,
    };

    #endregion

    #region Size
    public static T Width<T>(this T n, Rimact.Size w) where T : Node => n.Lay(l => l with { Width = w });
    public static T Height<T>(this T n, Rimact.Size h) where T : Node => n.Lay(l => l with { Height = h });
    public static T Size<T>(this T n, Rimact.Size w, Rimact.Size h) where T : Node => n.Lay(l => l with { Width = w, Height = h });
    public static T FillWidth<T>(this T n) where T : Node => n.Lay(l => l with { Width = Rimact.Size.Fill });
    public static T FillHeight<T>(this T n) where T : Node => n.Lay(l => l with { Height = Rimact.Size.Fill });
    public static T MinWidth<T>(this T n, float v) where T : Node => n.Lay(l => l with { MinWidth = v });
    /// <summary>Width / height, e.g. 16f / 9f. The side that isn't fixed is derived from the other.</summary>
    public static T AspectRatio<T>(this T n, float ratio) where T : Node => n.Lay(l => l with { AspectRatio = ratio });
    public static T MaxWidth<T>(this T n, float v) where T : Node => n.Lay(l => l with { MaxWidth = v });
    public static T MinHeight<T>(this T n, float v) where T : Node => n.Lay(l => l with { MinHeight = v });
    public static T MaxHeight<T>(this T n, float v) where T : Node => n.Lay(l => l with { MaxHeight = v });
    public static T Grow<T>(this T n, float g = 1f) where T : Node => n.Lay(l => l with { Grow = g });
    public static T Shrink<T>(this T n, float s = 1f) where T : Node => n.Lay(l => l with { Shrink = s });

    #endregion

    #region Spacing and alignment
    public static T Padding<T>(this T n, Edges p) where T : Node => n.Lay(l => l with { Padding = p });
    public static T Padding<T>(this T n, float horizontal, float vertical) where T : Node => n.Lay(l => l with { Padding = Edges.Sym(horizontal, vertical) });
    public static T Margin<T>(this T n, Edges m) where T : Node => n.Lay(l => l with { Margin = m });
    public static T Margin<T>(this T n, float horizontal, float vertical) where T : Node => n.Lay(l => l with { Margin = Edges.Sym(horizontal, vertical) });
    public static T Gap<T>(this T n, float g) where T : Node => n.Lay(l => l with { Gap = g });
    /// <summary>Wraps a row's or column's children onto more lines when they overflow. lineGap defaults to Gap.</summary>
    public static T FlexWrap<T>(this T n, bool wrap = true, float? lineGap = null) where T : Node =>
        n.Lay(l => l with { Wrap = wrap, LineGap = lineGap });
    public static T Align<T>(this T n, Rimact.Align a) where T : Node => n.Lay(l => l with { Align = a });
    public static T AlignSelf<T>(this T n, Rimact.Align a) where T : Node => n.Lay(l => l with { AlignSelf = a });
    public static T Justify<T>(this T n, Rimact.Justify j) where T : Node => n.Lay(l => l with { Justify = j });
    public static T Center<T>(this T n) where T : Node => n.Lay(l => l with { Align = Rimact.Align.Center, Justify = Rimact.Justify.Center });
    public static T Offset<T>(this T n, float x, float y) where T : Node => n.Lay(l => l with { Offset = new Vector2(x, y) });
    public static T Clip<T>(this T n, bool clip = true) where T : Node => n.Lay(l => l with { Clip = clip });
    public static T ScrollY<T>(this T n, float offset) where T : Node => n.Lay(l => l with { ScrollY = offset });
    /// <summary>Rows only. Like ScrollY, the box doesn't size to its content.</summary>
    public static T ScrollX<T>(this T n, float offset) where T : Node => n.Lay(l => l with { ScrollX = offset });

    #endregion

    #region Events
    public static T OnClick<T>(this T n, Action onClick) where T : Node => n.Ev(new EventProps { OnClick = onClick });
    public static T OnMouseDown<T>(this T n, Func<PointerEvent, bool> h) where T : Node => n.Ev(new EventProps { OnMouseDown = h });
    public static T OnMouseUp<T>(this T n, Func<PointerEvent, bool> h) where T : Node => n.Ev(new EventProps { OnMouseUp = h });
    public static T OnDrag<T>(this T n, Action<PointerEvent> h) where T : Node => n.Ev(new EventProps { OnDrag = h });
    public static T OnWheel<T>(this T n, Func<WheelEvent, bool> h) where T : Node => n.Ev(new EventProps { OnWheel = h });
    public static T OnHover<T>(this T n, Action<bool> h) where T : Node => n.Ev(new EventProps { OnHover = h });
    /// <summary>Pointer position on each repaint while the pointer is over this node, before it paints.</summary>
    public static T OnPointerMove<T>(this T n, Action<PointerEvent> h) where T : Node => n.Ev(new EventProps { OnPointerMove = h });
    /// <summary>Keys while this node or one inside it has focus. Return true to consume.</summary>
    public static T OnKeyDown<T>(this T n, Func<KeyEvent, bool> h) where T : Node => n.Ev(new EventProps { OnKeyDown = h });
    /// <summary>Tab stop. Nodes with OnClick are focusable by default.</summary>
    public static T Focusable<T>(this T n, bool focusable = true) where T : Node => n.Ev(new EventProps { Focusable = focusable });
    public static T OnFocus<T>(this T n, Action<bool> h) where T : Node => n.Ev(new EventProps { OnFocus = h });
    /// <summary>Arrow keys move focus inside, Tab treats it as one stop. selectOnMove also clicks the new node.</summary>
    public static T FocusGroup<T>(this T n, bool selectOnMove = false) where T : Node =>
        n.Ev(new EventProps { FocusGroup = selectOnMove ? FocusGroupMode.Select : FocusGroupMode.Move });
    /// <summary>Gets a rect inside this node that should be scrolled into view.</summary>
    public static T OnReveal<T>(this T n, Action<Rect> h) where T : Node => n.Ev(new EventProps { OnReveal = h });
    /// <summary>Focuses this node on mount, e.g. a dialog's main button.</summary>
    public static T AutoFocus<T>(this T n, bool autoFocus = true) where T : Node => n.Ev(new EventProps { AutoFocus = autoFocus });
    /// <summary>Where Tab lands when entering the focus group.</summary>
    public static T FocusEntry<T>(this T n, bool entry = true) where T : Node => n.Ev(new EventProps { FocusEntry = entry });

    #endregion

    #region Misc
    public static T Tooltip<T>(this T n, string? tip) where T : Node => n.Map(x => x with { Tooltip = tip });
    public static T Disabled<T>(this T n, bool disabled = true) where T : Node => n.Map(x => x with { Disabled = disabled });
    /// <summary>Alpha for the node and its subtree. Still takes input at 0.</summary>
    public static T Opacity<T>(this T n, float opacity) where T : Node => n.Map(x => x with { Opacity = Mathf.Clamp01(opacity) });

    #endregion

    #region Box appearance
    public static T Bg<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Bg = c } });
    public static T Border<T>(this T n, UColor c, float width = 1f) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Border = c, BorderWidth = width } });
    public static T Fg<T>(this T n, UColor c) where T : Node => n.Map(x => x switch
    {
        BoxNode b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Fg = c } },
        TextNode t => t with { Color = c },
        _ => x,
    });
    public static T Vanilla<T>(this T n, VanillaPart part) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Part = part } });
    public static T Visual<T>(this T n, Rimact.Visual normal, Rimact.Visual? hover = null, Rimact.Visual? pressed = null) where T : Node =>
        n.Box(b => b with { Normal = normal, Hover = hover ?? b.Hover, Pressed = pressed ?? b.Pressed });
    public static T HoverBg<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Hover = (b.Hover ?? Rimact.Visual.Empty) with { Bg = c } });
    public static T HoverFg<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Hover = (b.Hover ?? Rimact.Visual.Empty) with { Fg = c } });
    public static T PressedBg<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Pressed = (b.Pressed ?? Rimact.Visual.Empty) with { Bg = c } });
    public static T Radius<T>(this T n, float radius) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Radius = radius } });
    /// <summary>Soft shadow under the box, size in pixels.</summary>
    public static T Shadow<T>(this T n, float size, UColor color) where T : Node =>
        n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Shadow = new Rimact.Shadow(size, color) } });
    /// <summary>Square bevel inside the box. Light goes on the top and right, dark on the left and bottom.</summary>
    public static T Bevel<T>(this T n, Rimact.Bevel bevel) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Bevel = bevel } });
    /// <summary>1px line along the inside of the top edge.</summary>
    public static T TopEdge<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { TopEdge = c } });
    /// <summary>Fades from c at the top to transparent at the bottom, over the background.</summary>
    public static T Sheen<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Sheen = c } });
    /// <summary>Color laid over the background, e.g. for a selected state.</summary>
    public static T Overlay<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Normal = (b.Normal ?? Rimact.Visual.Empty) with { Overlay = c } });
    public static T HoverOverlay<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Hover = (b.Hover ?? Rimact.Visual.Empty) with { Overlay = c } });
    public static T PressedOverlay<T>(this T n, UColor c) where T : Node => n.Box(b => b with { Pressed = (b.Pressed ?? Rimact.Visual.Empty) with { Overlay = c } });

    #endregion

    #region Text
    public static T Font<T>(this T n, FontSize f) where T : Node => n.Map(x => x is TextNode t ? t with { Font = f } : x);
    public static T Color<T>(this T n, UColor c) where T : Node => n.Map(x => x is TextNode t ? t with { Color = c } : x);
    public static T Anchor<T>(this T n, TextAlign a) where T : Node => n.Map(x => x is TextNode t ? t with { Align = a } : x);
    public static T Wrap<T>(this T n, bool wrap = true) where T : Node => n.Map(x => x is TextNode t ? t with { Wrap = wrap } : x);
    public static T Truncate<T>(this T n, bool truncate = true) where T : Node => n.Map(x => x is TextNode t ? t with { Truncate = truncate } : x);
    /// <summary>Text size in pixels, overriding the font's own.</summary>
    public static T TextSize<T>(this T n, float px) where T : Node => n.Map(x => x is TextNode t ? t with { Size = px } : x);
    public static T Bold<T>(this T n, bool bold = true) where T : Node => n.Map(x => x is TextNode t ? t with { Bold = bold } : x);
    public static T Tint<T>(this T n, UColor c) where T : Node => n.Map(x => x is ImageNode i ? i with { Tint = c } : x);

    #endregion
}
