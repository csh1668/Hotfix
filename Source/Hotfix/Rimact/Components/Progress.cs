using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Progress bar for a value in 0..1. The fill eases to new values.</summary>
    internal sealed record Progress(float Value) : UIComponent
    {
        public float BarHeight { get; init; } = 8f;
        public Color? Color { get; init; }
        /// <summary>Splits the bar into this many parts with hairlines.</summary>
        public int Segments { get; init; }
        /// <summary>Moving stripes over the fill, to show work in progress.</summary>
        public bool Striped { get; init; }
        /// <summary>Ignores Value and sweeps a short fill across, for work of unknown length.</summary>
        public bool Indeterminate { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var backend = host.Backend;
            float shown = UseAnimated(Mathf.Clamp01(Value), 0.25f);
            bool indeterminate = Indeterminate;
            var color = Color ?? t.Bar;
            int segments = Segments;
            bool striped = Striped;
            float bevel = BarHeight >= 10f ? 2f : 1f;

            return Canvas((p, r) =>
            {
                p.DrawRecessed(r, t.Sunken, t, 1f);
                if (indeterminate)
                {
                    float w = r.width * 0.3f;
                    float phase = host.ReduceMotion ? 0.35f : backend.RealTime / 1.4f % 1f;
                    var sweep = LayoutEngine.Intersect(new Rect(r.x - w + (r.width + w) * phase, r.y, w, r.height), r);
                    if (sweep.width >= 1f) p.DrawRaised(sweep, color, t, bevel);
                }
                else if (shown > 0f)
                {
                    var fill = new Rect(r.x, r.y, Mathf.Max(Mathf.Min(r.width, 4f), r.width * shown), r.height);
                    if (striped)
                    {
                        p.FillRect(fill, color);
                        p.DrawStripes(fill, new UnityEngine.Color(0f, 0f, 0f, 0.16f), 10f, 4f, backend.RealTime * 14f);
                        var b = t.Raised(color, bevel);
                        p.DrawBevel(new Rect(fill.x + 1f, fill.y + 1f, fill.width - 2f, fill.height - 2f), b.Light, b.Dark, b.Width);
                        p.DrawBorder(fill, t.Outline, 1f);
                    }
                    else p.DrawRaised(fill, color, t, bevel);
                }
                for (int i = 1; i < segments; i++)
                    p.FillRect(new Rect(Mathf.Round(r.x + r.width * i / segments), r.y, 1f, r.height), t.Outline);
            }).Height(BarHeight);
        }
    }

    /// <summary>Icon centered in a small recessed square.</summary>
    internal sealed record IconWell(Icon Icon) : UIComponent
    {
        public Color? Tint { get; init; }
        public float Size { get; init; } = 24f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            float icon = Mathf.Round(Size * 0.67f);
            return Row(Image(Icon).Size(icon, icon).Tint(Tint ?? t.SecondaryForeground))
                .Size(Size, Size).Center().Bg(t.Sunken).Bevel(t.Recessed(t.Sunken, 1f)).Border(t.Outline).Radius(t.Radius);
        }
    }

    /// <summary>Label on the left, value on the right.</summary>
    internal sealed record ValueRow(string Label, string Value) : UIComponent
    {
        public Color? ValueColor { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Row(
                    Elements.Text(Label).Color(t.SecondaryForeground).Truncate().Grow().Shrink(),
                    Elements.Text(Value).Color(ValueColor ?? t.Foreground).Anchor(TextAlign.MiddleRight))
                .Height(24).Gap(t.Space(2)).Align(Align.Center);
        }
    }

    /// <summary>Row with an optional icon, a label, a 0..1 bar and the value as text.</summary>
    internal sealed record Meter(string Label, float Value, string ValueText) : UIComponent
    {
        public Color? Color { get; init; }
        public Icon? Icon { get; init; }
        /// <summary>Give rows in a list the same width so their bars line up.</summary>
        public float? LabelWidth { get; init; }
        public float ValueWidth { get; init; } = 48f;
        public int Segments { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var color = Color ?? t.Bar;
            var label = Elements.Text(Label).Color(t.SecondaryForeground).Truncate();
            return Row(
                    Icon is null ? null : new IconWell(Icon) { Tint = color, Size = 22f },
                    LabelWidth is { } w ? label.Width(w) : label.Shrink(),
                    new Progress(Value) { Color = color, Segments = Segments, BarHeight = 6f }.Grow().AlignSelf(Align.Center),
                    Elements.Text(ValueText).Color(t.Foreground).Anchor(TextAlign.MiddleRight).Width(ValueWidth))
                .Height(26).Gap(t.Space(2.5f)).Align(Align.Center);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Progress Progress(float value01) => new(value01);

        public static Components.IconWell IconWell(Icon icon, UnityEngine.Color? tint = null) => new(icon) { Tint = tint };

        public static Components.ValueRow ValueRow(string label, string value, UnityEngine.Color? valueColor = null) =>
            new(label, value) { ValueColor = valueColor };

        public static Components.Meter Meter(string label, float value01, string valueText, UnityEngine.Color? color = null) =>
            new(label, value01, valueText) { Color = color };
    }
}
