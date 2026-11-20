using System;
using Rimact;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record ChatUI(ChatSessions Sessions, Action OnClose) : UIComponent
{
    public const float HeaderHeight = 34f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var sessions = Sessions;
        var session = UseExternal(() => sessions.Current);
        var opacity = UseExternal(() => HotfixMod.Settings.ChatOpacity);
        return Column(
            new ChatHeader(sessions, OnClose),
            Row().Height(1f).Bg(t.Border),
            new Transcript(session).Key(session.Id).Grow(),
            new QuestionPanel(session).Key("question" + session.Id),
            // 넘겨 보던 위치와 스크린샷 버튼은 대화마다 따로 둔다
            new Composer(session).Key("composer" + session.Id).Margin(new Edges(t.Space(3), 0f, t.Space(3), 0f)),
            new StatusBar(session)
        ).Bg(Theme.WithAlpha(t.Background, opacity)).Border(t.CardBorder);
    }
}
