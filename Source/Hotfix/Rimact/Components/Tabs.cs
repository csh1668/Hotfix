using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>One tab. Content is mounted only while the tab is selected.</summary>
    /// <remarks>Every tab's Content is created on each render, so make it a component like new GeneralTab().</remarks>
    internal sealed record TabItem(string Id, string Label, Node Content)
    {
        public string? Tooltip { get; init; }
        /// <summary>Shown before the label. A collapsed vertical list shows only the icon.</summary>
        public Icon? Icon { get; init; }
    }

    /// <summary>Tabs over a body. Switching tabs unmounts the previous body and resets its state.</summary>
    /// <example><code>Tabs(Tab("general", "General", new GeneralTab()), Tab("advanced", "Advanced", new AdvancedTab())).Grow()</code></example>
    internal sealed record Tabs(IReadOnlyList<TabItem> Items) : UIComponent
    {
        /// <summary>Selected tab id, for controlled tabs. Pair with OnChange.</summary>
        public string? Value { get; init; }
        public Action<string>? OnChange { get; init; }
        public float TabHeight { get; init; } = 32f;
        public float MaxTabWidth { get; init; } = 200f;
        /// <summary>Tabs as a sidebar on the left, ListWidth wide.</summary>
        public bool Vertical { get; init; }
        public float ListWidth { get; init; } = 180f;
        /// <summary>With Vertical, shows only icons, with the labels as tooltips.</summary>
        public bool Collapsed { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var inner = UseState(Items.Count > 0 ? Items[0].Id : "");
            string selected = Value ?? inner.Value;
            var current = Items.FirstOrDefault(i => i.Id == selected) ?? Items.FirstOrDefault();

            var buttons = Items.Select(item =>
            {
                bool on = item.Id == current?.Id;
                Node button = new TabButton(item.Label, on, () =>
                    {
                        if (on) return;
                        // Same sound as switching categories in the game's options
                        backend.PlaySound(UiSound.Click);
                        if (Value is null) inner.Set(item.Id);
                        OnChange?.Invoke(item.Id);
                    })
                { Height = TabHeight, Vertical = Vertical, Icon = item.Icon, Collapsed = Vertical && Collapsed }
                    .Tooltip(Vertical && Collapsed ? item.Tooltip ?? item.Label : item.Tooltip).FocusEntry(on).Key(item.Id);
                return Vertical ? button : button.Grow().MaxWidth(MaxTabWidth);
            });

            // Grow, so a ScrollArea or VirtualList at the content root gets a size
            var body = current is null ? null : current.Content.Key("tab:" + current.Id).Grow();
            if (Vertical)
                return Row(
                    Column(buttons).Width(Collapsed ? t.ControlHeight + t.Space(4) : ListWidth).Gap(t.Space(0.5f)).FocusGroup(selectOnMove: true),
                    Row().Width(1).Bg(t.Border),
                    Column(body).Grow().Shrink().Padding(new Edges(t.Space(4), 0, 0, 0)));
            var header = Row(buttons).Gap(t.Space(1)).FocusGroup(selectOnMove: true);
            return Column(
                header,
                Row().Height(1).Bg(t.Border),
                Column(body).Grow().Padding(new Edges(0, t.Space(3), 0, 0)));
        }
    }

    internal sealed record TabButton(string Label, bool Selected, Action OnClick) : UIComponent
    {
        public float Height { get; init; } = 32f;
        // Vertical list: left-aligned label, the mark is a bar on the left
        public bool Vertical { get; init; }
        public Icon? Icon { get; init; }
        // Icon only, or the label's first letter without an icon
        public bool Collapsed { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            float u = UseAnimated(Selected ? 1f : 0f, 0.18f);
            var accent = t.Indicator;
            var visual = new Visual { Fg = Selected ? t.Foreground : t.MutedForeground, Radius = t.Radius, Overlay = Vertical && Selected ? t.SelectedOverlay : null };
            var hover = new Visual { Fg = Selected ? t.Foreground : t.SecondaryForeground, Overlay = t.HoverOverlay };
            if (Vertical)
                return Row(
                        Canvas((p, r) =>
                        {
                            if (u <= 0f) return;
                            float h = r.height * u;
                            p.FillRect(new Rect(r.x, r.center.y - h / 2f, r.width, h), accent);
                        }).Width(2),
                        Icon is { } icon ? Image(icon).Size(16, 16).AlignSelf(Align.Center) : null,
                        Collapsed
                            ? Icon is null ? Elements.Text(Label.Length > 0 ? Label.Substring(0, 1) : "").Bold() : null
                            : Elements.Text(Label).Truncate().Grow().Shrink())
                    .Height(Height).Gap(t.Space(2.5f)).Padding(new Edges(0, 0, t.Space(2), 0)).Align(Align.Stretch)
                    .Visual(visual, hover)
                    .OnClick(OnClick);
            // Painted, not laid out. An animated width in the tree would resize every tab each frame
            return Column(
                    Elements.Text(Label).Anchor(TextAlign.MiddleCenter).Truncate().Grow(),
                    Canvas((p, r) =>
                    {
                        if (u <= 0f) return;
                        float w = r.width * u;
                        p.FillRect(new Rect(r.center.x - w / 2f, r.y, w, r.height), accent);
                    }).Height(2))
                .Height(Height).Padding(t.Space(3), 0).Radius(t.Radius)
                .Visual(new Visual { Fg = Selected ? t.Foreground : t.MutedForeground, Radius = t.Radius },
                    new Visual { Fg = Selected ? t.Foreground : t.SecondaryForeground, Overlay = t.HoverOverlay })
                .OnClick(OnClick);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Tabs Tabs(params Components.TabItem[] items) => new(items);

        public static Components.Tabs Tabs(string value, Action<string> onChange, params Components.TabItem[] items) =>
            new(items) { Value = value, OnChange = onChange };

        public static Components.TabItem Tab(string id, string label, Node content, string? tooltip = null) =>
            new(id, label, content) { Tooltip = tooltip };
    }
}
