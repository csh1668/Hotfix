using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Swatch button that opens an HSV editor with a hex field and preset swatches.</summary>
    internal sealed record ColorPicker(Color Value, Action<Color>? OnChange) : UIComponent
    {
        /// <summary>The theme's minerals by default.</summary>
        public IReadOnlyList<Color>? Presets { get; init; }
        /// <summary>Shows an alpha bar and includes alpha in the hex text.</summary>
        public bool Alpha { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var (normal, hover, pressed) = Button.Styles(t, ButtonVariant.Default);
            var trigger = Row(
                    Row().Size(18, 18).Bg(Value).Border(t.Outline),
                    Text(Hex(Value, Alpha)).Color(t.SecondaryForeground))
                .Height(t.ControlHeight).Padding(t.Space(2), 0).Gap(t.Space(2)).Align(Align.Center)
                .Visual(normal, hover, pressed).Key("trigger");
            return new Popover(trigger, new ColorPanel(Value, OnChange, Presets ?? t.Minerals, Alpha)) { Width = 232f };
        }

        /// <summary>Each in 0..1.</summary>
        public static (float h, float s, float v) ToHsv(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float d = max - min, h = 0f;
            if (d > 1e-6f)
            {
                if (max == c.r) h = (c.g - c.b) / d;
                else if (max == c.g) h = 2f + (c.b - c.r) / d;
                else h = 4f + (c.r - c.g) / d;
                h /= 6f;
                if (h < 0f) h += 1f;
            }
            return (h, max <= 0f ? 0f : d / max, max);
        }

        /// <summary>h, s and v in 0..1.</summary>
        public static Color FromHsv(float h, float s, float v, float a)
        {
            h = (h % 1f + 1f) % 1f * 6f;
            int i = Mathf.FloorToInt(h) % 6;
            float f = h - Mathf.Floor(h);
            float p = v * (1f - s), q = v * (1f - s * f), u = v * (1f - s * (1f - f));
            return i switch
            {
                0 => new Color(v, u, p, a),
                1 => new Color(q, v, p, a),
                2 => new Color(p, v, u, a),
                3 => new Color(p, q, v, a),
                4 => new Color(u, p, v, a),
                _ => new Color(v, p, q, a),
            };
        }

        /// <summary>#RRGGBB, or #RRGGBBAA with alpha.</summary>
        public static string Hex(Color c, bool alpha = false)
        {
            static int B(float x) => Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);
            var s = $"#{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}";
            return alpha ? s + $"{B(c.a):X2}" : s;
        }

        /// <summary>Accepts #RGB, #RRGGBB or #RRGGBBAA. The # is optional.</summary>
        public static bool TryParseHex(string? text, out Color color)
        {
            color = default;
            var s = (text ?? "").Trim().TrimStart('#');
            if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
            if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) return false;
            if (s.Length == 6) n = (n << 8) | 0xFF;
            color = new Color((n >> 24 & 0xFF) / 255f, (n >> 16 & 0xFF) / 255f, (n >> 8 & 0xFF) / 255f, (n & 0xFF) / 255f);
            return true;
        }

        internal static bool Close(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
    }

    internal sealed record ColorPanel(Color Value, Action<Color>? OnChange, IReadOnlyList<Color> Presets, bool Alpha) : UIComponent
    {
        private static readonly Color[] HueStops =
        {
            new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 1, 1), new(0, 0, 1), new(1, 0, 1), new(1, 0, 0),
        };

        protected internal override Node Build()
        {
            var t = UseTheme();
            var self = UseFiber();
            var held = UseState(() => ColorPicker.ToHsv(Value));
            // Hex text as typed and the color it stands for. Shown until the color changes some other way
            var text = UseState(() => (Text: ColorPicker.Hex(Value, Alpha), For: Value));
            var value = Value;
            // Keep the held hue while it still gives this color, so gray or black doesn't lose the hue
            var (h, s, v) = ColorPicker.Close(ColorPicker.FromHsv(held.Value.h, held.Value.s, held.Value.v, value.a), value) ? held.Value : ColorPicker.ToHsv(value);
            var onChange = OnChange;
            bool alpha = Alpha;

            void Emit(float nh, float ns, float nv, float na)
            {
                held.Set((nh, ns, nv));
                var c = ColorPicker.FromHsv(nh, ns, nv, na);
                text.Set((ColorPicker.Hex(c, alpha), c));
                onChange?.Invoke(c);
            }

            var pure = ColorPicker.FromHsv(h, 1f, 1f, 1f);
            var sv = Canvas((p, r) =>
                {
                    p.FillGradient(r, Color.white, pure, GradientDirection.Horizontal);
                    p.FillGradient(r, new Color(0, 0, 0, 0), Color.black, GradientDirection.Vertical);
                    p.DrawBorder(r, t.Outline, 1f);
                    var at = new Vector2(r.x + s * r.width, r.y + (1f - v) * r.height);
                    p.DrawBorder(new Rect(at.x - 5f, at.y - 5f, 10f, 10f), Color.white, 2f);
                    p.DrawBorder(new Rect(at.x - 6f, at.y - 6f, 12f, 12f), t.Outline, 1f);
                })
                .Height(140)
                .OnMouseDown(e =>
                {
                    PickSv(e);
                    return true;
                })
                .OnDrag(PickSv)
                .Key("sv");

            void PickSv(PointerEvent e)
            {
                var r = FindRect(self, "sv");
                if (r.width <= 0f || r.height <= 0f) return;
                Emit(h, Mathf.Clamp01(e.Local.x / r.width), 1f - Mathf.Clamp01(e.Local.y / r.height), value.a);
            }

            var hue = Bar(t, self, "hue", (p, r) =>
            {
                float seg = r.width / (HueStops.Length - 1);
                for (int i = 0; i < HueStops.Length - 1; i++)
                    p.FillGradient(new Rect(r.x + seg * i, r.y, seg + 0.5f, r.height), HueStops[i], HueStops[i + 1], GradientDirection.Horizontal);
            }, h, x => Emit(x, s, v, value.a));

            Node? alphaBar = alpha
                ? Bar(t, self, "alpha", (p, r) =>
                {
                    p.FillRect(r, t.Sunken);
                    p.FillGradient(r, ColorPicker.FromHsv(h, s, v, 0f), ColorPicker.FromHsv(h, s, v, 1f), GradientDirection.Horizontal);
                }, value.a, x => Emit(h, s, v, x))
                : null;

            var shown = ColorPicker.Close(text.Value.For, value) ? text.Value.Text : ColorPicker.Hex(value, alpha);
            var hex = new Input(shown, next =>
            {
                if (!ColorPicker.TryParseHex(next, out var c))
                {
                    text.Set((next, value)); // Keep half-typed text
                    return;
                }
                if (!alpha) c = new Color(c.r, c.g, c.b, value.a);
                text.Set((next, c));
                held.Set(ColorPicker.ToHsv(c));
                onChange?.Invoke(c);
            })
            { MinWidth = 0 }.Grow();

            var presets = Row(Presets.Select((c, i) => (Node?)Row().Size(20, 20).Bg(c).Border(t.Outline).HoverOverlay(t.HoverOverlay)
                    .OnClick(() =>
                    {
                        held.Set(ColorPicker.ToHsv(c));
                        text.Set((ColorPicker.Hex(c, alpha), c));
                        onChange?.Invoke(c);
                    })
                    .Tooltip(ColorPicker.Hex(c))
                    .Key("preset" + i)))
                .FlexWrap().Gap(t.Space(1.5f));

            return Column(
                sv,
                hue,
                alphaBar,
                Row(Row().Size(t.ControlHeight, t.ControlHeight).Bg(value).Border(t.Outline), hex).Gap(t.Space(2)).Align(Align.Center),
                presets).Gap(t.Space(2.5f));
        }

        // at and set use 0..1
        private static Node Bar(Theme t, Fiber self, string key, Action<IPainter, Rect> paintTrack, float at, Action<float> set)
        {
            // Handlers only get local coordinates, so the width comes from the laid-out fiber
            void Pick(PointerEvent e)
            {
                float w = FindRect(self, key).width;
                if (w > 0f) set(Mathf.Clamp01(e.Local.x / w));
            }
            return Canvas((p, r) =>
                {
                    paintTrack(p, r);
                    p.DrawBorder(r, t.Outline, 1f);
                    float x = Mathf.Round(r.x + at * r.width);
                    var thumb = new Rect(x - 3f, r.y - 2f, 6f, r.height + 4f);
                    p.FillRect(thumb, Color.white);
                    p.DrawBorder(thumb, t.Outline, 1f);
                })
                .Height(12)
                .OnMouseDown(e =>
                {
                    Pick(e);
                    return true;
                })
                .OnDrag(Pick)
                .Key(key);
        }

        private static Rect FindRect(Fiber f, string key)
        {
            if (f.Key == key && !f.IsTransparent) return f.Rect;
            foreach (var c in f.Children)
            {
                var r = FindRect(c, key);
                if (r.width > 0f) return r;
            }
            return default;
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.ColorPicker ColorPicker(UnityEngine.Color value, Action<UnityEngine.Color>? onChange) => new(value, onChange);
    }
}
