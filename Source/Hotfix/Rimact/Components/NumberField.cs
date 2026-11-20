using System;
using System.Globalization;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Number input with - and + buttons, clamped to Min..Max.</summary>
    /// <remarks>Typed text is reported each time it parses (invariant culture). Until then the border is red.</remarks>
    internal sealed record NumberField(float Value, Action<float>? OnChange) : UIComponent
    {
        public float Min { get; init; } = float.MinValue;
        public float Max { get; init; } = float.MaxValue;
        public float Step { get; init; } = 1f;
        /// <summary>Digits after the point, for display and rounding. 0 = whole numbers.</summary>
        public int Decimals { get; init; }
        public string? Unit { get; init; }
        public float FieldWidth { get; init; } = 80f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            // Text being typed. null shows the formatted value
            var draft = UseState((string?)null);
            var value = Value;
            string Format(float v) => v.ToString("F" + Mathf.Max(0, Decimals), CultureInfo.InvariantCulture);

            // Drop the draft when the value changes to something it doesn't describe, e.g. from outside
            UseEffect(() =>
            {
                if (draft.Value != null && (!TryParse(draft.Value, out var d) || !Mathf.Approximately(Clamp(d), value))) draft.Set((string?)null);
            }, value);

            void Commit(float v)
            {
                v = Clamp(v);
                if (!Mathf.Approximately(v, value)) OnChange?.Invoke(v);
            }

            void Nudge(float dir)
            {
                draft.Set((string?)null);
                backend.PlaySound(UiSound.Tick);
                Commit(value + dir * Step);
            }

            string text = draft.Value ?? Format(value);
            bool invalid = draft.Value != null && !TryParse(draft.Value, out _);
            return Row(
                    Elements.Button("", () => Nudge(-1), ButtonVariant.Default, ButtonSize.Icon, BuiltinIcon.Minus).Disabled(value <= Min),
                    new Input(text, s =>
                    {
                        draft.Set(s);
                        if (TryParse(s, out var v)) Commit(v);
                    })
                    { Invalid = invalid, MinWidth = FieldWidth }.Width(FieldWidth),
                    Elements.Button("", () => Nudge(1), ButtonVariant.Default, ButtonSize.Icon, BuiltinIcon.Plus).Disabled(value >= Max),
                    Unit is null ? null : Elements.Text(Unit).Color(t.MutedForeground))
                .Gap(t.Space(1)).Align(Align.Center);
        }

        private float Clamp(float v)
        {
            v = Mathf.Clamp(v, Min, Max);
            return Decimals <= 0 ? Mathf.Round(v) : (float)Math.Round(v, Decimals);
        }

        private static bool TryParse(string s, out float v) =>
            float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
    }

    /// <summary>Two-thumb slider for Low..High within Min..Max. A thumb can't pass the other.</summary>
    internal sealed record RangeSlider(float Low, float High, Action<float, float>? OnChange) : UIComponent
    {
        public float Min { get; init; }
        public float Max { get; init; } = 1f;
        public float Step { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var layout = UseLayout();
            var grabbed = UseRef(0); // 0 = low thumb, 1 = high thumb
            const float handleW = 10f, handleH = 18f, rail = 8f;
            float range = Mathf.Max(1e-6f, Max - Min);
            float lo = Mathf.Clamp(Mathf.Min(Low, High), Min, Max), hi = Mathf.Clamp(Mathf.Max(Low, High), Min, Max);
            float pLo = (lo - Min) / range, pHi = (hi - Min) / range;
            float trackW = Mathf.Max(0, layout.Rect.width - handleW);

            float ValueAt(float localX)
            {
                float v = Min + Mathf.Clamp01((localX - handleW / 2) / Mathf.Max(1e-6f, trackW)) * range;
                if (Step > 0) v = Mathf.Round((v - Min) / Step) * Step + Min;
                return Mathf.Clamp(v, Min, Max);
            }

            void Move(float localX)
            {
                if (trackW <= 0) return;
                float v = ValueAt(localX);
                float nLo = lo, nHi = hi;
                if (grabbed.Current == 0) nLo = Mathf.Min(v, hi);
                else nHi = Mathf.Max(v, lo);
                if (Mathf.Approximately(nLo, lo) && Mathf.Approximately(nHi, hi)) return;
                Slider.PlayDragSound(backend);
                OnChange?.Invoke(nLo, nHi);
            }

            return Canvas((p, r) =>
                {
                    float travel = Mathf.Max(0, r.width - handleW);
                    var railRect = new Rect(r.x + handleW / 2 - rail / 2, r.y + (r.height - rail) / 2, travel + rail, rail);
                    p.DrawRaised(railRect, t.SurfaceRaised, t, 1f);
                    float x0 = r.x + handleW / 2 + pLo * travel, x1 = r.x + handleW / 2 + pHi * travel;
                    if (x1 > x0) p.DrawRaised(new Rect(x0, railRect.y, x1 - x0, rail), Theme.Scale(t.Indicator, 0.85f), t, 1f);
                    foreach (float cx in new[] { x0, x1 })
                        p.DrawRaised(new Rect(cx - handleW / 2, r.y + (r.height - handleH) / 2, handleW, handleH), t.Indicator, t, 2f);
                })
                .Height(24)
                .OnMouseDown(e =>
                {
                    if (e.Button != 0) return false;
                    float xLo = handleW / 2 + pLo * trackW, xHi = handleW / 2 + pHi * trackW;
                    float dLo = Mathf.Abs(e.Local.x - xLo), dHi = Mathf.Abs(e.Local.x - xHi);
                    // Thumbs on top of each other: pick by the side the pointer is on
                    grabbed.Current = dHi < dLo || (Mathf.Approximately(dLo, dHi) && e.Local.x > xHi) ? 1 : 0;
                    Slider.PlayDragSound(backend);
                    Move(e.Local.x);
                    return true;
                })
                .OnDrag(e => Move(e.Local.x));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.NumberField NumberField(float value, Action<float>? onChange, float min = float.MinValue, float max = float.MaxValue,
            float step = 1f, int decimals = 0) =>
            new(value, onChange) { Min = min, Max = max, Step = step, Decimals = decimals };

        public static Components.NumberField IntField(int value, Action<int>? onChange, int min = int.MinValue, int max = int.MaxValue, int step = 1) =>
            new(value, v => onChange?.Invoke(Mathf.RoundToInt(v))) { Min = min, Max = max, Step = step };

        public static Components.RangeSlider RangeSlider(float low, float high, Action<float, float>? onChange, float min = 0f, float max = 1f, float step = 0f) =>
            new(low, high, onChange) { Min = min, Max = max, Step = step };
    }
}
