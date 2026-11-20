using System;
using System.Collections.Generic;
using System.Linq;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Choice of a <see cref="RadioGroup{T}"/>. Description shows under the label.</summary>
    internal readonly record struct RadioOption<T>(T Value, string Label, string? Description = null, bool Disabled = false);

    /// <summary>Controlled set of exclusive choices. Stacked unless Horizontal.</summary>
    internal sealed record RadioGroup<T>(T Value, IReadOnlyList<RadioOption<T>> Options, Action<T>? OnChange) : UIComponent
    {
        public bool Horizontal { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var items = Options.Select(o =>
            {
                bool on = EqualityComparer<T>.Default.Equals(o.Value, Value);
                Node? description = string.IsNullOrEmpty(o.Description) ? null
                    : Elements.Text(o.Description!).TextSize(t.TextCaption).Color(t.MutedForeground).Wrap().Anchor(TextAlign.UpperLeft);
                return (Node?)Row(
                        ChoiceMark.Radio(t, on).AlignSelf(Align.Start).Margin(new Edges(0, 3, 0, 0)),
                        Column(Elements.Text(o.Label).Wrap().Anchor(TextAlign.MiddleLeft), description).Gap(1).Grow().Shrink())
                    .Gap(t.Space(2)).Padding(t.Space(1.5f), t.Space(1)).HoverOverlay(t.HoverOverlay)
                    .Disabled(o.Disabled)
                    .FocusEntry(on)
                    .OnClick(() =>
                    {
                        if (on) return;
                        backend.PlaySound(UiSound.Tick);
                        OnChange?.Invoke(o.Value);
                    })
                    .Key(o.Label);
            });
            return (Horizontal ? Row(items).Gap(t.Space(2)) : Column(items).Gap(t.Space(0.5f))).FocusGroup(selectOnMove: true);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.RadioGroup<T> RadioGroup<T>(T value, IEnumerable<(T value, string label)> options, Action<T>? onChange) =>
            new(value, options.Select(o => new Components.RadioOption<T>(o.value, o.label)).ToList(), onChange);
    }
}
