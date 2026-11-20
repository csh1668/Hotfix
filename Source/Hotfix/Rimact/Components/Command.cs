using System;
using System.Collections.Generic;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Entry of a <see cref="Command"/> palette.</summary>
    internal sealed record CommandItem(string Label, Action? OnSelect)
    {
        /// <summary>Heading to list it under. Groups keep the order they first appear in.</summary>
        public string? Group { get; init; }
        /// <summary>Key hint on the right, e.g. Ctrl+E. Display only.</summary>
        public string? Shortcut { get; init; }
        public Icon? Icon { get; init; }
        public bool Disabled { get; init; }
        /// <summary>Extra words the search matches.</summary>
        public IReadOnlyList<string>? Keywords { get; init; }
    }

    /// <summary>Search box over a grouped list of actions. Up/Down move the highlight, Enter runs it.</summary>
    /// <example><code>Command(new CommandItem("Clear cache", ClearCache) { Group = "Maintenance", Keywords = new[] { "reset" } })</code></example>
    internal sealed record Command(IReadOnlyList<CommandItem> Items) : UIComponent
    {
        public string? Placeholder { get; init; }
        public string? EmptyText { get; init; }
        public int VisibleRows { get; init; } = 8;
        public bool AutoFocus { get; init; } = true;

        private const float RowHeight = 28f;

        // A group heading, or an item index
        private readonly record struct Entry(string? Heading, int Item);

        protected internal override Node Build()
        {
            var t = UseTheme();
            var strings = UseStrings();
            var backend = UseHost().Backend;
            var self = UseFiber();
            var field = UseId();
            var query = UseState("");
            var active = UseState(0);
            var items = Items;
            var q = query.Value;
            var entries = UseMemo(() => Entries(items, q), items, q);

            var selectable = new List<int>();
            for (int p = 0; p < entries.Count; p++)
                if (entries[p].Heading is null && !items[entries[p].Item].Disabled) selectable.Add(p);
            int current = selectable.Count == 0 ? -1 : Mathf.Clamp(active.Value, 0, selectable.Count - 1);

            void Run(int item)
            {
                var it = items[item];
                if (it.Disabled) return;
                backend.PlaySound(UiSound.Click);
                it.OnSelect?.Invoke();
            }

            UseKeyDown(k =>
            {
                if (current < 0 || !HasFocus(self, backend, field)) return false;
                switch (k.Key)
                {
                    case KeyCode.DownArrow:
                        active.Set((current + 1) % selectable.Count);
                        return true;
                    case KeyCode.UpArrow:
                        active.Set((current - 1 + selectable.Count) % selectable.Count);
                        return true;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        Run(entries[selectable[current]].Item);
                        return true;
                }
                return false;
            }, beforeTextFields: true);

            Node RowAt(int p)
            {
                var e = entries[p];
                if (e.Heading is { } heading)
                    return Text(heading).TextSize(t.TextCaption).Bold().Color(t.MutedForeground).Anchor(TextAlign.LowerLeft)
                        .Padding(new Edges(t.Space(2), 0, t.Space(2), t.Space(1)));
                var it = items[e.Item];
                int order = selectable.IndexOf(p);
                bool on = order >= 0 && order == current;
                return Row(
                        it.Icon is { } icon ? Image(icon).Size(16, 16).Tint(t.SecondaryForeground) : null,
                        Text(it.Label).Truncate().Grow().Shrink(),
                        it.Shortcut is { } keys ? Text(keys).TextSize(t.TextCaption).Color(t.MutedForeground) : null)
                    .Padding(t.Space(2), 0).Gap(t.Space(2)).Align(Align.Center)
                    .Visual(new Visual { Overlay = on ? t.SelectedOverlay : null, Fg = on ? t.Foreground : t.SecondaryForeground })
                    .OnHover(over =>
                    {
                        if (over && order >= 0) active.Set(order);
                    })
                    .OnClick(() => Run(e.Item))
                    .Disabled(it.Disabled);
            }

            Node list = entries.Count == 0
                ? Text(EmptyText ?? strings.NoResults).Color(t.MutedForeground).Anchor(TextAlign.MiddleCenter).Height(RowHeight * 2)
                : new VirtualList(entries.Count, RowHeight, RowAt)
                {
                    KeyOf = p => entries[p].Heading is { } h ? "group:" + h : "item:" + entries[p].Item,
                    RevealIndex = current >= 0 ? selectable[current] : null,
                }.Height(Mathf.Min(entries.Count, VisibleRows) * RowHeight);
            return Column(
                    new Input(q, v =>
                    {
                        query.Set(v);
                        active.Set(0);
                    })
                    { Placeholder = Placeholder ?? strings.Search, Icon = BuiltinIcon.Search, AutoFocus = AutoFocus, ControlName = field }.FillWidth(),
                    Row().Height(1).Bg(t.Border),
                    list)
                .Gap(t.Space(2));
        }

        // Keys are ours if the search box has text focus or Rimact focus is inside self
        internal static bool HasFocus(Fiber self, IBackend backend, string field)
        {
            if (backend.FocusedControl == field) return true;
            for (var f = self.Host.Focused; f != null; f = f.Parent)
                if (f == self) return true;
            return false;
        }

        private static List<Entry> Entries(IReadOnlyList<CommandItem> items, string query)
        {
            var groups = new List<string?>();
            var byGroup = new Dictionary<string, List<int>>();
            var ungrouped = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!Matches(items[i], query)) continue;
                if (items[i].Group is not { } g)
                {
                    ungrouped.Add(i);
                    continue;
                }
                if (!byGroup.TryGetValue(g, out var list))
                {
                    byGroup[g] = list = new List<int>();
                    groups.Add(g);
                }
                list.Add(i);
            }
            var entries = new List<Entry>();
            foreach (int i in ungrouped) entries.Add(new Entry(null, i));
            foreach (var g in groups)
            {
                entries.Add(new Entry(g, -1));
                foreach (int i in byGroup[g!]) entries.Add(new Entry(null, i));
            }
            return entries;
        }

        private static bool Matches(CommandItem item, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (item.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (item.Keywords is null) return false;
            foreach (var k in item.Keywords)
                if (k.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Command Command(params Components.CommandItem[] items) => new(items);
    }
}
