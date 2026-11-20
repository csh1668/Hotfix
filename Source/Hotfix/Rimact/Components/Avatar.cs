using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Key cap for a key or part of a shortcut.</summary>
    internal sealed record Kbd(string Keys) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            return Row(Text(Keys).TextSize(t.TextCaption).Bold().Anchor(TextAlign.MiddleCenter).Color(t.SecondaryForeground))
                .Height(20).MinWidth(20).Padding(t.Space(1.5f), 0).Align(Align.Center).Justify(Justify.Center).AlignSelf(Align.Center)
                .Bg(t.SurfaceRaised).Radius(t.RadiusSm).Bevel(t.Raised(t.SurfaceRaised, 1f)).Border(t.Outline);
        }
    }

    /// <summary>Square with a picture, or the initials of Name.</summary>
    internal sealed record Avatar(string Name) : UIComponent
    {
        public Icon? Image { get; init; }
        public float Size { get; init; } = 32f;
        /// <summary>Defaults to a mineral picked from the name, so a name always gets the same color.</summary>
        public Color? Color { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            if (Image != null)
                return Row(Elements.Image(Image).Size(Size - 2, Size - 2)).Size(Size, Size).Center().Bg(t.Sunken).Border(t.Outline).Radius(t.Radius);
            var minerals = t.Minerals;
            var c = Color ?? minerals[StableHash(Name) % minerals.Length];
            return Row(Text(Initials(Name)).TextSize(Mathf.Round(Size * 0.4f)).Bold().Anchor(TextAlign.MiddleCenter).Color(c))
                .Size(Size, Size).Center().Bg(Theme.WithAlpha(c, 0.18f)).Border(Theme.WithAlpha(c, 0.45f)).Radius(t.Radius);
        }

        /// <summary>First letters of the first and last words, upper-cased. "?" for a blank name.</summary>
        public static string Initials(string name)
        {
            var words = (name ?? "").Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            var first = char.ToUpperInvariant(words[0][0]).ToString();
            return words.Length == 1 ? first : first + char.ToUpperInvariant(words[^1][0]);
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in s ?? "") h = h * 31 + ch;
                return h & int.MaxValue;
            }
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Kbd Kbd(string keys) => new(keys);

        public static Components.Avatar Avatar(string name, Icon? image = null, float size = 32f) => new(name) { Image = image, Size = size };
    }
}
