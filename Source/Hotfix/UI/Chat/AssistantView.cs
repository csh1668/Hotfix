using System;
using System.Collections.Generic;
using System.Linq;
using Hotfix.Data;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record AssistantView(LlmMessage Message, IReadOnlyList<LlmPart_ToolResult>? Results) : UIComponent
{
    public IReadOnlyList<ChatImage>? Images { get; init; }
    public string? Copy { get; init; }
    public ToolApproval? Approval { get; init; }
    public Action<bool>? OnAnswer { get; init; }
    // 지금 처리 중인 호출의 id
    public string? RunningId { get; init; }

    protected internal override Node Build()
    {
        var t = UseTheme();
        var parts = new List<Node?>();
        for (var i = 0; i < Message.Parts.Count; i++)
        {
            switch (Message.Parts[i])
            {
                case LlmPart_Reasoning { Summary: { Length: > 0 } summary }:
                    parts.Add(Collapsible("Hotfix_ChatReasoning".Translate(), Transcript.Detail(t, summary)).Key(i));
                    break;
                case LlmPart_Text { Text.Length: > 0 } reply:
                    parts.Add(new Markdown(reply.Text).Key(i).Memo());
                    break;
                case LlmPart_ToolCall call:
                    parts.Add(ToolCall(call, Results?.FirstOrDefault(result => result.ToolCallId == call.Id)).Key(i));
                    break;
            }
        }
        foreach (var image in Images ?? [])
            parts.Add(new ChatPicture(image.Path).Key(image.Path));
        if (Copy is not null)
            parts.Add(Markdown.CopyButton(Copy).AlignSelf(Align.Start));
        return Column(parts).Gap(t.Space(2));

        Node ToolCall(LlmPart_ToolCall call, LlmPart_ToolResult? result)
        {
            var arguments = ToolLine.Arguments(call.ArgumentsJson);
            var title = ToolLine.Title(call, arguments);
            // 승인할 호출은 인자를 자르지 않고 다 보여 줌
            var input = ToolLine.Body(t, call, arguments, Approval?.CallId == call.Id);
            if (Approval?.CallId == call.Id)
            {
                return Column(
                    Text(title).Truncate(),
                    Approval.Reason.Length > 0 ? Text(Markdown.Literal(Approval.Reason)).Color(t.MutedForeground).Wrap() : null,
                    input,
                    Row(
                        Button("Hotfix_ChatAllow".Translate(), () => OnAnswer?.Invoke(true), ButtonVariant.Primary, ButtonSize.Sm),
                        Button("Hotfix_ChatDeny".Translate(), () => OnAnswer?.Invoke(false), size: ButtonSize.Sm)
                    ).Gap(t.Space(2))
                ).Gap(t.Space(2)).Padding(t.Space(2)).Border(t.Border);
            }
            var summary = ToolLine.Summary(call, arguments, result);
            var line = Collapsible(title, Column(
                input,
                result is null ? null : Separator(),
                result is null ? null : Transcript.Detail(t, result.Content, result.IsError)
            )) with
            {
                Extra = Row(
                    summary is null ? null : Text(summary).TextSize(t.TextCaption).Color(t.MutedForeground),
                    result is { IsError: true } ? Image(Glyph.Alert).Size(14f, 14f).Tint(t.Destructive)
                    : result is null && RunningId == call.Id ? Spinner(14f) : null
                ).Gap(t.Space(2)).Align(Align.Center),
            };
            // 질문에 한 답은 사용자의 말이라서 접지 않고 보여 준다
            return ToolLine.Reply(call, result) is { } reply ? Column(line, new UserBubble(reply)).Gap(t.Space(2)) : line;
        }
    }
}
