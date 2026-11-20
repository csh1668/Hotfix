namespace Hotfix.Data;

public abstract record LlmStreamDelta(int PartIndex);

public sealed record LlmStreamDelta_Text(int PartIndex, string Text) : LlmStreamDelta(PartIndex);
public sealed record LlmStreamDelta_Reasoning(int PartIndex, string Text) : LlmStreamDelta(PartIndex);
public sealed record LlmStreamDelta_ToolCall(int PartIndex, string Name, string ArgumentsJson) : LlmStreamDelta(PartIndex);