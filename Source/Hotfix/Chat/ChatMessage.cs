using System.Collections.Generic;
using Hotfix.Data;

namespace Hotfix;

internal enum ChatNotice
{
    Interrupted,
    OutputLimit,
    Refused,
}

internal abstract record ChatMessage;

internal sealed record ChatMessage_User(string Text, IReadOnlyList<ChatImage>? Images = null) : ChatMessage;

internal sealed record ChatMessage_Assistant(LlmMessage Message) : ChatMessage;

internal sealed record ChatMessage_ToolResults(IReadOnlyList<LlmPart_ToolResult> Results, IReadOnlyList<ChatImage>? Images = null)
    : ChatMessage;

internal sealed record ChatMessage_Summary(string Text) : ChatMessage;

// 모델에게만 보임
internal sealed record ChatMessage_Meta(string Text) : ChatMessage;

// 화면에만 보임
internal sealed record ChatMessage_Notice(ChatNotice Kind) : ChatMessage;

// 화면에만 보임
internal sealed record ChatMessage_Error(LlmError Error) : ChatMessage;
