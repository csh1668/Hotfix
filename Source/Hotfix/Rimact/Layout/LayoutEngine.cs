using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

// A flexbox subset. Measure turns constraints into desired sizes, arrange sets the final rects.
// Components, fragments and providers are transparent: their host children join the parent container
internal static class LayoutEngine
{
    private static int s_gen;

    public static void Run(Host host, Rect rect)
    {
        s_gen++;
        host.Natives.Clear();
        host.Overlays.Clear();
        var root = host.Root;
        if (root is null) return;

        root.Rect = rect;
        root.ClipRect = rect;
        var pendingOverlays = new List<(Fiber overlay, Rect anchor)>();
        var children = root.IsTransparent ? LayoutChildren(root, pendingOverlays, rect) : new List<Fiber> { root };
        ArrangeStack(children, rect, Align.Stretch, rect, false, false, host, pendingOverlays);

        // After the main tree, so overlays paint and hit-test on top
        for (int i = 0; i < pendingOverlays.Count; i++)
            ArrangeOverlay(pendingOverlays[i].overlay, pendingOverlays[i].anchor, rect, host, pendingOverlays);

        UnionTransparent(root);
        if (root.IsTransparent)
        {
            root.Rect = rect;
            root.ContentSize = rect.size;
        }
    }

    // Tallest root child at this width, margins included. Fill heights count as their content
    public static float NaturalHeight(Host host, float width)
    {
        s_gen++;
        var root = host.Root;
        if (root is null) return 0f;
        var children = root.IsTransparent ? LayoutChildren(root, null, default) : new List<Fiber> { root };
        float h = 0f;
        foreach (var c in children)
        {
            var m = c.Node.Layout.Margin;
            float ch = Measure(c, Mathf.Max(0, width - m.Horizontal), float.PositiveInfinity).y;
            if (!float.IsInfinity(ch) && !float.IsNaN(ch)) h = Mathf.Max(h, ch + m.Vertical);
        }
        return h;
    }

    #region Child flattening

    // Host children of f, looking through transparent fibers. Overlays found on the way go into overlays with anchor
    private static List<Fiber> LayoutChildren(Fiber f, List<(Fiber, Rect)>? overlays, Rect anchor)
    {
        var list = new List<Fiber>(f.Children.Count);
        Collect(f, list, overlays, anchor);
        return list;
    }

    private static void Collect(Fiber f, List<Fiber> list, List<(Fiber, Rect)>? overlays, Rect anchor)
    {
        foreach (var c in f.Children)
        {
            if (c.Node is OverlayNode)
            {
                overlays?.Add((c, anchor));
                continue;
            }
            if (c.IsTransparent) Collect(c, list, overlays, anchor);
            else list.Add(c);
        }
    }

    #endregion

    #region Measure

    // Outer size without margins. Cached per fiber for this pass, keyed by the available size
    public static Vector2 Measure(Fiber f, float availW, float availH)
    {
        if (f.MeasureGen != s_gen)
        {
            f.MeasureGen = s_gen;
            f.MeasureNext = 0;
            for (int i = 0; i < f.MeasureKeys.Length; i++) f.MeasureKeys[i] = new Vector4(float.NaN, 0, 0, 0);
        }
        for (int i = 0; i < f.MeasureKeys.Length; i++)
        {
            var k = f.MeasureKeys[i];
            if (k.x.Equals(availW) && k.y.Equals(availH)) return new Vector2(k.z, k.w);
        }
        var r = MeasureUncached(f, availW, availH);
        f.MeasureKeys[f.MeasureNext] = new Vector4(availW, availH, r.x, r.y);
        f.MeasureNext = (f.MeasureNext + 1) % f.MeasureKeys.Length;
        return r;
    }

