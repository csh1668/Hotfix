using System.Collections.Generic;

namespace Hotfix.Data;

public sealed record LlmRequest
{
    public required LlmEndpoint Endpoint { get; init; }
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public required IReadOnlyList<LlmMessage> Messages { get; init; }
    public IReadOnlyList<LlmTool> Tools { get; init; } = [];
    public LlmToolChoice ToolChoice { get; init; } = LlmToolChoice.Auto;
    public int MaxOutputTokens { get; init; } = 64000;
    public LlmEffort? Effort { get; init; }
    public IReadOnlyList<string> StopSequences { get; init; } = [];
}
