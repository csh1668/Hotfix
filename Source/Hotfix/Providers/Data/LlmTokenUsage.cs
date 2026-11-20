namespace Hotfix.Data;

// InputTokens는 캐시 읽기/쓰기를 뺀 값, OutputTokens는 추론 토큰을 포함한 값
public sealed record LlmTokenUsage(int InputTokens, int OutputTokens, int CacheReadTokens = 0, int CacheWriteTokens = 0)
{
    public static LlmTokenUsage operator +(LlmTokenUsage left, LlmTokenUsage right)
    {
        return new LlmTokenUsage(
            left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens,
            left.CacheReadTokens + right.CacheReadTokens,
            left.CacheWriteTokens + right.CacheWriteTokens
        );
    }
}