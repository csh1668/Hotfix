namespace Hotfix.Data;

public enum LlmStopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    StopSequence,
    ContentFiltered,
    Unknown
}
