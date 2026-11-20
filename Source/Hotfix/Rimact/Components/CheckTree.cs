using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Checkbox tree like vanilla's storage filter. OnCheck gets the leaves to set and the new value.</summary>
    /// <remarks>Rows are virtualized, so size it with .Grow() or .Height(). KeyOf must be unique across the tree.</remarks>
    /// <example><code>CheckTree(roots, i => i.Id, i => i.Children, i => i.Name, i => enabled.Contains(i.Id),
    ///     (items, on) => { foreach (var i in items) if (on) enabled.Add(i.Id); else enabled.Remove(i.Id); }).Grow()</code></example>
    internal sealed record CheckTree<T>(
        IReadOnlyList<T> Roots,
        Func<T, string> KeyOf,
        Func<T, IReadOnlyList<T>> Children,
        Func<T, string> LabelOf,
        Func<T, bool> IsChecked,
        Action<IReadOnlyList<T>, bool> OnCheck) : UIComponent
    {
        public Func<T, Icon?>? IconOf { get; init; }
        public Func<T, string?>? TooltipOf { get; init; }
        public IReadOnlyCollection<string>? DefaultExpanded { get; init; }
        /// <summary>Search box and All/None buttons. All/None only touch the leaves shown.</summary>
        public bool Toolbar { get; init; } = true;
        public float Indent { get; init; } = 14f;

        private readonly record struct Row_(T Item, string Key, int Depth, bool Branch, bool Open, int Checked, int Total);

        protected internal override Node Build()
        {
            var t = UseTheme();
            var strings = UseStrings();
            var backend = UseHost().Backend;
            var expanded = UseState(() => new HashSet<string>(DefaultExpanded ?? Array.Empty<string>()));
            var query = UseState("");
            // Check state lives outside. Bumped after OnCheck to re-render
            var version = UseState(0);
            string q = query.Value;
            bool searching = q.Length > 0;
            var open = expanded.Value;

            // Leaf counts under every item, and which items the search keeps
            var counts = new Dictionary<string, (int on, int total)>();
            var keep = searching ? new HashSet<string>() : null;
            (int on, int total) Count(T item, bool parentMatched)
            {
                string key = KeyOf(item);
                var kids = Children(item);
                bool matched = parentMatched || (searching && LabelOf(item).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                (int on, int total) c;
                if (kids.Count == 0)
                {
                    // While searching, a branch counts only the leaves shown. Those are the ones its box changes
                    bool counted = !searching || matched;
                    c = counted ? (IsChecked(item) ? 1 : 0, 1) : (0, 0);
                    if (matched) keep?.Add(key);
                }
                else
                {
                    c = (0, 0);
                    foreach (var kid in kids)
                    {
                        var k = Count(kid, matched);
                        c = (c.on + k.on, c.total + k.total);
                        if (keep != null && keep.Contains(KeyOf(kid))) keep.Add(key);
                    }
                }
                counts[key] = c;
                return c;
            }
            foreach (var root in Roots) Count(root, false);

            var rows = new List<Row_>();
            void Walk(T item, int depth)
            {
                string key = KeyOf(item);
                if (keep != null && !keep.Contains(key)) return;
                var kids = Children(item);
                bool branch = kids.Count > 0;
                bool isOpen = branch && (searching || open.Contains(key));
                var c = counts[key];
                rows.Add(new Row_(item, key, depth, branch, isOpen, c.on, c.total));
                if (isOpen)
                    foreach (var kid in kids) Walk(kid, depth + 1);
            }
            foreach (var root in Roots) Walk(root, 0);

            var onCheck = OnCheck;
            void Set(IEnumerable<T> items, bool value)
            {
                var leaves = new List<T>();
                void Leaves(T item)
                {
                    var kids = Children(item);
                    if (kids.Count == 0)
                    {
                        if (keep is null || keep.Contains(KeyOf(item))) leaves.Add(item);
                    }
                    else foreach (var kid in kids) Leaves(kid);
                }
                foreach (var item in items) Leaves(item);
                backend.PlaySound(value ? UiSound.CheckboxOn : UiSound.CheckboxOff);
                onCheck(leaves, value);
                version.Set(v => v + 1);
            }

            void Toggle(string key) => expanded.Set(s =>
            {
                var next = new HashSet<string>(s);
                if (!next.Remove(key)) next.Add(key);
                return next;
            });

            Node RowNode(int i)
            {
                var r = rows[i];
                bool all = r.Total > 0 && r.Checked == r.Total, some = r.Checked > 0 && !all;
                var item = r.Item;
                var icon = IconOf?.Invoke(item);
                var box = Row(Checkbox.Box(t, all, some))
                    .Size(t.CheckboxSize + 4, t.CheckboxSize + 4).Center()
                    .OnClick(() => Set(new[] { item }, !all))
                    .Key("check");
                var chevron = r.Branch ? (Node)new Chevron(r.Open) { Size = 12f } : Row().Width(12);
                var row = Row(
                        r.Depth > 0 ? Row().Width(r.Depth * Indent) : null,
                        chevron,
                        icon is null ? null : Image(icon).Size(20, 20),
                        Text(LabelOf(item)).Truncate().Grow().Shrink().Color(r.Branch ? t.Foreground : t.SecondaryForeground),
                        box)
                    .Gap(t.Space(1.5f)).Padding(t.Space(1), 0).Align(Align.Center)
                    .HoverOverlay(t.HoverOverlay)
                    .Tooltip(TooltipOf?.Invoke(item));
                return r.Branch
                    ? row.OnClick(() =>
                    {
                        if (searching) return;
                        backend.PlaySound(UiSound.Click);
                        Toggle(r.Key);
                    })
                    : row.OnClick(() => Set(new[] { item }, !all));
            }

            var list = rows.Count == 0
                ? Text(strings.NoResults).Color(t.MutedForeground).Anchor(TextAlign.MiddleCenter).Height(t.RowHeight).FillWidth()
                : (Node)new VirtualList(rows.Count, t.RowHeight, RowNode) { KeyOf = i => rows[i].Key }.Grow();
            if (!Toolbar) return Column(list);
            var shown = rows.Where(r => r.Depth == 0).Select(r => r.Item).ToList();
            return Column(
                Row(
                    new Input(q, v => query.Set(v)) { Placeholder = strings.Search, Icon = BuiltinIcon.Search, Clearable = true }.Grow().Shrink(),
                    new Button(strings.All, () => Set(shown, true)) { Size = ButtonSize.Sm },
                    new Button(strings.None, () => Set(shown, false)) { Size = ButtonSize.Sm })
                    .Gap(t.Space(1.5f)).Align(Align.Center),
                list).Gap(t.Space(2));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.CheckTree<T> CheckTree<T>(IReadOnlyList<T> roots, Func<T, string> key, Func<T, IReadOnlyList<T>> children,
            Func<T, string> label, Func<T, bool> isChecked, Action<IReadOnlyList<T>, bool> onCheck) =>
            new(roots, key, children, label, isChecked, onCheck);
    }
}
