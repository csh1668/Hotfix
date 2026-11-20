using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>One line of a <see cref="LineChart"/>. Color defaults to the theme's minerals in order.</summary>
    internal sealed record ChartSeries(string Name, IReadOnlyList<float> Values)
    {
        public Color? Color { get; init; }
    }

    /// <summary>Line chart with grid lines, value labels and a legend when there are several series.</summary>
    /// <remarks>No width of its own. Points are spread evenly, so series should share x positions.</remarks>
    /// <example><code>LineChart(ChartSeries("Frame ms", frameTimes)) with { YMin = 0 }</code></example>
    internal sealed record LineChart(IReadOnlyList<ChartSeries> Series) : UIComponent
    {
        /// <summary>Pins the bottom of the y range. Unpinned ends round out to steps of 1, 2, 2.5 or 5 x 10^n.</summary>
        public float? YMin { get; init; }
        public float? YMax { get; init; }
        public float PlotHeight { get; init; } = 140f;
        public int GridLines { get; init; } = 4;
        public Func<float, string>? Format { get; init; }
        /// <summary>Shades under each line.</summary>
        public bool Area { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var pointer = UseRef<Vector2?>(null);
            bool shade = Area;
            var colors = Series.Select((s, i) => s.Color ?? t.Minerals[i % t.Minerals.Length]).ToArray();
            var (lo, hi) = Range(Series.SelectMany(s => s.Values), YMin, YMax);
            var (min, max, grid) = Nice(lo, hi, Mathf.Max(1, GridLines), YMin.HasValue, YMax.HasValue);
            var format = Format ?? (v => v.ToString("0.##", CultureInfo.InvariantCulture));
            var series = Series;
            const float axisW = 44f;

            var plot = Canvas((p, r) =>
            {
                var area = new Rect(r.x + axisW, r.y + 4, r.width - axisW, r.height - 8);
                p.DrawRecessed(area, t.Sunken, t, 1f);
                var labelStyle = new TextStyle(FontSize.Small, TextAlign.MiddleRight, false, true, t.TextCaption);
                for (int g = 0; g <= grid; g++)
                {
                    float y = Mathf.Round(area.yMax - area.height * g / grid);
                    if (g > 0 && g < grid) p.FillRect(new Rect(area.x, y, area.width, 1f), t.Border);
                    p.DrawText(new Rect(r.x, y - 8, axisW - 6, 16), format(Mathf.Lerp(min, max, (float)g / grid)), labelStyle, t.MutedForeground);
                }
                for (int s = 0; s < series.Count; s++)
                {
                    var v = series[s].Values;
                    if (v.Count == 0) continue;
                    Vector2 At(int i) => new(
                        v.Count == 1 ? area.center.x : area.x + area.width * i / (v.Count - 1),
                        area.yMax - area.height * Mathf.InverseLerp(min, max, v[i]));
                    if (shade && v.Count > 1)
                    {
                        var tris = new List<Vector2>((v.Count - 1) * 6);
                        for (int i = 1; i < v.Count; i++)
                        {
                            Vector2 a = At(i - 1), b = At(i);
                            Vector2 a0 = new(a.x, area.yMax), b0 = new(b.x, area.yMax);
                            tris.Add(a); tris.Add(b); tris.Add(b0);
                            tris.Add(a); tris.Add(b0); tris.Add(a0);
                        }
                        p.FillTriangles(tris, Theme.WithAlpha(colors[s], 0.16f));
                    }
                    var stroke = new List<Vector2>((v.Count - 1) * 6);
                    for (int i = 1; i < v.Count; i++) Glyphs.AddSegment(stroke, At(i - 1), At(i), 1f);
                    if (stroke.Count > 0) p.FillTriangles(stroke, colors[s]);
                    var last = At(v.Count - 1);
                    p.FillRect(new Rect(last.x - 2.5f, last.y - 2.5f, 5f, 5f), colors[s]);
                }

                // Crosshair at the nearest x. The pointer is read at paint time
                int points = series.Count == 0 ? 0 : series.Max(x => x.Values.Count);
                if (pointer.Current is not { } at || points == 0 || !area.Contains(at)) return;
                int index = points == 1 ? 0 : Mathf.Clamp(Mathf.RoundToInt((at.x - area.x) / area.width * (points - 1)), 0, points - 1);
                float gx = points == 1 ? area.center.x : area.x + area.width * index / (points - 1);
                p.FillRect(new Rect(Mathf.Round(gx), area.y, 1f, area.height), t.BorderStrong);
                var lines = new List<string>();
                var swatches = new List<Color>();
                for (int s = 0; s < series.Count; s++)
                {
                    var v = series[s].Values;
                    if (index >= v.Count) continue;
                    float y = area.yMax - area.height * Mathf.InverseLerp(min, max, v[index]);
                    p.FillRect(new Rect(gx - 3f, y - 3f, 6f, 6f), colors[s]);
                    p.DrawBorder(new Rect(gx - 3f, y - 3f, 6f, 6f), t.Outline, 1f);
                    lines.Add($"{series[s].Name}: {format(v[index])}");
                    swatches.Add(colors[s]);
                }
                DrawTip(p, t, new Vector2(gx, at.y), r, lines, swatches);
            }).Height(PlotHeight)
                .OnPointerMove(e => pointer.Current = e.Position)
                .OnHover(over =>
                {
                    if (!over) pointer.Current = null;
                });

            if (Series.Count < 2) return plot;
            return Column(plot, Legend(t, Series.Select(s => s.Name).ToArray(), colors).Margin(new Edges(axisW, 0, 0, 0))).Gap(t.Space(1.5f));
        }

        // Widens lo..hi to round steps, about `ticks` of them. A fixed end stays put and the step count rounds up
        internal static (float min, float max, int steps) Nice(float lo, float hi, int ticks, bool fixedLo, bool fixedHi)
        {
            float raw = (hi - lo) / Mathf.Max(1, ticks);
            if (raw <= 0f || float.IsNaN(raw) || float.IsInfinity(raw)) return (lo, hi, Mathf.Max(1, ticks));
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            float norm = raw / mag;
            float step = (norm <= 1f ? 1f : norm <= 2f ? 2f : norm <= 2.5f ? 2.5f : norm <= 5f ? 5f : 10f) * mag;
            float min = fixedLo ? lo : Mathf.Floor(lo / step + 1e-4f) * step;
            float max = fixedHi ? hi : Mathf.Ceil(hi / step - 1e-4f) * step;
            if (fixedLo && fixedHi) return (min, max, Mathf.Max(1, ticks));
            return (min, max, Mathf.Max(1, Mathf.CeilToInt((max - min) / step - 1e-3f)));
        }

        internal static (float min, float max) Range(IEnumerable<float> values, float? yMin, float? yMax)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in values)
            {
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
            }
            if (lo > hi) (lo, hi) = (0f, 1f);
            lo = yMin ?? lo;
            hi = yMax ?? hi;
            if (hi - lo < 1e-6f) hi = lo + 1f;
            return (lo, hi);
        }

        // Tooltip box beside `at`, kept inside bounds
        internal static void DrawTip(IPainter p, Theme t, Vector2 at, Rect bounds, IReadOnlyList<string> lines, IReadOnlyList<Color> swatches)
        {
            if (lines.Count == 0) return;
            var style = new TextStyle(FontSize.Small, TextAlign.MiddleLeft, false, false, t.TextCaption);
            float textW = 0f;
            foreach (var line in lines) textW = Mathf.Max(textW, p.MeasureText(line, style, 0f).x);
            const float rowH = 16f, pad = 6f, swatch = 8f;
            var size = new Vector2(pad * 2 + swatch + 6f + textW, pad * 2 + rowH * lines.Count);
            float x = at.x + 12f;
            if (x + size.x > bounds.xMax) x = at.x - 12f - size.x;
            float y = Mathf.Clamp(at.y - size.y / 2f, bounds.y, Mathf.Max(bounds.y, bounds.yMax - size.y));
            var box = new Rect(Mathf.Max(bounds.x, x), y, size.x, size.y);
            p.DrawRaised(box, t.SurfaceRaised, t, 1f);
            for (int i = 0; i < lines.Count; i++)
            {
                float ry = box.y + pad + rowH * i;
                p.FillRect(new Rect(box.x + pad, ry + (rowH - swatch) / 2f, swatch, swatch), swatches[i]);
                p.DrawText(new Rect(box.x + pad + swatch + 6f, ry, textW + 2f, rowH), lines[i], style, t.Foreground);
            }
        }

        internal static BoxNode Legend(Theme t, string[] names, Color[] colors) =>
            Row(names.Select((n, i) => (Node?)Row(
                    Row().Size(10, 10).Bg(colors[i]).Bevel(t.Raised(colors[i], 1f)).Border(t.Outline),
                    Elements.Text(n).TextSize(t.TextCaption).Color(t.SecondaryForeground))
                .Gap(t.Space(1.5f)).Align(Align.Center))).Gap(t.Space(4));
    }

    /// <summary>Bar chart with labels underneath. Bars start at 0 or YMin. Needs a width from outside.</summary>
    internal sealed record BarChart(IReadOnlyList<(string Label, float Value)> Bars) : UIComponent
    {
        public float? YMin { get; init; }
        public float? YMax { get; init; }
        public float PlotHeight { get; init; } = 140f;
        public Color? Color { get; init; }
        /// <summary>Each bar takes the next theme mineral. Color is ignored.</summary>
        public bool Colorful { get; init; }
        public Func<float, string>? Format { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var pointer = UseRef<Vector2?>(null);
            var (lo, hi) = LineChart.Range(Bars.Select(b => b.Value), YMin ?? 0f, YMax);
            var (min, max, _) = LineChart.Nice(lo, hi, 5, true, YMax.HasValue);
            var bars = Bars;
            var format = Format ?? (v => v.ToString("0.##", CultureInfo.InvariantCulture));
            var color = Color ?? t.Bar;
            bool colorful = Colorful;
            var caption = new TextStyle(FontSize.Small, TextAlign.MiddleCenter, false, true, t.TextCaption);

            return Canvas((p, r) =>
            {
                const float labelH = 18f;
                var area = new Rect(r.x, r.y, r.width, r.height - labelH);
                p.DrawRecessed(area, t.Sunken, t, 1f);
                if (bars.Count == 0) return;
                float slot = area.width / bars.Count;
                float w = Mathf.Max(4f, slot * 0.6f);
                for (int i = 0; i < bars.Count; i++)
                {
                    float h = area.height * Mathf.InverseLerp(min, max, bars[i].Value);
                    float x = area.x + slot * i + (slot - w) / 2f;
                    var c = colorful ? t.Minerals[i % t.Minerals.Length] : color;
                    if (h >= 1f)
                    {
                        var bar = new Rect(x, area.yMax - h, w, h);
                        p.FillRect(bar, c);
                        var bevel = t.Raised(c, Mathf.Min(2f, w / 4f));
                        p.DrawBevel(bar, bevel.Light, bevel.Dark, bevel.Width);
                        p.DrawBorder(bar, t.Outline, 1f);
                        if (h > 18f) p.DrawText(new Rect(x - 10, area.yMax - h - 16, w + 20, 16), format(bars[i].Value), caption, t.SecondaryForeground);
                    }
                    p.DrawText(new Rect(area.x + slot * i, area.yMax + 1, slot, labelH), bars[i].Label, caption, t.MutedForeground);
                }

                if (pointer.Current is not { } at || !area.Contains(at)) return;
                int hit = Mathf.Clamp((int)((at.x - area.x) / slot), 0, bars.Count - 1);
                p.FillRect(new Rect(area.x + slot * hit, area.y, slot, area.height), t.HoverOverlay);
                LineChart.DrawTip(p, t, at, r, new[] { $"{bars[hit].Label}: {format(bars[hit].Value)}" },
                    new[] { colorful ? t.Minerals[hit % t.Minerals.Length] : color });
            }).Height(PlotHeight + 18f)
                .OnPointerMove(e => pointer.Current = e.Position)
                .OnHover(over =>
                {
                    if (!over) pointer.Current = null;
                });
        }
    }
}

