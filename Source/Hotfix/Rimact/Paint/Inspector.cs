using System.Globalization;
using UnityEngine;

namespace Rimact;

// Debug overlay: a stats bar, outlines on re-rendered components and details on the node under the pointer.
// Painted last while Host.InspectorOn is set
internal static class Inspector
{
    // How long a re-rendered component stays outlined
    public const float FlashSeconds = 0.6f;

    private static readonly Color Panel = new(0f, 0f, 0f, 0.78f);
    private static readonly Color Flash = new(1f, 0.78f, 0.2f, 1f);
    private static readonly Color Target = new(0.35f, 0.85f, 1f, 1f);
    private static readonly Color Owner = new(1f, 0.55f, 0.85f, 1f);

    public static void Paint(Host host, Vector2 pointer)
    {
        var b = host.Backend;
        var rect = host.Rect;
        float now = b.RealTime;
        b.PushClip(rect);
        try
        {
            if (host.Root is { } root) Flashes(b, root, now);

            var target = host.Dispatcher.HitTest(pointer);
            if (target != null)
            {
                b.FillRect(target.Rect, new Color(Target.r, Target.g, Target.b, 0.12f));
                b.DrawBorder(target.Rect, Target, 1f);
                var owner = OwnerOf(target);
                if (owner != null && owner.Rect != target.Rect) b.DrawBorder(owner.Rect, Owner, 1f);
                Tag(b, rect, pointer, Describe(target, owner));
            }

            var s = host.Stats;
            string line = string.Format(CultureInfo.InvariantCulture,
                "render {0:0.00} ms  layout {1:0.00} ms  paint {2:0.00} ms   fibers {3}  renders {4}  layouts {5} (skipped {6})",
                s.RenderMs, s.LayoutMs, s.PaintMs, s.Fibers, s.ComponentRenders, s.LayoutPasses, s.SkippedLayouts);
            var style = new TextStyle(FontSize.Tiny, TextAlign.MiddleLeft, false, true);
            float w = Mathf.Min(rect.width, b.MeasureText(line, style, 0f).x + 12f);
            var bar = new Rect(rect.xMax - w, rect.y, w, 18f);
            b.FillRect(bar, Panel);
            b.DrawText(new Rect(bar.x + 6, bar.y, bar.width - 8, bar.height), line, style, Color.white);
        }
        finally
        {
            b.PopClip();
        }
    }

    private static void Flashes(IBackend b, Fiber f, float now)
    {
        if (f.IsComponent && f.LastRender >= 0f && now - f.LastRender < FlashSeconds)
        {
            var r = HostRectOf(f);
            if (r.width > 0f && r.height > 0f)
                b.DrawBorder(r, new Color(Flash.r, Flash.g, Flash.b, 1f - (now - f.LastRender) / FlashSeconds), 1f);
        }
        foreach (var c in f.Children) Flashes(b, c, now);
    }

    private static Rect HostRectOf(Fiber f)
    {
        while (f.IsTransparent && f.Children.Count > 0) f = f.Children[0];
        return f.IsTransparent ? default : f.Rect;
    }

    private static Fiber? OwnerOf(Fiber f)
    {
        for (var p = f.Parent; p != null; p = p.Parent)
            if (p.IsComponent) return p;
        return null;
    }

    private static string Describe(Fiber target, Fiber? owner)
    {
        string node = target.Node.GetType().Name.Replace("Node", "");
        string comp = owner is null ? "" : Name(owner) + " › ";
        return string.Format(CultureInfo.InvariantCulture, "{0}{1}  {2:0}×{3:0}{4}",
            comp, node, target.Rect.width, target.Rect.height, owner is null ? "" : $"  renders {owner.RenderCount}");
    }

    internal static string Name(Fiber f) => f.Id switch
    {
        System.Type t => t.Name.Split('`')[0],
        _ => f.Id.ToString() ?? "?",
    };

    private static void Tag(IBackend b, Rect host, Vector2 pointer, string text)
    {
        var style = new TextStyle(FontSize.Tiny, TextAlign.MiddleLeft, false, true);
        float w = b.MeasureText(text, style, 0f).x + 12f;
        var r = new Rect(pointer.x + 14f, pointer.y + 14f, w, 18f);
        if (r.xMax > host.xMax) r.x = pointer.x - 14f - w;
        if (r.yMax > host.yMax) r.y = pointer.y - 14f - r.height;
        b.FillRect(r, Panel);
        b.DrawText(new Rect(r.x + 6, r.y, r.width - 8, r.height), text, style, Color.white);
    }
}
