using UnityEngine;
using static Rimact.Elements;

namespace Rimact.Components
{
    /// <summary>The mark in front of a choice in a list: round where one is picked, square where several can be.</summary>
    internal static class ChoiceMark
    {
        public static CanvasNode Radio(Theme t, bool on) => Mark(t, on, true);

        public static CanvasNode Check(Theme t, bool on) => Mark(t, on, false);

        private static CanvasNode Mark(Theme t, bool on, bool round)
        {
            float size = t.RadioSize;
            return Canvas((p, r) =>
            {
                var well = new Rect(r.x, r.y, size, size);
                var dot = new Rect(well.x + 4, well.y + 4, size - 8, size - 8);
                if (!round)
                {
                    p.DrawRecessed(well, t.Sunken, t, 1f);
                    if (on) p.DrawRaised(dot, t.Indicator, t, 1f);
                    return;
                }
                // A bevel has corners, so the round well gets a lighter ring inside its outline instead
                p.FillRounded(well, t.Sunken, size / 2f);
                p.StrokeRounded(new Rect(well.x + 1, well.y + 1, size - 2, size - 2), Theme.Lighten(t.Sunken, 0.07f), 1f, size / 2f - 1);
                p.StrokeRounded(well, t.Outline, 1f, size / 2f);
                if (!on) return;
                p.FillRounded(dot, t.Indicator, dot.width / 2f);
                p.StrokeRounded(dot, t.Outline, 1f, dot.width / 2f);
            }).Size(size, size);
        }
    }
}
