using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Controlled horizontal slider. OnChange fires only when the value really changes.</summary>
    internal sealed record Slider(float Value, Action<float>? OnChange) : UIComponent
    {
        public float Min { get; init; }
        public float Max { get; init; } = 1f;
        /// <summary>Snaps to multiples of Step from Min when > 0. Arrow keys move by it, or by range / 20.</summary>
        public float Step { get; init; }
        /// <summary>Value bubble over the thumb while hovered or dragged.</summary>
        public bool ShowValue { get; init; }
        /// <summary>Tick at each Step. None if that would be more than 40 ticks.</summary>
        public bool Marks { get; init; }
        /// <summary>Text for ShowValue. Up to two decimals by default.</summary>
        public Func<float, string>? Format { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var layout = UseLayout();
            const float handleW = 10f, handleH = 18f, rail = 8f;
            float range = Mathf.Max(1e-6f, Max - Min);
            float pct = Mathf.Clamp01((Value - Min) / range);
            float trackW = Mathf.Max(0, layout.Rect.width - handleW);

            void SetValue(float v)
            {
                if (Step > 0) v = Mathf.Round((v - Min) / Step) * Step + Min;
                v = Mathf.Clamp(v, Min, Max);
                if (Mathf.Approximately(v, Value)) return;
                PlayDragSound(backend);
                OnChange?.Invoke(v);
            }

            void SetFromLocal(float localX)
            {
                if (trackW <= 0) return;
                SetValue(Min + Mathf.Clamp01((localX - handleW / 2) / trackW) * range);
            }

            float step = Step > 0 ? Step : range / 20f;
            var pointer = UseRef(false);
            var dragging = UseRef(false);
            bool showValue = ShowValue;
            int marks = Marks && Step > 0 ? Mathf.RoundToInt(range / Step) : 0;
            string label = (Format ?? (v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))(Value);

            // Rail ends sit under the thumb center at 0 and 1
            return Canvas((p, r) =>
                {
                    float travel = Mathf.Max(0, r.width - handleW);
                    var railRect = new Rect(r.x + handleW / 2 - rail / 2, r.y + (r.height - rail) / 2, travel + rail, rail);
                    float cx = r.x + handleW / 2 + pct * travel;
                    if (t.VanillaParts)
                    {
                        // Same as Widgets.HorizontalSlider: atlas rail, 12px handle
                        p.DrawVanilla(railRect, VanillaPart.SliderRail, InteractionState.None);
                        p.DrawVanilla(new Rect(Mathf.Round(cx - 6f), Mathf.Round(r.center.y - 6f), 12f, 12f), VanillaPart.SliderHandle, InteractionState.None);
                    }
                    else
                    {
                        p.DrawRaised(railRect, t.SurfaceRaised, t, 1f);
                        if (pct > 0) p.DrawRaised(new Rect(railRect.x, railRect.y, cx - railRect.x, rail), Theme.Scale(t.Indicator, 0.85f), t, 1f);
                        p.DrawRaised(new Rect(cx - handleW / 2, r.y + (r.height - handleH) / 2, handleW, handleH), t.Indicator, t, 2f);
                    }
                    if (marks is > 0 and <= 40)
                        for (int i = 0; i <= marks; i++)
                            p.FillRect(new Rect(Mathf.Round(r.x + handleW / 2 + travel * i / marks) - 0.5f, railRect.yMax + 2f, 1f, 3f), t.BorderStrong);
                    // Read at paint time so the bubble needs no re-render
                    if (showValue && (pointer.Current || dragging.Current))
                    {
                        var style = new TextStyle(FontSize.Small, TextAlign.MiddleCenter, false, false, t.TextCaption, true);
                        float w = p.MeasureText(label, style, 0f).x + 10f;
                        var bubble = new Rect(Mathf.Round(cx - w / 2), r.y + (r.height - handleH) / 2 - 22f, w, 18f);
                        p.DrawRaised(bubble, t.SurfaceRaised, t, 1f);
                        p.DrawText(bubble, label, style, t.Foreground);
                    }
                })
                .Height(24)
                .OnHover(over => pointer.Current = over)
                .OnMouseUp(_ =>
                {
                    dragging.Current = false;
                    return false;
                })
                .OnMouseDown(e =>
                {
                    if (e.Button != 0) return false;
                    dragging.Current = true;
                    PlayDragSound(backend);
                    SetFromLocal(e.Local.x);
                    return true;
                })
                .OnDrag(e => SetFromLocal(e.Local.x))
                .Focusable()
                .OnKeyDown(k =>
                {
                    switch (k.Key)
                    {
                        case KeyCode.LeftArrow:
                        case KeyCode.DownArrow:
                            SetValue(Value - step);
                            return true;
                        case KeyCode.RightArrow:
                        case KeyCode.UpArrow:
                            SetValue(Value + step);
                            return true;
                        case KeyCode.Home:
                            SetValue(Min);
                            return true;
                        case KeyCode.End:
                            SetValue(Max);
                            return true;
                    }
                    return false;
                });
        }

        private static float s_lastDragSound = float.NegativeInfinity;

        // At most every 0.075 s across all sliders, like vanilla
        internal static void PlayDragSound(IBackend backend)
        {
            float now = backend.RealTime;
            if (now >= s_lastDragSound && now < s_lastDragSound + 0.075f) return;
            s_lastDragSound = now;
            backend.PlaySound(UiSound.DragSlider);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Slider Slider(float value, Action<float>? onChange, float min = 0f, float max = 1f, float step = 0f) =>
            new(value, onChange) { Min = min, Max = max, Step = step };
    }
}
