using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal enum BadgeVariant : byte { Default, Secondary, Outline, Destructive, Success, Warning }

    /// <summary>Small tag for a status, a language or a count.</summary>
    internal sealed record Badge(string Text) : UIComponent
    {
        public BadgeVariant Variant { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var (bg, border, fg) = Variant switch
            {
                BadgeVariant.Secondary => ((Color?)new Color(1f, 1f, 1f, 0.08f), (Color?)null, t.SecondaryForeground),
                BadgeVariant.Outline => (null, t.BorderStrong, t.SecondaryForeground),
                BadgeVariant.Destructive => (Tint(t.Destructive), null, t.Destructive),
                BadgeVariant.Success => (Tint(t.Success), null, t.Success),
                BadgeVariant.Warning => (Tint(t.Warning), null, t.Warning),
                _ => (Tint(t.Indicator), null, t.Indicator),
            };
            const float h = 18f;
            var box = Row(Elements.Text(Text).TextSize(t.TextCaption).Bold().Anchor(TextAlign.MiddleCenter))
                .Height(h).Padding(t.Space(2), 0).Align(Align.Center).AlignSelf(Align.Center)
                .Radius(t.RadiusSm).Fg(fg);
            if (bg is { } b) box = box.Bg(b);
            return border is { } line ? box.Border(line) : box;
        }

        private static Color Tint(Color c) => new(c.r, c.g, c.b, 0.16f);
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Badge Badge(string text, Components.BadgeVariant variant = Components.BadgeVariant.Default) => new(text) { Variant = variant };
    }
}
