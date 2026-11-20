using System;
using System.Collections.Generic;
using System.Linq;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Controlled multi-value picker with removable chips. OnChange gets the selection in option order.</summary>
    internal sealed record MultiSelect<T>(IReadOnlyCollection<T> Values, IReadOnlyList<SelectOption<T>> Options, Action<IReadOnlyCollection<T>>? OnChange) : UIComponent
    {
        public string? Placeholder { get; init; }
        public float PanelWidth { get; init; } = 280f;
        public int VisibleRows { get; init; } = 9;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var strings = UseStrings();
            var backend = UseHost().Backend;
            var open = UseState(false);
            var chosen = new HashSet<T>(Values);
            var options = Options;
            var onChange = OnChange;

            void Toggle(T value)
            {
                var next = new HashSet<T>(chosen);
                if (!next.Remove(value)) next.Add(value);
                onChange?.Invoke(options.Where(o => next.Contains(o.Value)).Select(o => o.Value).ToArray());
            }

            var chips = options.Where(o => chosen.Contains(o.Value)).Select(o => (Node?)Row(
                    o.Icon is { } icon ? Image(icon).Size(14, 14) : null,
                    Text(o.Label).TextSize(t.TextCaption).Color(t.Foreground),
                    Row(Image(Glyph.X).Size(10, 10)).Size(14, 14).Center().Fg(t.MutedForeground).HoverFg(t.Foreground)
                        .OnClick(() => Toggle(o.Value)).Focusable(false).Key("remove:" + o.Value))
                .Height(20).Padding(t.Space(1.5f), 0).Gap(t.Space(1)).Align(Align.Center)
                .Bg(t.Secondary).Border(t.Border).Key("chip:" + o.Value)).ToArray();
            Node shown = chips.Length > 0
                ? Row(chips).FlexWrap().Gap(t.Space(1)).Grow().Shrink()
                : Text(Placeholder ?? "").Color(t.MutedForeground).Truncate().Grow().Shrink();
            var (normal, hover, pressed) = Button.Styles(t, ButtonVariant.Default);
            var trigger = Row(shown, new Chevron(true) { Size = 12f })
                .MinHeight(t.ControlHeight).Padding(t.Space(2), t.Space(1)).Gap(t.Space(1.5f)).Align(Align.Center)
                .Visual(normal, hover, pressed).Key("trigger");

            return new Popover(trigger, new ComboboxPanel<T>(default!, options, Toggle)
            {
                IsSelected = chosen.Contains,
                SearchPlaceholder = strings.Search,
                EmptyText = strings.NoResults,
                AllLabel = strings.All,
                VisibleRows = VisibleRows,
            })
            {
                Open = open.Value,
                OnOpenChange = v =>
                {
                    if (v) backend.PlaySound(UiSound.MenuOpen);
                    open.Set(v);
                },
                Width = PanelWidth,
            };
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.MultiSelect<T> MultiSelect<T>(IReadOnlyCollection<T> values, IEnumerable<(T value, string label)> options,
            Action<IReadOnlyCollection<T>>? onChange, string? placeholder = null) =>
            new(values, options.Select(o => new Components.SelectOption<T>(o.value, o.label)).ToList(), onChange) { Placeholder = placeholder };
    }
}
