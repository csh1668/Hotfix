using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Controlled checkbox with an optional label after the box.</summary>
    internal sealed record Checkbox(bool Checked, Action<bool>? OnChange, string? Label = null) : UIComponent
    {
        /// <summary>Label left, box right, full width with a hover overlay. For settings lists.</summary>
        public bool LabelFirst { get; init; }
        /// <summary>Clicking reports true.</summary>
        public bool Indeterminate { get; init; }
        /// <summary>Shown as a tooltip.</summary>
        public string? Description { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var icon = Box(t, Checked, Indeterminate).AlignSelf(Align.Center);
            Node? label = Label is null ? null : Elements.Text(Label).Wrap().Anchor(TextAlign.MiddleLeft).Grow().Shrink();
            var row = LabelFirst ? Row(label, icon) : Row(icon, label);
            row = row.Gap(t.Space(2)).Align(Align.Center).MinHeight(t.CheckboxSize)
                .OnClick(() =>
                {
                    bool next = Indeterminate || !Checked;
                    host.Backend.PlaySound(next ? UiSound.CheckboxOn : UiSound.CheckboxOff);
                    OnChange?.Invoke(next);
                });
            if (LabelFirst) row = row.Padding(t.Space(2), t.Space(1)).Radius(t.Radius).HoverOverlay(t.HoverOverlay);
            return row.Tooltip(Description);
        }

        // Just the box, no label or click. CheckTree uses it too
        internal static CanvasNode Box(Theme t, bool on, bool partial)
        {
            float size = t.CheckboxSize;
            return Canvas((p, r) =>
            {
                var box = new Rect(r.x, r.y + (r.height - size) / 2f, size, size);
                if (t.VanillaParts)
                {
                    p.DrawIcon(box, partial ? BuiltinIcon.CheckboxPartial : on ? BuiltinIcon.CheckboxOn : BuiltinIcon.CheckboxOff, Color.white);
                    return;
                }
                if (on || partial)
                {
                    p.DrawRaised(box, t.Indicator, t, t.BevelWidthSm);
                    if (partial) p.DrawLine(At(box, 0.28f, 0.5f), At(box, 0.72f, 0.5f), t.IndicatorForeground, 2f);
                    else
                    {
                        p.DrawLine(At(box, 0.24f, 0.52f), At(box, 0.43f, 0.71f), t.IndicatorForeground, 2f);
                        p.DrawLine(At(box, 0.43f, 0.71f), At(box, 0.77f, 0.31f), t.IndicatorForeground, 2f);
                    }
                }
                else
                {
                    p.DrawRaised(box, t.SurfaceRaised, t, t.BevelWidthSm);
                }
            }).Size(size, size);
        }

        private static Vector2 At(Rect r, float x, float y) => new(r.x + r.width * x, r.y + r.height * y);
    }

    internal sealed record Switch(bool On, Action<bool>? OnChange, string? Label = null) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            const float w = 32, h = 18, thumb = 12, inset = 3;
            float k = UseAnimated(On ? 1f : 0f, 0.12f);
            var track = Canvas((p, r) =>
            {
                var tr = new Rect(r.x, r.y + (r.height - h) / 2f, w, h);
                p.DrawRaised(tr, Color.Lerp(t.SurfaceRaised, Theme.Scale(t.Indicator, 0.8f), k), t, t.BevelWidthSm);
                float x = tr.x + inset + (w - thumb - inset * 2) * k;
                var th = new Rect(x, tr.y + (h - thumb) / 2f, thumb, thumb);
                p.DrawRaised(th, Color.Lerp(t.SecondaryForeground, t.Foreground, k), t, 2f);
            }).Size(w, h).AlignSelf(Align.Center);
            Node? label = Label is null ? null : Elements.Text(Label).Wrap().Grow().Shrink();
            return Row(track, label).Gap(t.Space(2)).Align(Align.Center).MinHeight(h)
                .OnClick(() =>
                {
                    host.Backend.PlaySound(!On ? UiSound.CheckboxOn : UiSound.CheckboxOff);
                    OnChange?.Invoke(!On);
                });
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Checkbox Checkbox(bool value, Action<bool>? onChange, string? label = null) => new(value, onChange, label);

        /// <summary>Full-width row with the label left and the box right, like vanilla Listing.CheckboxLabeled.</summary>
        public static Components.Checkbox CheckboxRow(string label, bool value, Action<bool>? onChange, string? tooltip = null) =>
            new(value, onChange, label) { LabelFirst = true, Description = tooltip };

        public static Components.Switch Switch(bool on, Action<bool>? onChange, string? label = null) => new(on, onChange, label);
    }
}
