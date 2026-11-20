using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

internal readonly record struct PaintVertex(Vector2 Pos, Color Color);

// Triangle lists for shapes the GUI backend has no primitive for. Methods append to tris, three vertices per triangle.
// Engine-free, so it is unit-tested
internal static class Geometry
{
    public static int CornerSegments(float radius) => radius <= 0f ? 0 : radius <= 2.5f ? 3 : radius <= 6f ? 5 : 8;

    // Clockwise from the top-left. Always 4 * (segments + 1) points, so two paths with equal segments pair up for Band
    public static void RoundedPath(Rect r, float radius, int segments, List<Vector2> pts)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) / 2f);
        Corner(pts, new Vector2(r.xMin + radius, r.yMin + radius), radius, 180f, segments);
        Corner(pts, new Vector2(r.xMax - radius, r.yMin + radius), radius, 270f, segments);
        Corner(pts, new Vector2(r.xMax - radius, r.yMax - radius), radius, 0f, segments);
        Corner(pts, new Vector2(r.xMin + radius, r.yMax - radius), radius, 90f, segments);
    }

    private static void Corner(List<Vector2> pts, Vector2 c, float radius, float startDeg, int segments)
    {
        // GUI y points down, so growing angles run clockwise on screen
        for (int i = 0; i <= segments; i++)
        {
            float a = (startDeg + 90f * (segments == 0 ? 0 : (float)i / segments)) * Mathf.Deg2Rad;
            pts.Add(new Vector2(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius));
        }
    }

    // Convex polygons only
    public static void Fan(List<Vector2> poly, Color c, List<PaintVertex> tris)
    {
        for (int i = 1; i + 1 < poly.Count; i++)
        {
            tris.Add(new PaintVertex(poly[0], c));
            tris.Add(new PaintVertex(poly[i], c));
            tris.Add(new PaintVertex(poly[i + 1], c));
        }
    }

    // Band between two closed outlines with the same point count
    public static void Band(List<Vector2> outer, Color outerColor, List<Vector2> inner, Color innerColor, List<PaintVertex> tris)
    {
        int n = Mathf.Min(outer.Count, inner.Count);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            tris.Add(new PaintVertex(outer[i], outerColor));
            tris.Add(new PaintVertex(outer[j], outerColor));
            tris.Add(new PaintVertex(inner[j], innerColor));
            tris.Add(new PaintVertex(outer[i], outerColor));
            tris.Add(new PaintVertex(inner[j], innerColor));
            tris.Add(new PaintVertex(inner[i], innerColor));
        }
    }

    public static void FillRounded(Rect r, float radius, Color c, List<PaintVertex> tris)
    {
        var pts = new List<Vector2>();
        RoundedPath(r, radius, CornerSegments(radius), pts);
        Fan(pts, c, tris);
    }

    // The ring lies inside r
    public static void StrokeRounded(Rect r, float radius, float thickness, Color c, List<PaintVertex> tris)
    {
        thickness = Mathf.Min(thickness, Mathf.Min(r.width, r.height) / 2f);
        if (thickness <= 0f) return;
        int seg = Mathf.Max(CornerSegments(radius), 1);
        var outer = new List<Vector2>();
        var inner = new List<Vector2>();
        RoundedPath(r, radius, seg, outer);
        RoundedPath(Inset(r, thickness), Mathf.Max(0f, radius - thickness), seg, inner);
        Band(outer, c, inner, c, tris);
    }

    // Solid core dropped by a third of the blur, fading out to transparent over s.Size
    public static void Shadow(Rect r, float radius, Shadow s, List<PaintVertex> tris)
    {
        if (s.Size <= 0f || s.Color.a <= 0f) return;
        float drop = s.Size / 3f;
        var core = new Rect(r.x + s.Size * 0.25f, r.y + drop + s.Size * 0.25f, r.width - s.Size * 0.5f, r.height - s.Size * 0.5f);
        if (core.width <= 0f || core.height <= 0f) core = new Rect(r.center.x, r.center.y + drop, 0f, 0f);
        int seg = Mathf.Max(CornerSegments(radius + s.Size), 3);
        var inner = new List<Vector2>();
        var outer = new List<Vector2>();
        RoundedPath(core, radius, seg, inner);
        RoundedPath(Inset(core, -s.Size), radius + s.Size, seg, outer);
        Fan(inner, s.Color, tris);
        Band(outer, new Color(s.Color.r, s.Color.g, s.Color.b, 0f), inner, s.Color, tris);
    }

    public static void Gradient(Rect r, Color from, Color to, GradientDirection dir, List<PaintVertex> tris)
    {
        switch (dir)
        {
            case GradientDirection.Horizontal:
                Quad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax),
                    from, to, to, from, tris);
                break;
            case GradientDirection.HorizontalMirror:
                float mid = r.center.x;
                Quad(new Vector2(r.xMin, r.yMin), new Vector2(mid, r.yMin), new Vector2(mid, r.yMax), new Vector2(r.xMin, r.yMax),
                    from, to, to, from, tris);
                Quad(new Vector2(mid, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(mid, r.yMax),
                    to, from, from, to, tris);
                break;
            default:
                Quad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax),
                    from, from, to, to, tris);
                break;
        }
    }

    // Mitred bands inside r: light on the top and right, dark on the left and bottom
    public static void Bevel(Rect r, Color light, Color dark, float width, List<PaintVertex> tris)
    {
        float w = Mathf.Min(width, Mathf.Min(r.width, r.height) / 2f);
        if (w <= 0f) return;
        var tl = new Vector2(r.xMin, r.yMin);
        var tr = new Vector2(r.xMax, r.yMin);
        var br = new Vector2(r.xMax, r.yMax);
        var bl = new Vector2(r.xMin, r.yMax);
        var itl = new Vector2(r.xMin + w, r.yMin + w);
        var itr = new Vector2(r.xMax - w, r.yMin + w);
        var ibr = new Vector2(r.xMax - w, r.yMax - w);
        var ibl = new Vector2(r.xMin + w, r.yMax - w);
        Quad(tl, tr, itr, itl, light, light, light, light, tris);
        Quad(tr, br, ibr, itr, light, light, light, light, tris);
        Quad(br, bl, ibl, ibr, dark, dark, dark, dark, tris);
        Quad(bl, tl, itl, ibl, dark, dark, dark, dark, tris);
    }

    public static void Line(Vector2 a, Vector2 b, float thickness, Color c, List<PaintVertex> tris)
    {
        var d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        var n = new Vector2(-d.y, d.x) / len * (thickness / 2f);
        var e = d / len * (thickness / 2f); // extend the ends so joined segments meet without a notch
        Quad(a - e + n, b + e + n, b + e - n, a - e - n, c, c, c, c, tris);
    }

    // 45 degree stripes, one every spacing px along x, clipped to r
    public static void Stripes(Rect r, float spacing, float width, float phase, Color c, List<PaintVertex> tris)
    {
        if (spacing <= 0f || width <= 0f || r.width <= 0f || r.height <= 0f) return;
        float h = r.height;
        float start = r.xMin - h - spacing + Mathf.Repeat(phase, spacing);
        var poly = new List<Vector2>(8);
        var clipped = new List<Vector2>(8);
        for (float x = start; x < r.xMax; x += spacing)
        {
            poly.Clear();
            poly.Add(new Vector2(x, r.yMax));
            poly.Add(new Vector2(x + width, r.yMax));
            poly.Add(new Vector2(x + width + h, r.yMin));
            poly.Add(new Vector2(x + h, r.yMin));
            ClipToRect(poly, r, clipped);
            if (clipped.Count >= 3) Fan(clipped, c, tris);
        }
    }

    // Sutherland-Hodgman, for convex polygons
    public static void ClipToRect(List<Vector2> poly, Rect r, List<Vector2> result)
    {
        var a = new List<Vector2>(poly);
        var b = new List<Vector2>(poly.Count + 4);
        ClipEdge(a, b, p => p.x >= r.xMin, (p, q) => Lerp(p, q, (r.xMin - p.x) / (q.x - p.x)));
        ClipEdge(b, a, p => p.x <= r.xMax, (p, q) => Lerp(p, q, (r.xMax - p.x) / (q.x - p.x)));
        ClipEdge(a, b, p => p.y >= r.yMin, (p, q) => Lerp(p, q, (r.yMin - p.y) / (q.y - p.y)));
        ClipEdge(b, a, p => p.y <= r.yMax, (p, q) => Lerp(p, q, (r.yMax - p.y) / (q.y - p.y)));
        result.Clear();
        result.AddRange(a);
    }

    private static void ClipEdge(List<Vector2> input, List<Vector2> output, System.Func<Vector2, bool> inside, System.Func<Vector2, Vector2, Vector2> cross)
    {
        output.Clear();
        for (int i = 0; i < input.Count; i++)
        {
            var p = input[i];
            var q = input[(i + 1) % input.Count];
            bool pin = inside(p), qin = inside(q);
            if (pin) output.Add(p);
            if (pin != qin) output.Add(cross(p, q));
        }
    }

    private static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

    private static void Quad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color c0, Color c1, Color c2, Color c3, List<PaintVertex> tris)
    {
        tris.Add(new PaintVertex(p0, c0));
        tris.Add(new PaintVertex(p1, c1));
        tris.Add(new PaintVertex(p2, c2));
        tris.Add(new PaintVertex(p0, c0));
        tris.Add(new PaintVertex(p2, c2));
        tris.Add(new PaintVertex(p3, c3));
    }

    // Negative d grows it
    public static Rect Inset(Rect r, float d) => new(r.x + d, r.y + d, Mathf.Max(0f, r.width - 2 * d), Mathf.Max(0f, r.height - 2 * d));
}
