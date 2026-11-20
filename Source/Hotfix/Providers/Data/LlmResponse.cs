namespace Hotfix.Data;

public sealed class LlmResponse
{
    public LlmMessage? Message { get; init; }
    public LlmStopReason StopReason { get; init; }
    public LlmTokenUsage? TokenUsage { get; init; }
    public string? ModelId { get; init; }
    public LlmError? Error { get; init; }
    public string? RawBody { get; init; }

    public bool IsSuccess => Error is null && Message is not null;

    public static LlmResponse Fail(LlmError error) => new() { Error = error };
}