using System;
using System.IO;
using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record Composer(ChatSession Session) : UIComponent
{
    public const string InputName = "hotfix.chat.input";

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var session = Session;
        var (busy, hasProfile, approval, asking) = UseExternal(() =>
            (session.Busy, session.Profile is not null, session.Approval is not null, session.Question is not null));
        var draft = UseExternal(() => session.Draft);
        // 켜 두면 보낼 때 화면을 찍어 붙인다
        var shot = UseState(false);
        // 보낸 글을 넘겨 보는 중이면 그 위치와 넘겨 보기 전에 쓰던 글
        var recall = UseRef(((int Index, string Stash)?)null);
        // 응답 중에 보내면 대기열에 들어간다. 질문이 떠 있을 때 글 없이 누르는 Enter는 질문 패널의 것이다
        var canSend = hasProfile && (draft.Trim().Length > 0 || shot.Value && !asking);
        // 승인을 기다리는 동안 입력란이 비어 있으면 Enter는 허용, Esc는 거부
        var answering = approval && !canSend;
        UseAcceptKey(canSend ? Send : answering ? () => session.Answer(true) : null);
        // 응답 중이면 Esc로 중단한다. 질문이 떠 있으면 Esc는 질문을 닫는다
        UseCancelKey(answering ? () => session.Answer(false) : busy && !asking ? session.Cancel : null);
        UseKeyDown(key => key.Key is (KeyCode.UpArrow or KeyCode.DownArrow) && !key.Shift && !key.Ctrl && !key.Alt
            && backend.FocusedControl == InputName && Recall(key.Key == KeyCode.UpArrow), beforeTextFields: true);

        var input = new ChatInput(draft, value =>
        {
            session.Draft = value;
            recall.Current = null;
        });
        var action = busy
            ? IconButton(Glyph.Stop, session.Cancel, "Hotfix_ChatStop".Translate(), ButtonVariant.Default)
            : IconButton(Glyph.ArrowUp, Send, "Hotfix_ChatSend".Translate(), ButtonVariant.Primary);
        if (!busy && !canSend)
            action = action.Disabled();
        return Column(
            Row(input.Grow(), action).Gap(t.Space(2)).Align(Align.End),
            IconButton(Glyph.Image, () => shot.Set(on => !on), "Hotfix_ChatShot".Translate(),
                shot.Value ? ButtonVariant.Primary : ButtonVariant.Ghost).AlignSelf(Align.Start)
        ).Gap(t.Space(1));

        async void Send()
        {
            var text = session.Draft.Trim();
            var capture = shot.Value;
            session.Draft = "";
            recall.Current = null;
            shot.Set(false);
            ChatImage? image = null;
            if (capture)
            {
                try
                {
                    image = await GameScreen.Capture(Path.Combine(session.Scratch, "screenshots"));
                }
                catch (Exception e)
                {
                    Logger.Warning($"Failed to take a screenshot: {e.Message}");
                }
            }
            if (text.Length > 0 || image is not null)
                session.Send(text, image);
        }

        // 위 키는 커서가 글의 맨 앞에 있을 때 앞서 보낸 글을 불러온다. 아래 키는 넘겨 보는 중에 커서가 마지막 줄에 있을 때 그 다음에 보낸 글을 불러온다
        bool Recall(bool older)
        {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (editor.hasSelection
                || (older ? editor.cursorIndex != 0 : recall.Current is null || editor.text.IndexOf('\n', editor.cursorIndex) >= 0))
                return false;
            var sent = session.Messages.OfType<ChatMessage_User>().Select(message => message.Text)
                .Concat(session.Queue.Select(queued => queued.Text)).Where(text => text.Length > 0).ToList();
            var next = (recall.Current?.Index ?? sent.Count) + (older ? -1 : 1);
            if (next < 0)
                return false;
            // 가장 최근 것을 지나면 쓰던 글로 돌아온다
            var stash = recall.Current?.Stash ?? session.Draft;
            session.Draft = next < sent.Count ? sent[next] : stash;
            recall.Current = next < sent.Count ? (next, stash) : null;
            // 같은 키를 다시 누르면 이어서 넘어가도록 커서를 둔다
            editor.text = session.Draft;
            editor.cursorIndex = editor.selectIndex = older ? 0 : session.Draft.Length;
            return true;
        }
    }
}

// 글의 줄 수에 맞춰 한 줄에서 MaxLines 줄까지 늘어나는 입력란
internal sealed record ChatInput(string Value, Action<string> OnChange) : UIComponent
{
    private const int MaxLines = 8;
    // 입력란 테두리와 글 사이의 좌우 여백
    private const float PadX = 16f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var width = UseLayout().Rect.width;
        var line = backend.MeasureText("A", FontSize.Small, 0f).y;
        // 둘째 줄부터 늘어나는 높이
        var step = backend.MeasureText("A\nA", FontSize.Small, 0f).y - line;
        // 끝의 빈 줄도 높이에 넣는다
        var text = width > PadX ? backend.MeasureText(Value + "A", FontSize.Small, width - PadX).y : line;
        var height = Mathf.Clamp(text, line, line + step * (MaxLines - 1)) + t.ControlHeight - line;
        return Textarea(Value, OnChange, height, "Hotfix_ChatPlaceholder".Translate()) with { ControlName = Composer.InputName };
    }
}
