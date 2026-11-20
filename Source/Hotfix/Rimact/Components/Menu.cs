using System;
using System.Collections.Generic;
using System.Linq;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal abstract record MenuEntry;

    /// <summary>Closes the menu, then runs OnSelect.</summary>
    internal sealed record MenuAction(string Label, Action? OnSelect) : MenuEntry
    {
        public Icon? Icon { get; init; }
        /// <summary>Key hint on the right, like Ctrl+C. Display only.</summary>
        public string? Shortcut { get; init; }
        public bool Disabled { get; init; }
        public bool Destructive { get; init; }
    }

    /// <summary>Item with a check mark. Choosing it calls OnChange(!Checked).</summary>
    internal sealed record MenuCheck(string Label, bool Checked, Action<bool>? OnChange) : MenuEntry
    {
        public bool Disabled { get; init; }
    }

    /// <summary>Opens a nested menu to the side.</summary>
    internal sealed record MenuSub(string Label, IReadOnlyList<MenuEntry> Entries) : MenuEntry
    {
        public Icon? Icon { get; init; }
        public bool Disabled { get; init; }
    }

    internal sealed record MenuSeparator : MenuEntry;

    /// <summary>Small heading over a group of items.</summary>
    internal sealed record MenuLabel(string Text) : MenuEntry;

    /// <summary>Drop-down menu opened by clicking Trigger. DropdownMenu is the vanilla FloatMenu version.</summary>
    /// <example><code>Menu(IconButton(Glyph.More), MenuAction("Copy", Copy) with { Shortcut = "Ctrl+C" },
    ///     MenuSeparator(), MenuSub("Export", MenuAction("As CSV", ExportCsv)))</code></example>
    internal sealed record Menu(Node Trigger, IReadOnlyList<MenuEntry> Entries) : UIComponent
    {
        public Placement Placement { get; init; } = Placement.Below;
        public float MinWidth { get; init; } = 200f;

        protected internal override Node Build()
        {
            var backend = UseHost().Backend;
            var open = UseState(false);
            void Close() => open.Set(false);
            return Stack(
                Trigger.OnClick(() =>
                {
                    if (!open.Value) backend.PlaySound(UiSound.MenuOpen);
                    open.Set(!open.Value);
                }),
                open.Value
                    ? Overlay(new MenuPanel(Entries, Close) { MinWidth = MinWidth }, Placement, onPointerDownOutside: Close, onEscape: Close)
                    : null);
        }
    }

    // The list of an open Menu or one of its nested menus
    internal sealed record MenuPanel(IReadOnlyList<MenuEntry> Entries, Action CloseAll) : UIComponent
    {
        // Set for nested menus. Left calls it to close just this panel
        public Action? Back { get; init; }
        // Gets -1 or +1 when Left or Right has nothing to do here, so a menubar can switch menus
        public Action<int>? OnSide { get; init; }
        public float MinWidth { get; init; } = 200f;

        private static bool Enabled(MenuEntry e) => e switch
        {
            MenuAction a => !a.Disabled,
            MenuCheck c => !c.Disabled,
            MenuSub s => !s.Disabled,
            _ => false,
        };

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var active = UseState(-1);
            var sub = UseState(-1);
            var entries = Entries;
            var closeAll = CloseAll;
            var back = Back;
            var side = OnSide;
            int current = active.Value;

            int Step(int dir)
            {
                int n = entries.Count;
                for (int k = 1; k <= n; k++)
                {
                    int i = ((current < 0 ? (dir > 0 ? -1 : n) : current) + dir * k % n + n * 2) % n;
                    if (Enabled(entries[i])) return i;
                }
                return current;
            }

            void Choose(int i)
            {
                switch (entries[i])
                {
                    case MenuAction a:
                        backend.PlaySound(UiSound.Click);
                        closeAll();
                        a.OnSelect?.Invoke();
                        break;
                    case MenuCheck c:
                        backend.PlaySound(c.Checked ? UiSound.CheckboxOff : UiSound.CheckboxOn);
                        closeAll();
                        c.OnChange?.Invoke(!c.Checked);
                        break;
                    case MenuSub:
                        sub.Set(i);
                        break;
                }
            }

            UseKeyDown(k =>
            {
                switch (k.Key)
                {
                    case UnityEngine.KeyCode.DownArrow:
                    case UnityEngine.KeyCode.UpArrow:
                        active.Set(Step(k.Key == UnityEngine.KeyCode.DownArrow ? 1 : -1));
                        sub.Set(-1);
                        return true;
                    case UnityEngine.KeyCode.Return:
                    case UnityEngine.KeyCode.KeypadEnter:
                    case UnityEngine.KeyCode.Space:
                        if (current >= 0) Choose(current);
                        return true;
                    case UnityEngine.KeyCode.RightArrow:
                        if (current >= 0 && entries[current] is MenuSub) sub.Set(current);
                        else side?.Invoke(1);
                        return true;
                    case UnityEngine.KeyCode.LeftArrow:
                        if (back != null) back();
                        else if (side != null) side(-1);
                        else return false;
                        return true;
                }
                return false;
            }, beforeTextFields: true); // Before the focused trigger, which would take Enter and Space

            bool lead = entries.Any(e => e is MenuCheck || e is MenuAction { Icon: not null } || e is MenuSub { Icon: not null });
            var rows = new Node?[entries.Count];
            for (int idx = 0; idx < entries.Count; idx++)
            {
                int i = idx;
                var entry = entries[i];
                switch (entry)
                {
                    case MenuSeparator:
                        rows[i] = Row().Height(1).Bg(t.Border).Margin(new Edges(-t.Space(1), t.Space(1), -t.Space(1), t.Space(1)));
                        continue;
                    case MenuLabel label:
                        rows[i] = Text(label.Text).TextSize(t.TextCaption).Bold().Color(t.MutedForeground).Height(22).Padding(t.Space(2), 0);
                        continue;
                }
                var (text, icon, check, right, destructive) = entry switch
                {
                    MenuAction a => (a.Label, a.Icon, false, a.Shortcut is { } s ? Text(s).TextSize(t.TextCaption).Color(t.MutedForeground) : null, a.Destructive),
                    MenuCheck c => (c.Label, (Icon?)null, c.Checked, (Node?)null, false),
                    MenuSub s => (s.Label, s.Icon, false, (Node?)Image(Glyph.ChevronRight).Size(14, 14), false),
                    _ => ("", null, false, null, false),
                };
                Node? leading = !lead ? null
                    : icon != null ? Image(icon).Size(16, 16)
                    : check ? Image(Glyph.Check).Size(16, 16).Tint(t.Indicator)
                    : Row().Size(16, 16);
                bool on = i == current || i == sub.Value;
                Node row = Row(leading, Text(text).Truncate().Grow().Shrink(), right)
                    .Height(28).Padding(t.Space(2), 0).Gap(t.Space(2)).Align(Align.Center)
                    .Visual(new Visual { Overlay = on ? t.HoverOverlay : null, Fg = destructive ? t.Destructive : on ? t.Foreground : t.SecondaryForeground })
                    .OnHover(over =>
                    {
                        if (!over) return;
                        active.Set(i);
                        sub.Set(entry is MenuSub ? i : -1);
                    })
                    .OnClick(() => Choose(i))
                    .Disabled(!Enabled(entry));
                if (entry is MenuSub nested && sub.Value == i)
                    row = Stack(row, Overlay(new MenuPanel(nested.Entries, closeAll) { Back = () => sub.Set(-1), MinWidth = MinWidth },
                        Placement.Right, onEscape: () => sub.Set(-1)));
                rows[i] = row;
            }
            return new FadeIn(Column(rows).MinWidth(MinWidth).Padding(t.Space(1))
                .Bg(t.SurfaceRaised).Radius(t.RadiusLg).Bevel(t.Raised(t.SurfaceRaised, t.BevelWidthSm)).Border(t.Outline)
                .Shadow(t.ShadowLg, t.ShadowColor)
                .OnMouseDown(_ => true)); // Clicks inside must not close the panel
        }
    }
}

