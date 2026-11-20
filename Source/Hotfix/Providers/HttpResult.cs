using System;

namespace Hotfix;

internal sealed record HttpResult(int StatusCode, string Body, TimeSpan? RetryAfter = null, Exception? Exception = null)
{
    public bool IsSuccess => Exception is null && StatusCode is >= 200 and < 300;
}