    private static Vector2 MeasureUncached(Fiber f, float availW, float availH)
    {
        var L = f.Node.Layout;
        // Fill counts as Auto here. As the available size it would inflate an Auto parent and push its siblings out
        float? fw = L.Width.Resolve(availW);
        float? fh = L.Height.Resolve(availH);
        if (L.AspectRatio is { } ratio && ratio > 0f)
        {
            if (fw is null && fh is { } byHeight) fw = byHeight * ratio;
            else if (fw is null && !float.IsInfinity(availW)) fw = availW;
            if (fh is null && fw is { } byWidth) fh = byWidth / ratio;
        }
        // Bounded by the max size, so wrapping text or a wrapping box measures the lines it will get
        float boundW = fw ?? Mathf.Min(availW, L.MaxWidth);
        float boundH = fh ?? Mathf.Min(availH, L.MaxHeight);
        Vector2 size;
        switch (f.Node)
        {
            case TextNode t:
                {
                    float maxW = boundW - L.Padding.Horizontal;
                    float wrap = t.Wrap && !float.IsInfinity(maxW) ? Mathf.Max(1, maxW) : 0;
                    var s = MeasureText(f, t, wrap);
                    float w = s.x;
                    if (!t.Wrap && t.Truncate && !float.IsInfinity(maxW)) w = Mathf.Min(w, Mathf.Max(0, maxW));
                    if (t.Wrap && !float.IsInfinity(maxW)) w = Mathf.Min(w, maxW);
                    size = new Vector2(fw ?? w + L.Padding.Horizontal, fh ?? s.y + L.Padding.Vertical);
                    break;
                }
            case BoxNode b:
                {
                    if (fw.HasValue && fh.HasValue)
                    {
                        size = new Vector2(fw.Value, fh.Value);
                        break;
                    }
                    float innerW = boundW - L.Padding.Horizontal;
                    float innerH = boundH - L.Padding.Vertical;
                    var content = MeasureContent(f, b, Mathf.Max(0, innerW), Mathf.Max(0, innerH));
                    // A scroll container measures min(content, MaxHeight), or 0 without MaxHeight.
                    // Its real size comes from Grow, Height or Fill
                    if (L.ScrollY.HasValue && b.Axis == Axis.Column)
                        content.y = float.IsInfinity(L.MaxHeight) ? 0 : Mathf.Min(content.y + L.Padding.Vertical, L.MaxHeight) - L.Padding.Vertical;
                    if (L.ScrollX.HasValue && b.Axis == Axis.Row)
                        content.x = float.IsInfinity(L.MaxWidth) ? 0 : Mathf.Min(content.x + L.Padding.Horizontal, L.MaxWidth) - L.Padding.Horizontal;
                    size = new Vector2(fw ?? content.x + L.Padding.Horizontal, fh ?? content.y + L.Padding.Vertical);
                    break;
                }
            case ImageNode:
                size = new Vector2(fw ?? 24f, fh ?? 24f);
                break;
            default:
                size = new Vector2(fw ?? 0f, fh ?? 0f);
                break;
        }
        return new Vector2(
            Mathf.Clamp(size.x, L.MinWidth, Mathf.Max(L.MinWidth, L.MaxWidth)),
            Mathf.Clamp(size.y, L.MinHeight, Mathf.Max(L.MinHeight, L.MaxHeight)));
    }

    // Like Size.Resolve, but Fill takes the available size when it is finite
    private static float? Fixed(Size s, float avail) =>
        s.Kind == SizeKind.Fill ? (float.IsInfinity(avail) ? null : avail) : s.Resolve(avail);

    private static Vector2 MeasureText(Fiber f, TextNode t, float wrapWidth)
    {
        var style = t.Style;
        if (f.MeasuredText == t.Text && f.MeasuredStyle.Font == style.Font && f.MeasuredStyle.Size == style.Size &&
            f.MeasuredStyle.Bold == style.Bold && f.MeasuredWidth.Equals(wrapWidth)) return f.MeasuredSize;
        var s = f.Host.Backend.MeasureText(t.Text, style, wrapWidth);
        f.MeasuredText = t.Text;
        f.MeasuredStyle = style;
        f.MeasuredWidth = wrapWidth;
        f.MeasuredSize = s;
        return s;
    }

