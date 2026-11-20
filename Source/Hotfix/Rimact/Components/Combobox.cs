using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Select with a search box, for long lists a FloatMenu can't filter.</summary>
    /// <remarks>Search matches label, detail and keywords. Two or more groups add a group filter.</remarks>
    internal sealed record Combobox<T>(T Value, IReadOnlyList<SelectOption<T>> Options, Action<T>? OnChange) : UIComponent
    {
        public string? Placeholder { get; init; }
        /// <summary>Defaults to Strings.Search.</summary>
        public string? SearchPlaceholder { get; init; }
        /// <summary>Shown when nothing matches. Defaults to Strings.NoResults.</summary>
        public string? EmptyText { get; init; }
        public float PanelWidth { get; init; } = 280f;
        public int VisibleRows { get; init; } = 9;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var open = UseState(false);
            var current = Options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, Value));
            bool found = Options.Any(o => EqualityComparer<T>.Default.Equals(o.Value, Value));
            var (normal, hover, pressed) = Button.Styles(t, ButtonVariant.Default);

            var trigger = Row(
                    found && current.Icon is { } icon ? Image(icon).Size(18, 18) : null,
                    Elements.Text(found ? current.Label : Placeholder ?? "").Truncate().Grow().Shrink().Color(found ? t.Foreground : t.MutedForeground),
                    new Chevron(true) { Size = 12f })
                .Height(t.ControlHeight).Padding(t.Space(3), 0).Gap(t.Space(1.5f)).Align(Align.Center)
                .Visual(normal, hover, pressed);

            var value = Value;
            var options = Options;
            var onChange = OnChange;
            var strings = UseStrings();
            var search = SearchPlaceholder ?? strings.Search;
            var empty = EmptyText ?? strings.NoResults;
            return new Popover(trigger, new ComboboxPanel<T>(value, options, v =>
                {
                    open.Set(false);
                    onChange?.Invoke(v);
                })
            {
                SearchPlaceholder = search,
                EmptyText = empty,
                AllLabel = strings.All,
                VisibleRows = VisibleRows,
            })
            {
                Open = open.Value,
                OnOpenChange = v =>
                {
                    // Same sound as a dropdown opening its FloatMenu
                    if (v) backend.PlaySound(UiSound.MenuOpen);
                    open.Set(v);
                },
                Width = PanelWidth,
            };
        }
    }

    /// <summary>List inside an open <see cref="Combobox{T}"/>. Search text resets on each open.</summary>
    internal sealed record ComboboxPanel<T>(T Value, IReadOnlyList<SelectOption<T>> Options, Action<T> OnPick) : UIComponent
    {
        public string SearchPlaceholder { get; init; } = "Search...";
        public string EmptyText { get; init; } = "No results.";
        public int VisibleRows { get; init; } = 9;
        /// <summary>Options with a check mark. Defaults to the one equal to Value.</summary>
        public Func<T, bool>? IsSelected { get; init; }
        /// <summary>Group filter entry that shows every group.</summary>
        public string AllLabel { get; init; } = "All";
        public float GroupFilterWidth { get; init; } = 120f;

        private const float RowHeight = 26f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var query = UseState("");
            var group = UseState((string?)null);
            var q = query.Value;
            var g = group.Value;
            var groups = UseMemo(() => Options.Select(o => o.Group).Where(x => x != null).Distinct().ToList(), Options);
            var filtered = UseMemo(() => Options.Where(o => (g is null || o.Group == g) && o.Matches(q)).ToList(), q, g, Options);
            var self = UseFiber();
            var field = UseId();
            var value = Value;
            var active = UseState(() => Math.Max(0, filtered.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, value))));
            int current = filtered.Count == 0 ? -1 : Mathf.Clamp(active.Value, 0, filtered.Count - 1);
            var pick = OnPick;
            UseKeyDown(k =>
            {
                if (current < 0 || !Command.HasFocus(self, backend, field)) return false;
                switch (k.Key)
                {
                    case KeyCode.DownArrow:
                        active.Set((current + 1) % filtered.Count);
                        return true;
                    case KeyCode.UpArrow:
                        active.Set((current - 1 + filtered.Count) % filtered.Count);
                        return true;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (filtered[current].Disabled) return true;
                        backend.PlaySound(UiSound.Click);
                        pick(filtered[current].Value);
                        return true;
                }
                return false;
            }, beforeTextFields: true);

            Node Row_(int i)
            {
                var o = filtered[i];
                bool on = IsSelected?.Invoke(o.Value) ?? EqualityComparer<T>.Default.Equals(o.Value, Value);
                return Row(
                        o.Icon is { } icon ? Image(icon).Size(20, 20) : null,
                        Elements.Text(o.Label).Truncate().Grow().Shrink(),
                        o.Detail is { } detail ? Elements.Text(detail).TextSize(t.TextCaption).Color(t.MutedForeground).Truncate().Shrink() : null,
                        on ? Canvas((p, r) =>
                        {
                            var c = r.center;
                            p.DrawLine(c + new Vector2(-4, 0), c + new Vector2(-1, 3), t.Indicator, 2f);
                            p.DrawLine(c + new Vector2(-1, 3), c + new Vector2(4, -3), t.Indicator, 2f);
                        }).Size(12, 12) : null)
                    .Padding(t.Space(2), 0).Gap(t.Space(2)).Align(Align.Center)
                    .Visual(new Visual { Fg = on ? t.Indicator : t.Foreground, Overlay = on ? t.SelectedOverlay : i == current ? t.HoverOverlay : null },
                        new Visual { Overlay = t.HoverOverlay })
                    .OnHover(over =>
                    {
                        if (over) active.Set(i);
                    })
                    .Disabled(o.Disabled)
                    .OnClick(() =>
                    {
                        backend.PlaySound(UiSound.Click);
                        OnPick(o.Value);
                    });
            }

            float listHeight = Mathf.Clamp(filtered.Count, 1, VisibleRows) * RowHeight;
            var search = new Input(q, v =>
            {
                query.Set(v);
                active.Set(0);
            })
            { Placeholder = SearchPlaceholder, Icon = BuiltinIcon.Search, AutoFocus = true, ControlName = field };
            Node? groupFilter = groups.Count > 1
                ? new Select<string?>(g, groups.Prepend(null).Select(x => new SelectOption<string?>(x, x ?? AllLabel)).ToList(), v =>
                {
                    group.Set(v);
                    active.Set(0);
                }).Width(GroupFilterWidth).Key("group")
                : null;
            return Column(
                Row(search.Grow().Shrink(), groupFilter).Gap(t.Space(1.5f)).FillWidth(),
                filtered.Count == 0
                    ? Elements.Text(EmptyText).Color(t.MutedForeground).Anchor(TextAlign.MiddleCenter).Height(RowHeight)
                    : new VirtualList(filtered.Count, RowHeight, Row_) { RevealIndex = current, KeyOf = i => "option:" + filtered[i].Value }.Height(listHeight)
            ).Gap(t.Space(2));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Combobox<T> Combobox<T>(T value, IEnumerable<(T value, string label)> options, Action<T>? onChange, string? placeholder = null) =>
            new(value, options.Select(o => new Components.SelectOption<T>(o.value, o.label)).ToList(), onChange) { Placeholder = placeholder };
    }
}
