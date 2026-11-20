using System;
using System.Collections.Generic;
using System.Linq;
using Rimact;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// OnCancel이 있으면 아직 보내지 않은 입력
internal sealed record UserBubble(string Content, Action? OnCancel = null, IReadOnlyList<ChatImage>? Images = null) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var text = Text(Markdown.Literal(Content));
        if (OnCancel is not null)
            text = text.Color(t.MutedForeground);
        return Row(
            Spacer().MinWidth(t.Space(8)),
            OnCancel is null ? null : IconButton(Glyph.X, OnCancel, "Hotfix_ChatUnqueue".Translate()),
            Column(
            [
                Content.Length > 0 ? text.Wrap().Anchor(TextAlign.UpperLeft).Shrink() : null,
                // 함께 보낸 스크린샷
                .. (Images ?? []).Select(image => new ChatPicture(image.Path).Key(image.Path)),
            ]).Gap(t.Space(1)).Padding(t.Space(2.5f), t.Space(1.5f)).Bg(t.Surface).Border(t.Border).Shrink()
        ).Align(Align.Center);
    }
}
