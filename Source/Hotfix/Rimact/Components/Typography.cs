using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal enum TextTone : byte { Default, Muted, Link, Destructive, Success, Warning }

    /// <summary>Text colored from the theme by Tone. Wraps by default.</summary>
    internal sealed record Label(string Text) : UIComponent
    {
        public TextTone Tone { get; init; }
        public FontSize? Font { get; init; }
        public bool Wrap { get; init; } = true;

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Elements.Text(Text).Font(Font ?? t.Font).Wrap(Wrap).Truncate(!Wrap).Color(ToneColor(t, Tone)).Anchor(TextAlign.UpperLeft);
        }

        internal static Color ToneColor(Theme t, TextTone tone) => tone switch
        {
            TextTone.Muted => t.MutedForeground,
            TextTone.Link => t.Link,
            TextTone.Destructive => t.Destructive,
            TextTone.Success => t.Success,
            TextTone.Warning => t.Warning,
            _ => t.Foreground,
        };
    }

    /// <summary>Bold heading. Level 1 = heading size, 2 = title size, 3 = body size in the secondary color.</summary>
    internal sealed record Heading(string Text, int Level = 1) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            var text = Elements.Text(Text).Bold().Truncate();
            return Level switch
            {
                <= 1 => text.TextSize(t.TextHeading).Color(t.Foreground),
                2 => text.TextSize(t.TextTitle).Color(t.Foreground),
                _ => text.Color(t.SecondaryForeground),
            };
        }
    }

    /// <summary>Hairline. A label sits at the start with the line running on beside it.</summary>
    internal sealed record Separator(string? Text = null, bool Vertical = false) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            if (Vertical) return Row().Width(1).Bg(t.Border).AlignSelf(Align.Stretch);
            var line = Row().Height(1).Bg(t.Border);
            if (string.IsNullOrEmpty(Text)) return line.Margin(0, t.Space(1));
            return Row(Elements.Text(Text!).TextSize(t.TextCaption).Bold().Color(t.MutedForeground), line.Grow())
                .Gap(t.Space(2)).Align(Align.Center).Margin(0, t.Space(1.5f));
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Label Label(string text, Components.TextTone tone = Components.TextTone.Default) => new(text) { Tone = tone };
        public static Components.Label Muted(string text) => new(text) { Tone = Components.TextTone.Muted };
        public static Components.Heading Heading(string text, int level = 1) => new(text, level);
        public static Components.Separator Separator(string? label = null) => new(label);
        /// <summary>Stretches across the cross axis.</summary>
        public static Components.Separator VerticalSeparator() => new(null, true);
    }
}