    private static Vector2 MeasureContent(Fiber f, BoxNode b, float innerW, float innerH)
    {
        var children = LayoutChildren(f, null, default);
        if (children.Count == 0) return Vector2.zero;
        if (b.Axis == Axis.Stack)
        {
            float w = 0, h = 0;
            foreach (var c in children)
            {
                var m = c.Node.Layout.Margin;
                var s = Measure(c, innerW - m.Horizontal, innerH - m.Vertical);
                w = Mathf.Max(w, s.x + m.Horizontal);
                h = Mathf.Max(h, s.y + m.Vertical);
            }
            return new Vector2(w, h);
        }
        bool row = b.Axis == Axis.Row;
        float innerMain = row ? innerW : innerH;
        if (b.Layout.ScrollY.HasValue && !row || b.Layout.ScrollX.HasValue && row) innerMain = float.PositiveInfinity;
        var lines = new List<FlexLine>(1);
        Plan(b, children, innerMain, row ? innerH : innerW, grow: false, lines);
        float main = 0, cross = 0;
        foreach (var line in lines)
        {
            main = Mathf.Max(main, line.Main);
            cross += line.Cross;
        }
        cross += LineGap(b.Layout) * (lines.Count - 1);
        return row ? new Vector2(main, cross) : new Vector2(cross, main);
    }

    #endregion

    #region Flex planning

    private struct ChildPlan
    {
        public Fiber F;
        public float Main, Cross, MarginMain, MarginCross;
        public Align Align;
    }

    private struct FlexLine
    {
        public int Start, End;
        // Main includes gaps and margins, Cross includes margins
        public float Main, Cross;
    }

    private static float LineGap(LayoutProps L) => L.LineGap ?? L.Gap;

    private static List<ChildPlan> Plan(BoxNode b, List<Fiber> children, float innerMain, float innerCross, bool grow, List<FlexLine> lines)
    {
        bool row = b.Axis == Axis.Row;
        bool wrap = b.Layout.Wrap && !float.IsInfinity(innerMain);
        float gap = b.Layout.Gap;
        var plan = new List<ChildPlan>(children.Count);
        var grows = new float[children.Count];

        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            var L = c.Node.Layout;
            var m = L.Margin;
            float mMain = row ? m.Horizontal : m.Vertical;
            float mCross = row ? m.Vertical : m.Horizontal;
            var align = L.AlignSelf ?? b.Layout.Align;
            Size mainSize = row ? L.Width : L.Height;
            Size crossSize = row ? L.Height : L.Width;
            float availCross = innerCross - mCross;
            float? fixedCross = Fixed(crossSize, innerCross);
            float g = L.Grow;
            float basis;
            if (mainSize.Kind == SizeKind.Fill)
            {
                g = Mathf.Max(g, 1f);
                basis = 0;
            }
            else
            {
                float? fixedMain = mainSize.Resolve(innerMain);
                if (fixedMain.HasValue) basis = fixedMain.Value;
                else
                {
                    float measureCross = fixedCross ?? availCross;
                    var s = row ? Measure(c, innerMain - mMain, measureCross) : Measure(c, measureCross, innerMain - mMain);
                    basis = row ? s.x : s.y;
                }
            }
            grows[i] = g;
            plan.Add(new ChildPlan { F = c, Main = ClampMain(L, row, basis), MarginMain = mMain, MarginCross = mCross, Align = align });
        }