namespace Rimact.Components
{
    internal sealed record MenubarMenu(string Label, IReadOnlyList<MenuEntry> Entries);

    /// <summary>Bar of menus like File, Edit, View. While one is open, hover or Left/Right switches menus.</summary>
    /// <example><code>Menubar(MenubarMenu("File", MenuAction("Import...", Import), MenuAction("Export...", Export)),
    ///     MenubarMenu("View", MenuCheck("Show untranslated only", onlyNew, SetOnlyNew)))</code></example>
    internal sealed record Menubar(IReadOnlyList<MenubarMenu> Menus) : UIComponent
    {
        public float MinWidth { get; init; } = 200f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var open = UseState(-1);
            int current = open.Value, count = Menus.Count;
            void Close() => open.Set(-1);
            var items = new Node?[count];
            for (int idx = 0; idx < count; idx++)
            {
                int i = idx;
                var menu = Menus[i];
                bool on = current == i;
                var trigger = Row(Text(menu.Label))
                    .Height(t.ControlHeightSm).Padding(t.Space(3), 0).Align(Align.Center)
                    .Visual(new Visual { Overlay = on ? t.HoverOverlay : null, Fg = on ? t.Foreground : t.SecondaryForeground },
                        new Visual { Overlay = t.HoverOverlay, Fg = t.Foreground })
                    .OnClick(() =>
                    {
                        if (!on) backend.PlaySound(UiSound.MenuOpen);
                        open.Set(on ? -1 : i);
                    })
                    .OnHover(over =>
                    {
                        if (over && current >= 0 && current != i) open.Set(i);
                    });
                items[i] = Stack(trigger, on
                        ? Overlay(new MenuPanel(menu.Entries, Close) { MinWidth = MinWidth, OnSide = d => open.Set((i + d + count) % count) },
                            Placement.Below, onPointerDownOutside: Close, onEscape: Close)
                        : null)
                    .Key(menu.Label);
            }
            return Row(items).Gap(t.Space(0.5f)).Padding(t.Space(1)).Align(Align.Center).Bg(t.Surface).Border(t.Border);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Menubar Menubar(params Components.MenubarMenu[] menus) => new(menus);

        public static Components.MenubarMenu MenubarMenu(string label, params Components.MenuEntry[] entries) => new(label, entries);

        public static Components.Menu Menu(Node trigger, params Components.MenuEntry[] entries) => new(trigger, entries);

        public static Components.MenuAction MenuAction(string label, Action? onSelect) => new(label, onSelect);

        public static Components.MenuCheck MenuCheck(string label, bool @checked, Action<bool>? onChange) => new(label, @checked, onChange);

        public static Components.MenuSub MenuSub(string label, params Components.MenuEntry[] entries) => new(label, entries);

        public static Components.MenuSeparator MenuSeparator() => new();

        public static Components.MenuLabel MenuLabel(string text) => new(text);
    }
}
