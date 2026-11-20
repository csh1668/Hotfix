using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Section that expands and collapses when its header is clicked. Content is mounted only while open.</summary>
    internal sealed record Collapsible(string Title, Node Content) : UIComponent
    {
        /// <summary>Makes it controlled. Pair with OnOpenChange.</summary>
        public bool? Open { get; init; }
        public Action<bool>? OnOpenChange { get; init; }
        public bool DefaultOpen { get; init; }
        /// <summary>Shown at the right of the header.</summary>
        public Node? Extra { get; init; }
        public float HeaderHeight { get; init; } = 28f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var inner = UseState(DefaultOpen);
            bool open = Open ?? inner.Value;
            var header = Header(t, Title, open, Extra, HeaderHeight, () =>
            {
                if (Open is null) inner.Set(!open);
                OnOpenChange?.Invoke(!open);
            });
            return Column(header, new Expand(open, Content));
        }

        internal static Node Header(Theme t, string title, bool open, Node? extra, float height, Action toggle) =>
            Row(
                    new Chevron(open),
                    Elements.Text(title).Truncate().Grow().Shrink(),
                    extra)
                .Height(height).Gap(t.Space(2)).Padding(t.Space(2), 0).Align(Align.Center)
                .Radius(t.Radius).HoverOverlay(t.HoverOverlay)
                .OnClick(toggle);
    }

    /// <summary>Chevron that turns from right to down as it opens.</summary>
    internal sealed record Chevron(bool Open) : UIComponent
    {
        public float Size { get; init; } = 12f;
        /// <summary>Closed direction in degrees, clockwise from right. 180 points left.</summary>
        public float Direction { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            float angle = (UseAnimated(Open ? 90f : 0f, 0.15f) + Direction) * Mathf.Deg2Rad;
            var color = t.MutedForeground;
            return Canvas((p, r) =>
            {
                var c = r.center;
                float s = Mathf.Min(r.width, r.height) * 0.22f;
                Vector2 Rot(float x, float y) => c + new Vector2(x * Mathf.Cos(angle) - y * Mathf.Sin(angle), x * Mathf.Sin(angle) + y * Mathf.Cos(angle));
                var tip = Rot(s, 0f);
                p.DrawLine(Rot(-s * 0.6f, -s * 1.6f), tip, color, 1.6f);
                p.DrawLine(tip, Rot(-s * 0.6f, s * 1.6f), color, 1.6f);
            }).Size(Size, Size);
        }
    }

    /// <summary>Shows Child while Open, easing its height open and shut. Unmounts it once fully closed.</summary>
    /// <remarks>Fill heights inside the child count as their content.</remarks>
    internal sealed record Expand(bool Open, Node Child) : UIComponent
    {
        public float Seconds { get; init; } = 0.18f;

        protected internal override Node? Build()
        {
            float natural = UseLayout().ContentSize.y;
            var openAtMount = UseRef(Open);
            var measured = UseRef(false);
            // Open at mount: jump to the first measured height, no easing up from 0
            bool snap = openAtMount.Current && !measured.Current && natural > 0f;
            if (natural > 0f) measured.Current = true;
            float height = UseAnimated(Open ? natural : 0f, snap ? 0f : Seconds);
            if (!Open && height <= 0.5f) return null;
            return Column(Child).ScrollY(0f).Height(height);
        }
    }

    internal sealed record AccordionItem(string Id, string Title, Node Content)
    {
        public Node? Extra { get; init; }
    }

    /// <summary>Collapsible sections keyed by item Id. One open at a time unless Multiple is set.</summary>
    internal sealed record Accordion(IReadOnlyList<AccordionItem> Items) : UIComponent
    {
        public bool Multiple { get; init; }
        /// <summary>Ids open at first. Open state is always internal after that.</summary>
        public IReadOnlyCollection<string>? DefaultOpen { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var open = UseState(() => (IReadOnlyCollection<string>)(DefaultOpen?.ToArray() ?? Array.Empty<string>()));
            return Column(Items.Select(item =>
            {
                bool isOpen = open.Value.Contains(item.Id);
                return (Node)Column(
                        Collapsible.Header(t, item.Title, isOpen, item.Extra, 28f, () =>
                            open.Set(cur => isOpen
                                ? cur.Where(id => id != item.Id).ToArray()
                                : Multiple ? cur.Append(item.Id).ToArray() : new[] { item.Id })),
                        new Expand(isOpen, Column(item.Content).Padding(new Edges(t.Space(2), t.Space(1), 0, t.Space(2)))),
                        Row().Height(1).Bg(t.Border))
                    .Key(item.Id);
            }));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Collapsible Collapsible(string title, Node content, bool defaultOpen = false) =>
            new(title, content) { DefaultOpen = defaultOpen };

        public static Components.Accordion Accordion(params Components.AccordionItem[] items) => new(items);

        public static Components.Accordion Accordion(bool multiple, IReadOnlyCollection<string>? defaultOpen, params Components.AccordionItem[] items) =>
            new(items) { Multiple = multiple, DefaultOpen = defaultOpen };

        /// <summary>extra sits at the right of the header.</summary>
        public static Components.AccordionItem AccordionItem(string id, string title, Node content, Node? extra = null) =>
            new(id, title, content) { Extra = extra };
    }
}
