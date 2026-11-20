using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public abstract class Provider_Cli : Provider_Base
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private protected static string Folder => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Hotfix", "cli")).FullName;

    private protected Task<CliResult> RunAsync(LlmEndpoint endpoint, IReadOnlyList<string> arguments, CliInput input,
        Action<string> onLine, CancellationToken ct) =>
        Cli.RunAsync(BaseUrlOf(endpoint), arguments, input, Folder, onLine, IdleTimeout, ct);

    private protected Task<CliResult> RunAsync(LlmEndpoint endpoint, IReadOnlyList<string> arguments, string input,
        Action<string> onLine, CancellationToken ct)
    {
        var all = new CliInput();
        all.Write(input);
        all.Close();
        return RunAsync(endpoint, arguments, all, onLine, ct);
    }

    private protected static LlmResponse ResponseOf(CliReply reply, LlmStopReason stopReason, LlmTokenUsage? usage, string? model)
    {
        var parts = reply.Finish();
        return new LlmResponse
        {
            Message = new LlmMessage(LlmRole.Assistant, parts),
            StopReason = parts.OfType<LlmPart_ToolCall>().Any() ? LlmStopReason.ToolUse : stopReason,
            TokenUsage = usage,
            ModelId = model,
        };
    }

    private protected LlmError ErrorOf(CliResult result) => result.Exception switch
    {
        null => new LlmError(LlmErrorKind.Unknown, result.Error.Trim() is { Length: > 0 } error
            ? error
            : $"{Label} exited with code {result.ExitCode}"),
        TaskCanceledException { InnerException: TimeoutException } => new LlmError(LlmErrorKind.Timeout, "Timed out"),
        OperationCanceledException => new LlmError(LlmErrorKind.Cancelled, "Cancelled"),
        Win32Exception e => new LlmError(LlmErrorKind.InvalidRequest, e.Message),
        var e => new LlmError(LlmErrorKind.Unknown, $"{e.GetType().Name}: {e.Message}"),
    };

    private protected static LlmError ErrorOf(string message, int? status = null)
    {
        var kind = status is 401 or 403 ? LlmErrorKind.Authentication
            : message.Contains("too long", StringComparison.OrdinalIgnoreCase)
              || message.Contains("context window", StringComparison.OrdinalIgnoreCase)
              || message.Contains("context length", StringComparison.OrdinalIgnoreCase) ? LlmErrorKind.ContextTooLong
            : LlmErrorKind.Unknown;
        return new LlmError(kind, message, status);
    }
}
