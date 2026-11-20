using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Loading placeholder with a sweeping band. Size it like the content it stands for.</summary>
    internal sealed record Skeleton : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var fill = Theme.WithAlpha(t.Foreground, 0.06f);
            var band = Theme.WithAlpha(t.Foreground, 0.05f);
            var clear = Theme.WithAlpha(band, 0f);
            return Canvas((p, r) =>
            {
                p.FillRect(r, fill);
                if (host.ReduceMotion || r.width <= 0f) return;
                float w = Mathf.Max(32f, r.width * 0.4f);
                float x = r.x - w + (r.width + w) * (host.Backend.RealTime / 1.6f % 1f);
                var left = LayoutEngine.Intersect(new Rect(x, r.y, w / 2, r.height), r);
                var right = LayoutEngine.Intersect(new Rect(x + w / 2, r.y, w / 2, r.height), r);
                if (left.width > 0f) p.FillGradient(left, clear, band, GradientDirection.Horizontal);
                if (right.width > 0f) p.FillGradient(right, band, clear, GradientDirection.Horizontal);
            }).Height(14f);
        }
    }

    /// <summary>Small busy indicator.</summary>
    internal sealed record Spinner : UIComponent
    {
        public float Size { get; init; } = 16f;
        public Color? Color { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var color = Color ?? t.SecondaryForeground;
            return Canvas((p, r) =>
            {
                const int spokes = 8;
                int head = host.ReduceMotion ? 0 : (int)(host.Backend.RealTime * 10f) % spokes;
                var c = r.center;
                float outer = Mathf.Min(r.width, r.height) / 2f, inner = outer * 0.45f;
                float thick = Mathf.Max(1.5f, outer * 0.24f);
                for (int i = 0; i < spokes; i++)
                {
                    float a = i * (2f * Mathf.PI / spokes);
                    var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                    int behind = (head - i + spokes) % spokes;
                    p.DrawLine(c + dir * inner, c + dir * outer, Theme.WithAlpha(color, color.a * (1f - behind / (float)spokes * 0.8f)), thick);
                }
            }).Size(Size, Size).AlignSelf(Align.Center);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Skeleton Skeleton() => new();

        public static Components.Spinner Spinner(float size = 16f) => new() { Size = size };
    }
}