namespace Rimact.Components
{
    /// <summary>Pie or donut chart with a legend of shares. Values of 0 or less get no slice.</summary>
    internal sealed record PieChart(IReadOnlyList<(string Label, float Value)> Slices) : UIComponent
    {
        public float Size { get; init; } = 140f;
        /// <summary>Hole radius as a fraction of the outer radius. 0 = pie, about 0.6 = donut.</summary>
        public float InnerRadius { get; init; }
        /// <summary>Text in the donut hole. Needs InnerRadius above 0.</summary>
        public string? CenterLabel { get; init; }
        /// <summary>Per-slice colors. Missing ones fall back to the theme's minerals.</summary>
        public IReadOnlyList<Color>? Colors { get; init; }
        public bool Legend { get; init; } = true;
        /// <summary>Formats a slice's share (0..1) for the legend. Whole percents by default.</summary>
        public Func<float, string>? Format { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var slices = Slices;
            var colors = Enumerable.Range(0, slices.Count).Select(i => Colors != null && i < Colors.Count ? Colors[i] : t.Minerals[i % t.Minerals.Length]).ToArray();
            float total = slices.Sum(s => Mathf.Max(0f, s.Value));
            float inner = Mathf.Clamp(InnerRadius, 0f, 0.95f);
            var center = CenterLabel;
            var gap = t.Background;
            var labelStyle = new TextStyle(FontSize.Small, TextAlign.MiddleCenter, false, true, t.TextTitle, true);

            var pie = Canvas((p, r) =>
            {
                var c = r.center;
                float outer = Mathf.Min(r.width, r.height) / 2f - 1f, hole = outer * inner;
                if (total <= 0f)
                {
                    p.FillTriangles(Ring(c, outer, hole, 0f, Mathf.PI * 2f), t.Sunken);
                    return;
                }
                float a = -Mathf.PI / 2f;
                var edges = new List<float>();
                for (int i = 0; i < slices.Count; i++)
                {
                    float v = slices[i].Value;
                    if (v <= 0f) continue;
                    float sweep = Mathf.PI * 2f * v / total;
                    p.FillTriangles(Ring(c, outer, hole, a, a + sweep), colors[i]);
                    edges.Add(a);
                    a += sweep;
                }
                if (edges.Count > 1)
                    foreach (float e in edges)
                    {
                        var dir = new Vector2(Mathf.Cos(e), Mathf.Sin(e));
                        p.DrawLine(c + dir * hole, c + dir * outer, gap, 1.5f);
                    }
                if (center != null && hole > 0f)
                    p.DrawText(new Rect(c.x - hole, c.y - 12f, hole * 2f, 24f), center, labelStyle, t.Foreground);
            }).Size(Size, Size);

            if (!Legend) return pie;
            var format = Format ?? (share => Mathf.RoundToInt(share * 100f) + "%");
            var legend = Column(slices.Select((s, i) => (Node?)Row(
                    Row().Size(10, 10).Bg(colors[i]).Bevel(t.Raised(colors[i], 1f)).Border(t.Outline),
                    Text(s.Label).Color(t.SecondaryForeground).Truncate().Grow().Shrink(),
                    Text(format(total > 0f ? Mathf.Max(0f, s.Value) / total : 0f)).Color(t.Foreground).Anchor(TextAlign.MiddleRight))
                .Gap(t.Space(2)).Align(Align.Center).Height(22))).MinWidth(140);
            return Row(pie, legend).Gap(t.Space(5)).Align(Align.Center);
        }

