using UnityEngine;

namespace Rimact;

// Paints in tree order, then the overlays. Nodes outside the clip are skipped, except native leaves:
// they run on every repaint to keep IMGUI control IDs stable
internal static class Painter
{
    // Hover fade time in seconds
    public const float HoverFade = 0.1f;

    public static void Paint(Host host)
    {
        var root = host.Root;
        if (root is null) return;
        var b = host.Backend;
        if (host.InspectorOn) host.Stats.Fibers = Count(root);
        b.PushClip(host.Rect);
        try
        {
            // For text with no color set anywhere above it
            var fg = Theme.Default.Foreground;
            PaintFiber(host, root, fg, b);
            foreach (var ov in host.Overlays)
                foreach (var c in ov.Children)
                    PaintFiber(host, c, fg, b);
        }
        finally
        {
            b.PopClip();
        }
    }

    private static int Count(Fiber f)
    {
        int n = 1;
        foreach (var c in f.Children) n += Count(c);
        return n;
    }

    private static void PaintFiber(Host host, Fiber f, Color fg, IBackend b)
    {
        if (f.Node is OverlayNode) return; // painted in the overlay pass
        if (f.IsTransparent)
        {
            foreach (var c in f.Children) PaintFiber(host, c, fg, b);
            return;
        }

        var node = f.Node;
        var rect = f.Rect;
        if (!rect.Overlaps(f.ClipRect) && node is not NativeNode)
        {
            // A clipping box hides its whole subtree. Otherwise, children may still overflow into view
            if (node is BoxNode box && box.Layout.ClipsChildren) PaintNatives(f, b);
            else foreach (var c in f.Children) PaintFiber(host, c, fg, b);
            return;
        }

        float opacity = (node.Disabled ? 0.5f : 1f) * node.Opacity;
        bool alpha = opacity < 1f;
        if (alpha) b.PushAlpha(opacity);
        try
        {
            PaintSelf(host, f, node, rect, fg, b);
            // Even when disabled, so it can explain why. Vanilla checks only the rect, so clipped or covered parts would show it
            if (node.Tooltip != null && host.Dispatcher.IsHovered(f)) b.Tooltip(rect, node.Tooltip);
            if (host.FocusVisible && host.Focused == f) b.DrawFocusRing(rect, host.FocusRingColor);
        }
        finally
        {
            if (alpha) b.PopAlpha();
        }
    }

    private static void PaintNatives(Fiber f, IBackend b)
    {
        foreach (var c in f.Children)
        {
            if (c.Node is NativeNode native) b.Native(c.Rect, c.ClipRect, native.Draw, !c.DisabledTree, Host.OwnerName(c));
            else PaintNatives(c, b);
        }
    }

    private static void PaintSelf(Host host, Fiber f, Node node, Rect rect, Color fg, IBackend b)
    {
        switch (node)
        {
            case BoxNode box:
                {
                    var state = host.Dispatcher.StateOf(f);
                    bool enabled = (state & InteractionState.Disabled) == 0;
                    bool hovered = enabled && (state & InteractionState.Hovered) != 0;
                    var v = Visual.Blend(box.Normal ?? Visual.Empty, box.Hover, HoverProgress(host, f, hovered && box.Hover != null));
                    if (enabled && (state & InteractionState.Pressed) != 0) v = v.Merge(box.Pressed);
                    PaintBox(b, rect, v, state);

                    var childFg = v.Fg ?? fg;
                    bool clip = box.Layout.ClipsChildren;
                    if (clip) b.PushClip(LayoutEngine.ClipArea(box, rect));
                    try
                    {
                        foreach (var c in f.Children) PaintFiber(host, c, childFg, b);
                    }
                    finally
                    {
                        if (clip) b.PopClip();
                    }
                    break;
                }
            case TextNode t:
                if (t.Text.Length > 0)
                    b.DrawText(t.Layout.Padding.Shrink(rect), t.Text, t.Style, t.Color ?? fg);
                break;
            case ImageNode img:
                if (img.Icon.Glyph is { } glyph) Glyphs.Paint(b, rect, glyph, img.Tint ?? fg); // glyphs follow the text color
                else b.DrawIcon(rect, img.Icon, img.Tint ?? Color.white);
                break;
            case CanvasNode canvas:
                canvas.Paint(b, rect);
                break;
            case NativeNode native:
                b.Native(rect, f.ClipRect, native.Draw, !f.DisabledTree, Host.OwnerName(f));
                break;
        }
    }

    internal static void PaintBox(IPainter b, Rect rect, Visual v, InteractionState state)
    {
        float radius = v.Radius ?? 0f;
        if (v.Shadow is { } shadow) b.DrawShadow(rect, radius, shadow);
        if (v.Part is { } part && part != VanillaPart.None) b.DrawVanilla(rect, part, state);
        if (v.Bg is { } bg) Fill(b, rect, bg, radius);
        if (v.Sheen is { } sheen)
        {
            float inset = radius * 0.3f;
            b.FillGradient(new Rect(rect.x + inset, rect.y, rect.width - inset * 2, rect.height), sheen, new Color(sheen.r, sheen.g, sheen.b, 0f), GradientDirection.Vertical);
        }
        if (v.TopEdge is { } edge && rect.width > radius * 2) b.FillRect(new Rect(rect.x + radius, rect.y, rect.width - radius * 2, 1f), edge);
        if (v.Bevel is { Width: > 0f } bevel)
        {
            float inset = v.Border is null ? 0f : v.BorderWidth ?? 1f;
            b.DrawBevel(new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2, rect.height - inset * 2), bevel.Light, bevel.Dark, bevel.Width);
        }
        if (v.Overlay is { } overlay) Fill(b, rect, overlay, radius);
        if (v.Border is { } border)
        {
            float w = v.BorderWidth ?? 1f;
            if (radius > 0f) b.StrokeRounded(rect, border, w, radius);
            else b.DrawBorder(rect, border, w);
        }
    }

    private static void Fill(IPainter b, Rect r, Color c, float radius)
    {
        if (radius > 0f) b.FillRounded(r, c, radius);
        else b.FillRect(r, c);
    }

    // Moves HoverT toward the goal and returns it smoothstepped
    private static float HoverProgress(Host host, Fiber f, bool hovered)
    {
        float goal = hovered ? 1f : 0f;
        float now = host.Backend.RealTime;
        if (host.ReduceMotion || f.HoverStamp < 0f) f.HoverT = goal;
        else f.HoverT = Mathf.MoveTowards(f.HoverT, goal, Mathf.Clamp(now - f.HoverStamp, 0f, 0.1f) / HoverFade);
        f.HoverStamp = now;
        float t = f.HoverT;
        return t * t * (3f - 2f * t);
    }
}
