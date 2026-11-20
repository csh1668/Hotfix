using System;
using System.Collections.Generic;
using System.Linq;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Option for Select, Combobox or MultiSelect. Search matches Label, Detail and Keywords.</summary>
    internal readonly record struct SelectOption<T>(T Value, string Label, bool Disabled = false)
    {
        /// <summary>A FloatMenu only shows item and texture icons.</summary>
        public Icon? Icon { get; init; }
        /// <summary>Dimmed text on the right, such as the mod a def comes from.</summary>
        public string? Detail { get; init; }
        /// <summary>Extra search terms that aren't shown, such as a defName.</summary>
        public string? Keywords { get; init; }
        /// <summary>Lets the panel's filter narrow the list to one group.</summary>
        public string? Group { get; init; }

        public bool Matches(string query) =>
            string.IsNullOrEmpty(query)
            || Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || (Detail?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (Keywords?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
    }

    /// <summary>Value picker that opens a vanilla FloatMenu, so the options can go past the window edge.</summary>
    internal sealed record Select<T>(T Value, IReadOnlyList<SelectOption<T>> Options, Action<T>? OnChange) : UIComponent
    {
        public string? Placeholder { get; init; }
        public ButtonVariant Variant { get; init; } = ButtonVariant.Default;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var current = Options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, Value));
            bool found = Options.Any(o => EqualityComparer<T>.Default.Equals(o.Value, Value));
            string label = found ? current.Label : Placeholder ?? "";
            var (normal, hover, pressed) = Button.Styles(t, Variant);
            return Row(
                    found && current.Icon is { } icon ? Image(icon).Size(18, 18) : null,
                    Elements.Text(label).Truncate().Grow().Shrink().Color(found ? t.Foreground : t.MutedForeground),
                    new Chevron(true) { Size = 12f })
                .Height(t.ControlHeight).Padding(t.Space(3), 0).Gap(t.Space(1.5f)).Align(Align.Center)
                .Visual(normal, hover, pressed)
                .OnClick(() => backend.OpenMenu(Options
                    .Select(o => new MenuItem(o.Label, () => OnChange?.Invoke(o.Value), o.Disabled) { Icon = o.Icon })
                    .ToList()));
        }
    }

    /// <summary>Opens a vanilla FloatMenu when Trigger is clicked. Items runs each time it opens.</summary>
    /// <example><code>DropdownMenu("Actions", () => new[] { MenuItem("Copy", Copy), MenuItem("Delete", Delete) })</code></example>
    internal sealed record DropdownMenu(Node Trigger, Func<IEnumerable<MenuItem>> Items) : UIComponent
    {
        protected internal override Node Build()
        {
            var backend = UseHost().Backend;
            return Trigger.OnClick(() => backend.OpenMenu(Items().ToList()));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Select<T> Select<T>(T value, IEnumerable<(T value, string label)> options, Action<T>? onChange, string? placeholder = null) =>
            new(value, options.Select(o => new Components.SelectOption<T>(o.value, o.label)).ToList(), onChange) { Placeholder = placeholder };

        public static Components.Select<T> Select<T>(T value, IReadOnlyList<Components.SelectOption<T>> options, Action<T>? onChange, string? placeholder = null) =>
            new(value, options, onChange) { Placeholder = placeholder };

        public static Components.DropdownMenu DropdownMenu(Node trigger, Func<IEnumerable<MenuItem>> items) => new(trigger, items);

        /// <summary>Trigger is a button with a chevron.</summary>
        public static Components.DropdownMenu DropdownMenu(string label, Func<IEnumerable<MenuItem>> items,
            Components.ButtonVariant variant = Components.ButtonVariant.Default) =>
            new(new Components.Button(label, null) { Variant = variant, Icon = BuiltinIcon.ChevronDown }, items);

        public static Rimact.MenuItem MenuItem(string label, Action onSelect, bool disabled = false) => new(label, onSelect, disabled);
    }
}
