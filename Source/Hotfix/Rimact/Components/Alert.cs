using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal enum AlertVariant : byte { Default, Destructive, Warning, Success }

    /// <summary>Callout with an icon, title and description. Variant picks the accent color.</summary>
    internal sealed record Alert(string? Title, string? Description) : UIComponent
    {
        public AlertVariant Variant { get; init; }
        public Icon? Icon { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var accent = Variant switch
            {
                AlertVariant.Destructive => t.Destructive,
                AlertVariant.Warning => t.Warning,
                AlertVariant.Success => t.Success,
                _ => t.Info,
            };
            var icon = Icon ?? (Variant == AlertVariant.Default ? BuiltinIcon.Info : BuiltinIcon.Warning);
            return Row(
                    Row().Width(3).AlignSelf(Align.Stretch).Bg(accent),
                    Row(
                            Image(icon).Size(18, 18).Tint(accent).Margin(new Edges(0, 1, 0, 0)),
                            Column(
                                    string.IsNullOrEmpty(Title) ? null : Elements.Text(Title!).Bold().Color(Variant == AlertVariant.Default ? t.Foreground : accent),
                                    string.IsNullOrEmpty(Description) ? null : Elements.Text(Description!).Wrap().Color(t.SecondaryForeground).Anchor(TextAlign.UpperLeft))
                                .Gap(2).Grow().Shrink())
                        .Gap(t.Space(2.5f)).Padding(new Edges(t.Space(3), t.Space(2.5f), t.Space(3), t.Space(2.5f))).Align(Align.Start).Grow().Shrink())
                .Bg(new Color(accent.r, accent.g, accent.b, 0.07f)).Radius(t.Radius);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Alert Alert(string? title, string? description = null, Components.AlertVariant variant = Components.AlertVariant.Default) =>
            new(title, description) { Variant = variant };
    }
}
