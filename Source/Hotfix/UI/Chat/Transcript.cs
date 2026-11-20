using System.Collections.Generic;
using System.Linq;
using Hotfix.Data;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record Transcript(ChatSession Session) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var session = Session;
        UseExternal(() => session.Version);
        var hasProfile = UseExternal(() => session.Profile is not null);

        var messages = session.Messages;
        var copies = Copies(messages, session.Busy);
        var rows = new List<Node?>();
        for (var i = 0; i < messages.Count; i++)
        {
            // 도구 결과는 그 도구를 부른 응답의 바로 다음 항목이다
            var results = i + 1 < messages.Count ? messages[i + 1] as ChatMessage_ToolResults : null;
            Node? row = messages[i] switch
            {
                ChatMessage_User user => new UserBubble(user.Text, null, user.Images).Memo(),
                ChatMessage_Assistant assistant => new AssistantView(assistant.Message, results?.Results)
                {
                    Images = results?.Images,
                    Copy = copies.GetValueOrDefault(i),
                    Approval = session.Approval is { } waiting && assistant.Message.ToolCalls().Any(call => call.Id == waiting.CallId)
                        ? waiting : null,
                    OnAnswer = session.Answer,
                    RunningId = session.RunningCall is { } running && assistant.Message.ToolCalls().Any(call => call.Id == running.Id)
                        ? running.Id : null,
                }.Memo(),
                ChatMessage_Summary summary => Collapsible("Hotfix_ChatSummary".Translate(), Detail(t, summary.Text)),
                ChatMessage_Notice notice => Text(("Hotfix_ChatNotice_" + notice.Kind).Translate()).TextSize(t.TextCaption)
                    .Color(t.MutedForeground),
                ChatMessage_Error error => Alert(null, error.Error.Message, AlertVariant.Destructive),
                // Meta는 모델에게만 보내고, ToolResults는 호출과 함께 그린다
                _ => null,
            };
            if (row is not null)
                rows.Add(row.Key(i));
        }
        if (session.Live is { } live)
        {
            // 완료되면 같은 자리의 메시지가 되므로 키를 맞춰 둔다
            rows.Add(new AssistantView(live, null).Key(messages.Count));
        }
        if (session.Busy)
        {
            rows.Add(new WorkingLine(session));
        }
        else if (session.Error is not null)
        {
            rows.Add(Button("Hotfix_ChatRetry".Translate(), session.Retry, size: ButtonSize.Sm).AlignSelf(Align.Start));
        }
        for (var i = 0; i < session.Queue.Count; i++)
        {
            var index = i;
            rows.Add(new UserBubble(session.Queue[i].Text, () => session.Unqueue(index), session.Queue[i].Image is { } image ? [image] : null)
                .Key("queued" + i));
        }

        return !hasProfile ? Hint("Hotfix_ChatNoProfile".Translate())
            : rows.Count == 0 ? Hint("Hotfix_ChatEmpty".Translate())
            : new ScrollArea(Column(rows).Gap(t.Space(4)).Padding(t.Space(3))) { StickToBottom = true, JumpToBottom = true };

        Node Hint(string text) => Text(text).Color(t.MutedForeground).Wrap().Anchor(TextAlign.MiddleCenter)
            .Margin(t.Space(6), 0f);
    }

    // 복사 버튼은 차례마다 하나, 그 차례의 마지막 응답에 둔다. 진행 중인 차례에는 없다
    // 키는 그 응답의 위치, 값은 그 차례의 응답 글 전체
    private static Dictionary<int, string> Copies(IReadOnlyList<ChatMessage> messages, bool busy)
    {
        var copies = new Dictionary<int, string>();
        var texts = new List<string>();
        var last = 0;
        for (var i = 0; i <= messages.Count; i++)
        {
            var message = i < messages.Count ? messages[i] : null;
            if (message is ChatMessage_Assistant assistant)
            {
                if (assistant.Message.Text() is { Length: > 0 } text)
                    texts.Add(text);
                last = i;
            }
            else if (message is null or ChatMessage_User)
            {
                if (texts.Count > 0 && (message is not null || !busy))
                    copies[last] = string.Join("\n\n", texts);
                texts.Clear();
            }
        }
        return copies;
    }

    // 접힌 구역 안에 보여 주는 글. full이 아니면 긴 글은 잘라서 보여 준다
    public static Node Detail(Theme t, string text, bool error = false, bool full = false)
    {
        return full ? Render(text) : new Clipped(text, Render) { Tail = error };

        Node Render(string shown) => Text(Markdown.Literal(shown))
            .Color(error ? t.Destructive : t.MutedForeground).Wrap().Anchor(TextAlign.UpperLeft)
            .Margin(new Edges(t.Space(2), 0f, 0f, t.Space(1)));
    }
}

// 지금 하는 일과 이번 차례의 경과 시간
internal sealed record WorkingLine(ChatSession Session) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var session = Session;
        UseExternal(() => session.Version);
        var tick = UseState(0);
        UseInterval(() => tick.Set(value => value + 1), 1f);

        var phase = session.RetryAttempt > 0
            ? "Hotfix_ChatRetrying".Translate(session.RetryAttempt.ToString(), ChatSession.MaxRetries.ToString())
            : session.Approval is not null ? "Hotfix_ChatApproval".Translate()
            : session.Question is not null ? "Hotfix_ChatAsking".Translate()
            : session.Judging ? "Hotfix_ChatJudging".Translate()
            : session.RunningCall is { } call ? "Hotfix_ChatRunning".Translate(call.Name)
            : session.Summarizing ? "Hotfix_ChatSummarizing".Translate()
            : session.Live?.Parts.LastOrDefault() switch
            {
                LlmPart_Reasoning => "Hotfix_ChatThinking".Translate(),
                LlmPart_Text => "Hotfix_ChatWriting".Translate(),
                _ => "Hotfix_ChatWorking".Translate(),
            };
        var elapsed = session.Elapsed;
        var time = elapsed.TotalMinutes >= 1
            ? "Hotfix_ChatMinutes".Translate(((int)elapsed.TotalMinutes).ToString(), elapsed.Seconds.ToString("00"))
            : "Hotfix_ChatSeconds".Translate(elapsed.Seconds.ToString());
        // 승인이나 답을 기다릴 때는 Esc가 거부나 건너뛰기라서 안내하지 않는다
        var line = session.Approval is null && session.Question is null
            ? $"{phase} · {time} · {"Hotfix_ChatStopHint".Translate()}"
            : $"{phase} · {time}";
        return Row(Spinner(14f), Text(line).Color(t.MutedForeground).Truncate().Shrink()).Gap(t.Space(2)).Align(Align.Center);
    }
}