        // Break before a child that would overflow. Each line keeps at least one child
        lines.Clear();
        int lineStart = 0;
        float lineUsed = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            float size = plan[i].Main + plan[i].MarginMain;
            if (wrap && i > lineStart && lineUsed + gap + size > innerMain + 0.01f)
            {
                lines.Add(new FlexLine { Start = lineStart, End = i, Main = lineUsed });
                lineStart = i;
                lineUsed = size;
            }
            else lineUsed += (i > lineStart ? gap : 0) + size;
        }
        if (plan.Count > 0) lines.Add(new FlexLine { Start = lineStart, End = plan.Count, Main = lineUsed });

        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            if (!float.IsInfinity(innerMain)) FlexLineMain(plan, grows, line, innerMain - line.Main, row, grow);

            // Cross sizes come after the main sizes are final, since wrapped text height depends on width
            float used = gap * (line.End - line.Start - 1), lineCross = 0;
            for (int i = line.Start; i < line.End; i++)
            {
                var p = plan[i];
                var L = p.F.Node.Layout;
                Size crossSize = row ? L.Height : L.Width;
                float availCross = innerCross - p.MarginCross;
                // While measuring (grow false), Fill and Stretch take their content size. Otherwise an Auto-height
                // Row in a Column would grow to the Column's full height. A wrapped line stretches to the line instead
                float? fixedCross = grow ? Fixed(crossSize, innerCross) : crossSize.Resolve(innerCross);
                float cross;
                if (fixedCross.HasValue) cross = fixedCross.Value;
                else if (grow && !wrap && p.Align == Align.Stretch && !float.IsInfinity(availCross)) cross = availCross;
                else
                {
                    var s = row ? Measure(p.F, p.Main, availCross) : Measure(p.F, availCross, p.Main);
                    cross = row ? s.y : s.x;
                    if (!float.IsInfinity(availCross)) cross = Mathf.Min(cross, Mathf.Max(availCross, 0));
                }
                p.Cross = ClampCross(L, row, cross);
                plan[i] = p;
                used += p.Main + p.MarginMain;
                lineCross = Mathf.Max(lineCross, p.Cross + p.MarginCross);
            }
            line.Main = used;
            line.Cross = lineCross;
            lines[li] = line;

            if (!wrap || !grow) continue;
            for (int i = line.Start; i < line.End; i++)
            {
                var p = plan[i];
                var L = p.F.Node.Layout;
                if (p.Align != Align.Stretch || (row ? L.Height : L.Width).Kind != SizeKind.Auto) continue;
                p.Cross = ClampCross(L, row, lineCross - p.MarginCross);
                plan[i] = p;
            }
        }
        return plan;
    }

    // Grows or shrinks the line's children to fill the free space
    private static void FlexLineMain(List<ChildPlan> plan, float[] grows, FlexLine line, float free, bool row, bool grow)
    {
        float growSum = 0, shrinkSum = 0;
        for (int i = line.Start; i < line.End; i++)
        {
            growSum += grows[i];
            shrinkSum += plan[i].F.Node.Layout.Shrink * plan[i].Main;
        }
        if (grow && free > 0 && growSum > 0)
        {
            for (int i = line.Start; i < line.End; i++)
            {
                if (grows[i] <= 0) continue;
                var p = plan[i];
                p.Main = ClampMain(p.F.Node.Layout, row, p.Main + free * grows[i] / growSum);
                plan[i] = p;
            }
        }
        else if (free < 0 && shrinkSum > 0)
        {
            for (int i = line.Start; i < line.End; i++)
            {
                var p = plan[i];
                float k = p.F.Node.Layout.Shrink * p.Main;
                if (k <= 0) continue;
                p.Main = ClampMain(p.F.Node.Layout, row, Mathf.Max(0, p.Main + free * k / shrinkSum));
                plan[i] = p;
            }
        }
    }

    private static float ClampCross(LayoutProps L, bool row, float v) => row
        ? Mathf.Clamp(v, L.MinHeight, Mathf.Max(L.MinHeight, L.MaxHeight))
        : Mathf.Clamp(v, L.MinWidth, Mathf.Max(L.MinWidth, L.MaxWidth));

    private static float ClampMain(LayoutProps L, bool row, float v) => row
        ? Mathf.Clamp(v, L.MinWidth, Mathf.Max(L.MinWidth, L.MaxWidth))
        : Mathf.Clamp(v, L.MinHeight, Mathf.Max(L.MinHeight, L.MaxHeight));

    #endregion

    #region Arrange

    private static void Arrange(Fiber f, Rect rect, Rect clip, bool disabled, bool inOverlay, Host host, List<(Fiber, Rect)> overlays)
    {
        var L = f.Node.Layout;
        f.Rect = rect;
        f.ClipRect = clip;
        f.DisabledTree = disabled || f.Node.Disabled;
        f.InOverlay = inOverlay;
        f.ContentSize = rect.size;

        switch (f.Node)
        {
            case NativeNode:
                host.Natives.Add(f);
                return;
            case BoxNode b:
                {
                    var inner = L.Padding.Shrink(rect);
                    var childClip = L.ClipsChildren ? Intersect(clip, ClipArea(f.Node, rect)) : clip;
                    var children = LayoutChildren(f, overlays, rect);
                    if (children.Count == 0) return;
                    if (b.Axis == Axis.Stack)
                    {
                        ArrangeStack(children, inner, b.Layout.Align, childClip, f.DisabledTree, inOverlay, host, overlays);
                        // Largest child content, so a scroll area wrapped in a Stack still reports its content size
                        var cs = rect.size;
                        foreach (var c in children) cs = Vector2.Max(cs, c.ContentSize);
                        f.ContentSize = cs;
                        return;
                    }
                    ArrangeFlex(f, b, children, inner, childClip, host, overlays);
                    return;
                }
        }
    }

    private static void ArrangeFlex(Fiber f, BoxNode b, List<Fiber> children, Rect inner, Rect childClip, Host host, List<(Fiber, Rect)> overlays)
    {
        bool row = b.Axis == Axis.Row;
        float? scrollBy = row ? b.Layout.ScrollX : b.Layout.ScrollY;
        bool scroll = scrollBy.HasValue;
        float innerMain = scroll ? float.PositiveInfinity : (row ? inner.width : inner.height);
        float innerCross = row ? inner.height : inner.width;
        var lines = new List<FlexLine>(1);
        var plan = Plan(b, children, innerMain, innerCross, grow: true, lines);
        bool multi = b.Layout.Wrap && !float.IsInfinity(innerMain);
        float crossStart = row ? inner.y : inner.x;

        float maxMain = 0;
        float lineCursor = crossStart;
        foreach (var line in lines)
        {
            maxMain = Mathf.Max(maxMain, line.Main);
            float gap = b.Layout.Gap;
            float start = 0;
            if (!float.IsInfinity(innerMain))
            {
                float free = innerMain - line.Main;
                int count = line.End - line.Start;
                if (free > 0)
                {
                    switch (b.Layout.Justify)
                    {
                        case Justify.Center: start = free / 2; break;
                        case Justify.End: start = free; break;
                        case Justify.SpaceBetween when count > 1: gap += free / (count - 1); break;
                    }
                }
            }

            float lineCross = multi ? line.Cross : innerCross;
            float cursor = (row ? inner.x : inner.y) + start - (scrollBy ?? 0f);
            for (int i = line.Start; i < line.End; i++)
            {
                var p = plan[i];
                var m = p.F.Node.Layout.Margin;
                float availCross = lineCross - p.MarginCross;
                float crossOff = p.Align switch
                {
                    Align.Center => (availCross - p.Cross) / 2,
                    Align.End => availCross - p.Cross,
                    _ => 0,
                };
                Rect r = row
                    ? new Rect(cursor + m.Left, lineCursor + m.Top + crossOff, p.Main, p.Cross)
                    : new Rect(lineCursor + m.Left + crossOff, cursor + m.Top, p.Cross, p.Main);
                r.position += p.F.Node.Layout.Offset;
                Arrange(p.F, r, childClip, f.DisabledTree, f.InOverlay, host, overlays);
                cursor += p.Main + p.MarginMain + gap;
            }
            lineCursor += line.Cross + LineGap(b.Layout);
        }

        var L = b.Layout;
        float crossUsed = Mathf.Max(0, lineCursor - crossStart - LineGap(L));
        f.ContentSize = (row, multi) switch
        {
            (true, true) => new Vector2(maxMain + L.Padding.Horizontal, crossUsed + L.Padding.Vertical),
            (false, true) => new Vector2(crossUsed + L.Padding.Horizontal, maxMain + L.Padding.Vertical),
            (true, false) => new Vector2(maxMain + L.Padding.Horizontal, f.Rect.height),
            _ => new Vector2(f.Rect.width, maxMain + L.Padding.Vertical),
        };
    }

    private static void ArrangeStack(List<Fiber> children, Rect inner, Align align, Rect clip, bool disabled, bool inOverlay, Host host, List<(Fiber, Rect)> overlays)
    {
        foreach (var c in children)
        {
            var L = c.Node.Layout;
            var m = L.Margin;
            var a = L.AlignSelf ?? align;
            float availW = inner.width - m.Horizontal, availH = inner.height - m.Vertical;
            float? fw = Fixed(L.Width, inner.width);
            float? fh = Fixed(L.Height, inner.height);
            Vector2 s = default;
            if ((!fw.HasValue || !fh.HasValue) && a != Align.Stretch) s = Measure(c, fw ?? availW, fh ?? availH);
            float w = fw ?? (a == Align.Stretch ? availW : Mathf.Min(s.x, availW));
            float h = fh ?? (a == Align.Stretch ? availH : Mathf.Min(s.y, availH));
            w = Mathf.Clamp(w, L.MinWidth, Mathf.Max(L.MinWidth, L.MaxWidth));
            h = Mathf.Clamp(h, L.MinHeight, Mathf.Max(L.MinHeight, L.MaxHeight));
            float x = inner.x + m.Left + AlignOffset(a, availW, w);
            float y = inner.y + m.Top + AlignOffset(a, availH, h);
            var r = new Rect(x + L.Offset.x, y + L.Offset.y, w, h);
            Arrange(c, r, clip, disabled, inOverlay, host, overlays);
        }
    }

    private static float AlignOffset(Align a, float avail, float size) => a switch
    {
        Align.Center => (avail - size) / 2,
        Align.End => avail - size,
        _ => 0,
    };

    private static void ArrangeOverlay(Fiber ov, Rect anchor, Rect hostRect, Host host, List<(Fiber, Rect)> overlays)
    {
        var node = (OverlayNode)ov.Node;
        ov.AnchorRect = anchor;
        ov.InOverlay = true;
        ov.ClipRect = hostRect;
        var children = LayoutChildren(ov, overlays, anchor);
        host.Overlays.Add(ov);
        if (children.Count == 0)
        {
            ov.Rect = new Rect(anchor.x, anchor.yMax, 0, 0);
            return;
        }
        if (node.Placement == Placement.Fill && node.FillAlign is { } align)
        {
            var only = children[0];
            var ol = only.Node.Layout;
            var om = ol.Margin;
            float aw = Mathf.Max(0f, hostRect.width - om.Horizontal), ah = Mathf.Max(0f, hostRect.height - om.Vertical);
            var size = Measure(only, Fixed(ol.Width, aw) ?? aw, Fixed(ol.Height, ah) ?? ah);
            size = Vector2.Min(size, new Vector2(aw, ah));
            var placed = new Rect(hostRect.x + om.Left + (aw - size.x) * align.x, hostRect.y + om.Top + (ah - size.y) * align.y, size.x, size.y);
            ov.Rect = placed;
            Arrange(only, placed, hostRect, false, true, host, overlays);
            return;
        }
        if (node.Placement == Placement.Fill)
        {
            ArrangeStack(children, hostRect, Align.Stretch, hostRect, false, true, host, overlays);
            var area = children[0].Rect;
            for (int i = 1; i < children.Count; i++) area = Union(area, children[i].Rect);
            ov.Rect = area;
            return;
        }
        var c = children[0];
        var L = c.Node.Layout;
        var s = Measure(c, Fixed(L.Width, hostRect.width) ?? hostRect.width, Fixed(L.Height, hostRect.height) ?? hostRect.height);
        float off = node.SideOffset;
        Vector2 pos = node.Placement switch
        {
            Placement.Above => new Vector2(anchor.x, anchor.y - off - s.y),
            Placement.Right => new Vector2(anchor.xMax + off, anchor.y),
            Placement.Left => new Vector2(anchor.x - off - s.x, anchor.y),
            _ => new Vector2(anchor.x, anchor.yMax + off),
        };
        // Flip to the other side on overflow if it fits there, then clamp
        if (node.Placement == Placement.Below && pos.y + s.y > hostRect.yMax && anchor.y - off - s.y >= hostRect.y) pos.y = anchor.y - off - s.y;
        if (node.Placement == Placement.Above && pos.y < hostRect.y && anchor.yMax + off + s.y <= hostRect.yMax) pos.y = anchor.yMax + off;
        if (node.Placement == Placement.Right && pos.x + s.x > hostRect.xMax && anchor.x - off - s.x >= hostRect.x) pos.x = anchor.x - off - s.x;
        if (node.Placement == Placement.Left && pos.x < hostRect.x && anchor.xMax + off + s.x <= hostRect.xMax) pos.x = anchor.xMax + off;
        pos.x = Mathf.Clamp(pos.x, hostRect.x, Mathf.Max(hostRect.x, hostRect.xMax - s.x));
        pos.y = Mathf.Clamp(pos.y, hostRect.y, Mathf.Max(hostRect.y, hostRect.yMax - s.y));
        var r = new Rect(pos, s);
        ov.Rect = r;
        Arrange(c, r, hostRect, false, true, host, overlays);
    }

    #endregion

    #region Translate

    // Moves a laid-out subtree as a changed Offset would. False means a full layout has to run instead.
    // An overlay inside may flip or clamp differently and overlay content ignores Offset, so both refuse
    public static bool Translate(Fiber f, Vector2 delta)
    {
        if (NearestHostAncestor(f) is { Node: OverlayNode }) return false;
        if (!TranslateTree(f, delta, f.ClipRect)) return false;
        UpdateTransparentAncestors(f);
        return true;
    }

    // Same for a changed ScrollY or ScrollX, which moves only the box's children
    public static bool TranslateChildren(Fiber box, Vector2 delta)
    {
        var overlays = new List<(Fiber, Rect)>();
        var children = LayoutChildren(box, overlays, default);
        if (overlays.Count > 0) return false;
        foreach (var c in children)
        {
            if (!TranslateTree(c, delta, c.ClipRect)) return false;
            UpdateTransparentAncestors(c);
        }
        return true;
    }

    private static bool TranslateTree(Fiber f, Vector2 delta, Rect clip)
    {
        if (f.Node is OverlayNode) return false;
        if (f.IsTransparent)
        {
            foreach (var c in f.Children)
                if (!TranslateTree(c, delta, clip)) return false;
            return true;
        }
        f.Rect = new Rect(f.Rect.position + delta, f.Rect.size);
        f.ClipRect = clip;
        var L = f.Node.Layout;
        var childClip = f.Node is BoxNode && L.ClipsChildren ? Intersect(clip, ClipArea(f.Node, f.Rect)) : clip;
        foreach (var c in f.Children)
            if (!TranslateTree(c, delta, childClip)) return false;
        return true;
    }

    // Inside the border, so content scrolled to the edge doesn't paint over it
    internal static Rect ClipArea(Node n, Rect rect)
    {
        float w = n is BoxNode { Normal: { Border: not null } v } ? v.BorderWidth ?? 1f : 0f;
        return w > 0f ? new Rect(rect.x + w, rect.y + w, Mathf.Max(0f, rect.width - 2f * w), Mathf.Max(0f, rect.height - 2f * w)) : rect;
    }

    private static Fiber? NearestHostAncestor(Fiber f)
    {
        for (var p = f.Parent; p != null; p = p.Parent)
            if (!p.IsTransparent) return p;
        return null;
    }

    private static void UpdateTransparentAncestors(Fiber f)
    {
        var top = f;
        while (top.Parent is { IsTransparent: true, Parent: not null } p) top = p;
        UnionTransparent(top);
    }

    // A transparent fiber's rect is the union of its host children, for UseLayout
    private static Rect? UnionTransparent(Fiber f)
    {
        Rect? acc = null;
        foreach (var c in f.Children)
        {
            if (c.Node is OverlayNode)
            {
                UnionTransparent(c);
                continue;
            }
            var r = UnionTransparent(c);
            if (r.HasValue) acc = acc.HasValue ? Union(acc.Value, r.Value) : r;
        }
        if (!f.IsTransparent || f.Parent is null) return f.Rect;
        if (acc.HasValue)
        {
            f.Rect = acc.Value;
            // One host child passes its content size through, e.g. for ScrollArea
            var hostChild = SingleHostChild(f);
            f.ContentSize = hostChild?.ContentSize ?? acc.Value.size;
        }
        return acc;
    }

    private static Fiber? SingleHostChild(Fiber f)
    {
        var list = LayoutChildren(f, null, default);
        return list.Count == 1 ? list[0] : null;
    }

    private static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    public static Rect Intersect(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
        return Rect.MinMaxRect(x1, y1, Mathf.Max(x1, x2), Mathf.Max(y1, y2));
    }

    #endregion
}
