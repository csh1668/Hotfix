using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Wrapping text with **bold**, `code` and [label](target) links. Blank line = new paragraph, "- " or "* " = bullet.</summary>
    /// <remarks>Every word is its own node, so keep it to notes, not long documents.</remarks>
    internal sealed record RichText(string Markup) : UIComponent
    {
        /// <summary>Called with the clicked link's target.</summary>
        public Action<string>? OnLink { get; init; }
        /// <summary>Theme foreground by default.</summary>
        public Color? Color { get; init; }

        private readonly record struct Word(string Text, bool Bold, bool Code, string? Link);

        protected internal override Node Build()
        {
            var t = UseTheme();
            var host = UseHost();
            var onLink = OnLink;
            var color = Color ?? t.Foreground;
            var blocks = new List<Node?>();
            var bullets = new List<Node?>();

            void FlushBullets()
            {
                if (bullets.Count == 0) return;
                blocks.Add(Column(bullets.ToArray()).Gap(t.Space(1)));
                bullets.Clear();
            }

            Node Flow(string text) => Row(Words(text).ConvertAll(w => (Node?)WordNode(w)).ToArray()).FlexWrap(lineGap: 0f).Align(Align.Center);

            Node WordNode(Word w)
            {
                if (w.Code)
                {
                    var trimmed = w.Text.TrimEnd();
                    return Row(Text(trimmed).TextSize(t.TextCaption + 1f).Color(color))
                        .Padding(t.Space(1), 0).Height(18).Align(Align.Center).Bg(t.Sunken).Border(t.Border)
                        .Margin(new Edges(0, 0, trimmed.Length < w.Text.Length ? t.Space(1) : 0, 0));
                }
                var text = Text(w.Text).Bold(w.Bold);
                if (w.Link is not { } target) return text.Color(color);
                return Row(text).Fg(t.Link).HoverFg(t.Hover).OnClick(() =>
                {
                    host.Backend.PlaySound(UiSound.Click);
                    onLink?.Invoke(target);
                });
            }

            var paragraph = new StringBuilder();
            void FlushParagraph()
            {
                if (paragraph.Length == 0) return;
                blocks.Add(Flow(paragraph.ToString()));
                paragraph.Clear();
            }

            foreach (var raw in (Markup ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    FlushParagraph();
                    FlushBullets();
                    continue;
                }
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    FlushParagraph();
                    bullets.Add(Row(Text("•").Color(t.MutedForeground).Width(t.Space(4)), Column(Flow(line.Substring(2))).Grow().Shrink()).Align(Align.Start));
                    continue;
                }
                FlushBullets();
                if (paragraph.Length > 0) paragraph.Append(' ');
                paragraph.Append(line);
            }
            FlushParagraph();
            FlushBullets();
            return Column(blocks.ToArray()).Gap(t.Space(2));
        }

        private static readonly Regex Spans = new(@"\*\*(.+?)\*\*|`(.+?)`|\[(.+?)\]\((.+?)\)", RegexOptions.Compiled);
        private static readonly Regex Tokens = new(@"\S+\s*", RegexOptions.Compiled);

        // Words keep their trailing space and the style of their span
        private static List<Word> Words(string text)
        {
            var words = new List<Word>();
            int at = 0;
            void Add(string span, bool bold, bool code, string? link)
            {
                // A span starting with a space splits from the previous word
                if (span.Length > 0 && char.IsWhiteSpace(span[0]) && words.Count > 0 && !char.IsWhiteSpace(words[^1].Text[^1]))
                    words[^1] = words[^1] with { Text = words[^1].Text + " " };
                if (code)
                {
                    words.Add(new Word(span, false, true, null));
                    return;
                }
                foreach (Match m in Tokens.Matches(span)) words.Add(new Word(m.Value, bold, false, link));
            }
            foreach (Match m in Spans.Matches(text))
            {
                if (m.Index > at) Add(text.Substring(at, m.Index - at), false, false, null);
                if (m.Groups[1].Success) Add(m.Groups[1].Value, true, false, null);
                else if (m.Groups[2].Success) Add(m.Groups[2].Value, false, true, null);
                else Add(m.Groups[3].Value, false, false, m.Groups[4].Value);
                at = m.Index + m.Length;
                // Space after a span stays with its last word
                if (at < text.Length && char.IsWhiteSpace(text[at]) && words.Count > 0)
                    words[^1] = words[^1] with { Text = words[^1].Text + " " };
            }
            if (at < text.Length) Add(text.Substring(at), false, false, null);
            return words;
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.RichText RichText(string markup) => new(markup);
    }
}
