using System;
using System.Collections.Generic;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Breadcrumb step. OnClick makes it a link.</summary>
    internal sealed record BreadcrumbItem(string Label, Action? OnClick = null);

    /// <summary>Path of steps split by chevrons. The last step is the current one and never a link.</summary>
    internal sealed record Breadcrumb(IReadOnlyList<BreadcrumbItem> Items) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var children = new List<Node?>(Items.Count * 2);
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (i > 0) children.Add(new Chevron(false) { Size = 10f }.AlignSelf(Align.Center));
                var text = Text(item.Label).Truncate().Shrink();
                if (i == Items.Count - 1) children.Add(text.Color(t.Foreground));
                else if (item.OnClick is { } click)
                    children.Add(Row(text).Shrink().Align(Align.Center).Fg(t.MutedForeground).HoverFg(t.Foreground).OnClick(() =>
                    {
                        host.Backend.PlaySound(UiSound.Click);
                        click();
                    }));
                else children.Add(text.Color(t.MutedForeground));
            }
            return Row(children).Height(t.ControlHeightSm).Gap(t.Space(1.5f)).Align(Align.Center);
        }
    }

    /// <summary>Prev/next arrows around page numbers. Page is zero-based, shown one-based.</summary>
    internal sealed record Pagination(int Page, int PageCount, Action<int>? OnChange) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            float h = t.ControlHeightSm;
            var (ghost, ghostHover, ghostPressed) = Button.Styles(t, ButtonVariant.Ghost);

            void Go(int p)
            {
                host.Backend.PlaySound(UiSound.Click);
                OnChange?.Invoke(p);
            }

            Node Arrow(string key, float direction, int target, bool enabled) =>
                Row(new Chevron(false) { Size = 12f, Direction = direction })
                    .Size(h, h).Center().Visual(ghost, ghostHover, ghostPressed)
                    .OnClick(() => Go(target)).Disabled(!enabled).Key(key);

            var children = new List<Node?> { Arrow("prev", 180f, Page - 1, Page > 0) };
            int gaps = 0;
            foreach (int p in Pages(Page, PageCount))
            {
                if (p < 0)
                {
                    children.Add(Text("…").Color(t.MutedForeground).Anchor(TextAlign.MiddleCenter).Width(h).Key("gap" + gaps++));
                    continue;
                }
                int page = p;
                children.Add(new Button((p + 1).ToString(), () => OnChange?.Invoke(page))
                {
                    Variant = p == Page ? ButtonVariant.Outline : ButtonVariant.Ghost,
                    Size = ButtonSize.Sm,
                }.MinWidth(h).Key("p" + p));
            }
            children.Add(Arrow("next", 0f, Page + 1, Page < PageCount - 1));
            return Row(children).Gap(t.Space(1)).Align(Align.Center);
        }

        /// <summary>Pages to show, -1 for a gap. At most 7, always with the first and last page.</summary>
        public static int[] Pages(int page, int count)
        {
            if (count <= 7)
            {
                var all = new int[Math.Max(0, count)];
                for (int i = 0; i < all.Length; i++) all[i] = i;
                return all;
            }
            if (page <= 3) return new[] { 0, 1, 2, 3, 4, -1, count - 1 };
            if (page >= count - 4) return new[] { 0, -1, count - 5, count - 4, count - 3, count - 2, count - 1 };
            return new[] { 0, -1, page - 1, page, page + 1, -1, count - 1 };
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Breadcrumb Breadcrumb(params Components.BreadcrumbItem[] items) => new(items);

        public static Components.BreadcrumbItem BreadcrumbItem(string label, Action? onClick = null) => new(label, onClick);

        public static Components.Pagination Pagination(int page, int pageCount, Action<int>? onChange) => new(page, pageCount, onChange);
    }
}
