using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

internal static class HttpResultExtensions
{
    public static LlmError ToLlmError(this HttpResult result) => result.Exception switch
    {
        null => new LlmError(KindOf(result.StatusCode), MessageOf(result), result.StatusCode, result.RetryAfter),
        TaskCanceledException { InnerException: TimeoutException } => new LlmError(LlmErrorKind.Timeout, "Timed out"),
        OperationCanceledException => new LlmError(LlmErrorKind.Cancelled, "Cancelled"),
        HttpRequestException e => new LlmError(LlmErrorKind.Network, e.Message),
        IOException e => new LlmError(LlmErrorKind.Network, e.Message),
        var e => new LlmError(LlmErrorKind.Unknown, $"{e.GetType().Name}: {e.Message}")
    };

    // 오류 본문은 {"error": {"message": "..."}} 이거나 {"error": "..."}
    private static string MessageOf(HttpResult result)
    {
        var error = (result.Body.TryParseJson() as JsonObject)?["error"];
        var message = error is JsonObject ? error["message"] : error;
        return message is JsonValue value && value.TryGetValue<string>(out var text)
            ? $"HTTP {result.StatusCode}: {text}"
            : $"HTTP {result.StatusCode}";
    }

    private static LlmErrorKind KindOf(int status) => status switch
    {
        401 or 403 => LlmErrorKind.Authentication,
        408 => LlmErrorKind.Timeout,
        429 => LlmErrorKind.RateLimited,
        503 or 529 => LlmErrorKind.Overloaded,
        >= 500 => LlmErrorKind.Server,
        >= 400 => LlmErrorKind.InvalidRequest,
        _ => LlmErrorKind.Unknown
    };
}