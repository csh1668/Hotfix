using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Two-state button that stays pressed in while on.</summary>
    internal sealed record Toggle(bool Pressed, Action<bool>? OnChange, string? Label) : UIComponent
    {
        public Icon? Icon { get; init; }
        public ButtonSize Size { get; init; } = ButtonSize.Default;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            return Face(t, Pressed, Label, Icon, Size, () =>
            {
                backend.PlaySound(UiSound.Tick);
                OnChange?.Invoke(!Pressed);
            });
        }

        // Also used by ToggleGroup
        internal static BoxNode Face(Theme t, bool pressed, string? label, Icon? icon, ButtonSize size, Action onClick)
        {
            float h = size switch { ButtonSize.Sm => t.ControlHeightSm, ButtonSize.Lg => t.ControlHeightLg, _ => t.ControlHeight };
            var face = pressed ? Theme.Scale(t.SurfaceRaised, 0.7f) : t.SurfaceRaised;
            var bevel = t.Raised(face, t.BevelWidthSm);
            return Row(
                    icon is null ? null : Image(icon).Size(16, 16).Tint(pressed ? t.Indicator : t.SecondaryForeground),
                    string.IsNullOrEmpty(label) ? null : Elements.Text(label!).Anchor(TextAlign.MiddleCenter).Truncate().Shrink())
                .Height(h).Padding(size == ButtonSize.Sm ? t.Space(2) : t.Space(3), 0).Gap(t.Space(1.5f))
                .Align(Align.Center).Justify(Justify.Center)
                .Visual(new Visual { Bg = face, Fg = pressed ? t.Indicator : t.SecondaryForeground, Border = t.Outline, Bevel = pressed ? bevel.Inverted : bevel },
                    new Visual { Overlay = t.HoverOverlay, Fg = pressed ? t.Indicator : t.Foreground },
                    new Visual { Overlay = t.PressedOverlay })
                .OnClick(onClick);
        }
    }

    /// <summary>Give it a label, an icon or both.</summary>
    internal readonly record struct ToggleOption<T>(T Value, string? Label, Icon? Icon = null, string? Tooltip = null);

    /// <summary>Joined toggles with exactly one pressed. A compact Select for a handful of choices.</summary>
    internal sealed record ToggleGroup<T>(T Value, IReadOnlyList<ToggleOption<T>> Options, Action<T>? OnChange) : UIComponent
    {
        public ButtonSize Size { get; init; } = ButtonSize.Default;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            return ButtonGroup.Join(Options.Select(o =>
            {
                bool on = EqualityComparer<T>.Default.Equals(o.Value, Value);
                return (Node)Toggle.Face(t, on, o.Label, o.Icon, Size, () =>
                {
                    if (on) return;
                    backend.PlaySound(UiSound.Tick);
                    OnChange?.Invoke(o.Value);
                }).Tooltip(o.Tooltip).FocusEntry(on).Key(o.Label ?? o.Value?.ToString() ?? "");
            }).ToArray()).FocusGroup();
        }
    }

    /// <summary>Buttons joined edge to edge, sharing their outlines.</summary>
    internal sealed record ButtonGroup(Node[] Buttons) : UIComponent
    {
        protected internal override Node Build() => Join(Buttons);

        // Overlaps each by 1px so neighboring outlines merge
        internal static BoxNode Join(Node[] items)
        {
            var joined = new Node?[items.Length];
            for (int i = 0; i < items.Length; i++)
                joined[i] = i == 0 ? items[i] : items[i].Margin(new Edges(-1f, 0, 0, 0));
            return Row(joined).Align(Align.Center);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Toggle Toggle(bool pressed, Action<bool>? onChange, string? label = null, Icon? icon = null) =>
            new(pressed, onChange, label) { Icon = icon };

        public static Components.ToggleGroup<T> ToggleGroup<T>(T value, IEnumerable<(T value, string label)> options, Action<T>? onChange) =>
            new(value, options.Select(o => new Components.ToggleOption<T>(o.value, o.label)).ToList(), onChange);

        public static Components.ButtonGroup ButtonGroup(params Node?[] buttons) => new(NodeArrays.Normalize(buttons));
    }
}