        // Triangles for the ring segment from radius r0 to r1, angle a0 to a1
        private static List<Vector2> Ring(Vector2 c, float r1, float r0, float a0, float a1)
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / (Mathf.PI / 40f)));
            var tris = new List<Vector2>(segments * 6);
            for (int j = 0; j < segments; j++)
            {
                float s0 = Mathf.Lerp(a0, a1, (float)j / segments), s1 = Mathf.Lerp(a0, a1, (float)(j + 1) / segments);
                var d0 = new Vector2(Mathf.Cos(s0), Mathf.Sin(s0));
                var d1 = new Vector2(Mathf.Cos(s1), Mathf.Sin(s1));
                tris.Add(c + d0 * r0); tris.Add(c + d0 * r1); tris.Add(c + d1 * r1);
                if (r0 > 0f)
                {
                    tris.Add(c + d0 * r0); tris.Add(c + d1 * r1); tris.Add(c + d1 * r0);
                }
            }
            return tris;
        }
    }
}

namespace Rimact.Components
{
    /// <summary>Tiny trend line with no axes or labels. 24px tall, takes its width from outside.</summary>
    internal sealed record Sparkline(IReadOnlyList<float> Values) : UIComponent
    {
        public Color? Color { get; init; }
        public float? YMin { get; init; }
        public float? YMax { get; init; }
        /// <summary>Dashed reference line at this value, like a frame budget.</summary>
        public float? Baseline { get; init; }
        /// <summary>Shades under the line.</summary>
        public bool Area { get; init; } = true;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var values = Values;
            var color = Color ?? t.Plasteel;
            bool shade = Area;
            var baseline = Baseline;
            float? yMin = YMin, yMax = YMax;
            return Canvas((p, r) =>
            {
                // Range is read at paint time, so values appended to the same list still fit
                float lo = yMin ?? (values.Count > 0 ? values.Min() : 0f), hi = yMax ?? (values.Count > 0 ? values.Max() : 1f);
                if (baseline is { } bl && !yMin.HasValue) lo = Mathf.Min(lo, bl);
                if (baseline is { } bh && !yMax.HasValue) hi = Mathf.Max(hi, bh);
                if (hi - lo < 1e-6f) hi = lo + 1f;
                var area = new Rect(r.x + 1, r.y + 2, r.width - 2, r.height - 4);
                float Y(float v) => area.yMax - area.height * Mathf.Clamp01(Mathf.InverseLerp(lo, hi, v));
                if (baseline is { } b)
                {
                    float y = Mathf.Round(Y(b));
                    for (float x = area.x; x < area.xMax; x += 6f) p.FillRect(new Rect(x, y, Mathf.Min(3f, area.xMax - x), 1f), t.BorderStrong);
                }
                if (values.Count == 0) return;
                Vector2 At(int i) => new(values.Count == 1 ? area.center.x : area.x + area.width * i / (values.Count - 1), Y(values[i]));
                if (shade && values.Count > 1)
                {
                    var tris = new List<Vector2>((values.Count - 1) * 6);
                    for (int i = 1; i < values.Count; i++)
                    {
                        Vector2 a = At(i - 1), c = At(i);
                        Vector2 a0 = new(a.x, area.yMax), c0 = new(c.x, area.yMax);
                        tris.Add(a); tris.Add(c); tris.Add(c0);
                        tris.Add(a); tris.Add(c0); tris.Add(a0);
                    }
                    p.FillTriangles(tris, Theme.WithAlpha(color, 0.18f));
                }
                var stroke = new List<Vector2>((values.Count - 1) * 6);
                for (int i = 1; i < values.Count; i++) Glyphs.AddSegment(stroke, At(i - 1), At(i), 0.75f);
                if (stroke.Count > 0) p.FillTriangles(stroke, color);
                var last = At(values.Count - 1);
                p.FillRect(new Rect(last.x - 1.5f, last.y - 1.5f, 3f, 3f), color);
            }).Height(24).MinWidth(40);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.PieChart PieChart(IEnumerable<(string label, float value)> slices) =>
            new(slices.Select(s => (s.label, s.value)).ToList());

        public static Components.LineChart LineChart(params Components.ChartSeries[] series) => new(series);

        public static Components.ChartSeries ChartSeries(string name, IReadOnlyList<float> values, UnityEngine.Color? color = null) =>
            new(name, values) { Color = color };

        public static Components.Sparkline Sparkline(IReadOnlyList<float> values, UnityEngine.Color? color = null) => new(values) { Color = color };

        public static Components.BarChart BarChart(IEnumerable<(string label, float value)> bars) =>
            new(bars.Select(b => (b.label, b.value)).ToList());
    }
}
