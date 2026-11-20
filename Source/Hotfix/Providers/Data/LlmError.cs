using System;

namespace Hotfix.Data;

public sealed record LlmError(LlmErrorKind Kind, string Message, int? HttpStatusCode = null, TimeSpan? RetryAfter = null)
{
    public bool IsRetryable => Kind is LlmErrorKind.Network or LlmErrorKind.Timeout or LlmErrorKind.RateLimited
        or LlmErrorKind.Overloaded or LlmErrorKind.Server;
}