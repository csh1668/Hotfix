namespace Hotfix.Data;

public abstract record LlmPart
{
    public LlmProviderState? ProviderState { get; init; }

    public string? StateFor(string providerId) =>
        ProviderState?.ProviderId == providerId ? ProviderState.Data : null;
}

public sealed record LlmPart_Text(string Text) : LlmPart;
public sealed record LlmPart_Image(string MediaType, string Base64Data) : LlmPart;
public sealed record LlmPart_ToolCall(string Id, string Name, string ArgumentsJson) : LlmPart;
public sealed record LlmPart_ToolResult(string ToolCallId, string ToolName, string Content, bool IsError = false) : LlmPart;
public sealed record LlmPart_Reasoning(string? Summary) : LlmPart;