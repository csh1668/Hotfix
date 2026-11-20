using System;
using System.Collections.Generic;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Expandable tree. A row click selects and toggles, the chevron only toggles.</summary>
    /// <remarks>KeyOf must be unique across the whole tree, e.g. a path.</remarks>
    internal sealed record Tree<T>(IReadOnlyList<T> Roots, Func<T, string> KeyOf, Func<T, IReadOnlyList<T>> Children, Func<T, Node> Render) : UIComponent
    {
        public Action<T>? OnSelect { get; init; }
        public string? SelectedKey { get; init; }
        /// <summary>Keys open at first. Expansion is internal state after that.</summary>
        public IReadOnlyCollection<string>? DefaultExpanded { get; init; }
        public float Indent { get; init; } = 16f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var expanded = UseState(() => new HashSet<string>(DefaultExpanded ?? Array.Empty<string>()));
            var open = expanded.Value;
            var onSelect = OnSelect;

            void Toggle(string key) => expanded.Set(s =>
            {
                var next = new HashSet<string>(s);
                if (!next.Remove(key)) next.Add(key);
                return next;
            });

            var rows = new List<Node?>();
            void Walk(T item, int depth)
            {
                string key = KeyOf(item);
                var kids = Children(item) ?? Array.Empty<T>();
                bool branch = kids.Count > 0, isOpen = branch && open.Contains(key);
                var toggle = branch
                    ? Row(new Chevron(isOpen) { Size = 12f }).Size(16, 16).Center().OnClick(() => Toggle(key)).Key("toggle:" + key)
                    : Row().Width(16);
                var row = Row(depth > 0 ? Row().Width(depth * Indent) : null, toggle, Column(Render(item)).Grow().Shrink())
                    .Height(t.RowHeight).Gap(t.Space(1)).Padding(t.Space(1), 0).Align(Align.Center)
                    .HoverOverlay(t.HoverOverlay)
                    .OnClick(() =>
                    {
                        host.Backend.PlaySound(UiSound.Click);
                        if (branch) Toggle(key);
                        onSelect?.Invoke(item);
                    })
                    .Key(key);
                rows.Add(key == SelectedKey ? row.Overlay(t.SelectedOverlay) : row);
                if (isOpen)
                    foreach (var kid in kids) Walk(kid, depth + 1);
            }
            foreach (var root in Roots) Walk(root, 0);
            return Column(rows);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Tree<T> Tree<T>(IReadOnlyList<T> roots, Func<T, string> key, Func<T, IReadOnlyList<T>> children,
            Func<T, Node> render, Action<T>? onSelect = null) => new(roots, key, children, render) { OnSelect = onSelect };
    }
}
