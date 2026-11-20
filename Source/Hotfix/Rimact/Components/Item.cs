using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>List row: media on the left, title over description, actions on the right.</summary>
    internal sealed record Item(string Title) : UIComponent
    {
        public string? Description { get; init; }
        public Icon? Icon { get; init; }
        public Color? IconTint { get; init; }
        /// <summary>Any node in place of Icon, e.g. a pawn portrait.</summary>
        public Node? Media { get; init; }
        public Node? Actions { get; init; }
        public Action? OnClick { get; init; }
        public bool Selected { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            Node? media = Media ?? (Icon is null ? null : new IconWell(Icon) { Tint = IconTint, Size = 30f });
            var body = Column(
                    Elements.Text(Title).Color(t.Foreground).Truncate(),
                    string.IsNullOrEmpty(Description) ? null : Elements.Text(Description!).TextSize(t.TextCaption).Color(t.MutedForeground).Truncate())
                .Gap(1).Justify(Justify.Center).Grow().Shrink();
            var row = Row(media, body, Actions)
                .Gap(t.Space(2.5f)).Padding(t.Space(2.5f), t.Space(1.5f)).Align(Align.Center)
                .Visual(new Visual { Overlay = Selected ? t.SelectedOverlay : null });
            if (OnClick is { } click) row = row.HoverOverlay(Selected ? Theme.WithAlpha(t.SelectedOverlay, t.SelectedOverlay.a + 0.06f) : t.HoverOverlay).OnClick(click);
            if (!Selected) return row;
            return Stack(row, Row().Width(2).FillHeight().Bg(t.Indicator).AlignSelf(Align.Start));
        }
    }

    /// <summary>Empty state: icon, title, description and an optional action, centered.</summary>
    internal sealed record Empty(string Title) : UIComponent
    {
        public string? Description { get; init; }
        public Icon? Icon { get; init; } = BuiltinIcon.Search;
        public Node? Action { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Column(
                    Icon is null ? null : new IconWell(Icon) { Size = 44f, Tint = t.MutedForeground },
                    Elements.Text(Title).TextSize(t.TextTitle).Bold().Color(t.SecondaryForeground).Anchor(TextAlign.MiddleCenter),
                    string.IsNullOrEmpty(Description) ? null
                        : Elements.Text(Description!).Color(t.MutedForeground).Wrap().Anchor(TextAlign.UpperCenter).MaxWidth(360),
                    Action?.Margin(new Edges(0, t.Space(1), 0, 0)))
                .Gap(t.Space(1.5f)).Align(Align.Center).Justify(Justify.Center).Padding(t.Space(6));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Item Item(string title, string? description = null, Icon? icon = null, Node? actions = null, Action? onClick = null) =>
            new(title) { Description = description, Icon = icon, Actions = actions, OnClick = onClick };

        public static Components.Empty Empty(string title, string? description = null, Node? action = null) =>
            new(title) { Description = description, Action = action };
    }
}
