namespace Hotfix.Data;

public sealed record LlmToolChoice(LlmToolChoiceMode Mode, string? ToolName = null)
{
    public static readonly LlmToolChoice Auto = new(LlmToolChoiceMode.Auto);
    public static readonly LlmToolChoice None = new(LlmToolChoiceMode.None);
    public static readonly LlmToolChoice Required = new(LlmToolChoiceMode.Required);
    public static LlmToolChoice Force(string toolName) => new(LlmToolChoiceMode.Specific, toolName);
}