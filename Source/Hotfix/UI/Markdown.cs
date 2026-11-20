using System.Collections.Generic;
using System.Text.RegularExpressions;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 문단마다 텍스트 노드 하나로 그린다. 굵게와 인라인 코드는 엔진의 리치 텍스트 태그로 바꾼다
internal sealed record Markdown(string Source) : UIComponent
{
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^(-{3,}|\*{3,}|_{3,})$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Spans = new(@"`(.+?)`|\*\*(.+?)\*\*|\[(.+?)\]\((.+?)\)", RegexOptions.Compiled);
    // 한 줄을 다 차지하는 그림. 경로에 괄호가 있을 수 있어서 줄 끝의 괄호까지를 경로로 본다
    private static readonly Regex Picture = new(@"^!\[(.*?)\]\((.+)\)$", RegexOptions.Compiled);

    protected internal override Node Build()
    {
        var t = UseTheme();
        var code = "#" + ColorUtility.ToHtmlStringRGB(t.Copper);
        var link = "#" + ColorUtility.ToHtmlStringRGB(t.Link);
        var blocks = new List<Node?>();
        var paragraph = new List<string>();
        var lines = Source.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```"))
            {
                FlushParagraph();
                var body = new List<string>();
                // 닫는 줄이 아직 안 온 스트리밍 중에는 끝까지 코드로 본다
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++)
                    body.Add(lines[i]);
                blocks.Add(new CodeBlock(trimmed[3..].Trim(), string.Join("\n", body)));
            }
            else if (trimmed.Length == 0)
            {
                FlushParagraph();
            }
            else if (Heading.Match(trimmed) is { Success: true } heading)
            {
                FlushParagraph();
                var text = Body(heading.Groups[2].Value).Bold();
                blocks.Add(heading.Groups[1].Length <= 2 ? text.TextSize(t.TextTitle) : text);
            }
            else if (Rule.IsMatch(trimmed))
            {
                FlushParagraph();
                blocks.Add(Separator());
            }
            else if (Picture.Match(trimmed) is { Success: true } picture && picture.Groups[2].Value.Trim('<', '>', ' ') is var file
                && ImageFile.IsImage(file))
            {
                FlushParagraph();
                blocks.Add(new ChatPicture(file, picture.Groups[1].Value));
            }
            else if (Bullet.Match(line) is { Success: true } bullet)
            {
                FlushParagraph();
                blocks.Add(ListItem("-", bullet.Groups[1].Length, bullet.Groups[2].Value));
            }
            else if (Numbered.Match(line) is { Success: true } numbered)
            {
                FlushParagraph();
                blocks.Add(ListItem(numbered.Groups[2].Value + ".", numbered.Groups[1].Length, numbered.Groups[3].Value));
            }
            else
            {
                paragraph.Add(trimmed);
            }
        }
        FlushParagraph();
        return Column(blocks).Gap(t.Space(2));

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
                return;
            blocks.Add(Body(string.Join("\n", paragraph)));
            paragraph.Clear();
        }

        TextNode Body(string text) => Text(Inline(text)).Wrap().Anchor(TextAlign.UpperLeft);

        Node ListItem(string marker, int indent, string text) => Row(
            Text(marker).Color(t.MutedForeground).Anchor(TextAlign.UpperLeft).Width(t.Space(6)),
            Body(text).Grow().Shrink()
        ).Align(Align.Start).Margin(new Edges(indent * t.Space(2), 0f, 0f, 0f));

        string Inline(string text) => Spans.Replace(Literal(text), span =>
            span.Groups[1].Success ? $"<color={code}>{span.Groups[1].Value}</color>"
            : span.Groups[2].Success ? $"<b>{span.Groups[2].Value}</b>"
            : $"<color={link}>{span.Groups[3].Value}</color>");
    }

    // 엔진이 태그로 읽지 않게 한다. "<<b></b>"는 "<" 한 글자로 그려진다
    public static string Literal(string text) => text.Replace("<", "<<b></b>");

    public static Node CopyButton(string text) =>
        Button("", () => GUIUtility.systemCopyBuffer = text, ButtonVariant.Ghost, ButtonSize.Sm, Glyph.Copy)
            .Tooltip("Hotfix_ChatCopy".Translate());
}
