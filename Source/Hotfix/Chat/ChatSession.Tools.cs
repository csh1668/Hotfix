using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

internal sealed record ToolApproval(string CallId, string Reason);

internal sealed record PendingQuestion(string CallId, IReadOnlyList<AskQuestion> Questions);

internal sealed partial class ChatSession
{
    private async Task<List<LlmPart_ToolResult>> RunTools(List<LlmPart_ToolCall> calls, CancellationToken ct)
    {
        var results = new List<LlmPart_ToolResult>();
        foreach (var call in calls)
        {
            string content;
            var isError = false;
            RunningCall = call;
            Version++;
            try
            {
                ct.ThrowIfCancellationRequested();
                var tool = Tools.FirstOrDefault(candidate => candidate.Enabled && candidate.Definition.Name == call.Name)
                    ?? throw new InvalidOperationException(Prompts.UnknownTool(call.Name));
                var arguments = JsonNode.Parse(call.ArgumentsJson) ?? new JsonObject();
                if (tool.NeedsApproval(arguments, Context) && !await Approved(call, ct))
                    throw new InvalidOperationException(Prompts.ToolDenied);
                content = Capped(call, await tool.RunAsync(arguments, Context, ct));
            }
            catch (Exception e)
            {
                content = e is OperationCanceledException ? Prompts.ToolInterrupted : e.Message;
                isError = true;
            }
            results.Add(new LlmPart_ToolResult(call.Id, call.Name, content, isError));
        }
        RunningCall = null;
        return results;
    }

    private async Task<bool> Approved(LlmPart_ToolCall call, CancellationToken ct)
    {
        var mode = HotfixMod.Settings.PermissionMode;
        if (mode == PermissionMode.Bypass)
            return true;
        var reason = "";
        if (mode == PermissionMode.Auto)
        {
            Judging = true;
            Version++;
            bool allow;
            try
            {
                (allow, reason) = await Judge(call, ct);
            }
            finally
            {
                Judging = false;
            }
            if (allow)
                return true;
        }

        ct.ThrowIfCancellationRequested();
        var answer = _answer = new TaskCompletionSource<bool>();
        Approval = new ToolApproval(call.Id, reason);
        Version++;
        using var cancel = ct.Register(() => answer.TrySetCanceled());
        try
        {
            return await answer.Task;
        }
        finally
        {
            Approval = null;
            Version++;
        }
    }

    private async Task<IReadOnlyList<IReadOnlyList<string>>?> Ask(IReadOnlyList<AskQuestion> questions, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reply = _reply = new TaskCompletionSource<IReadOnlyList<IReadOnlyList<string>>?>();
        Question = new PendingQuestion(RunningCall!.Id, questions);
        Version++;
        using var cancel = ct.Register(() => reply.TrySetCanceled());
        try
        {
            return await reply.Task;
        }
        finally
        {
            Question = null;
            Version++;
        }
    }

    private async Task<(bool Allow, string Reason)> Judge(LlmPart_ToolCall pending, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var message in _messages)
        {
            switch (message)
            {
                case ChatMessage_User user:
                    sb.Append($"User: {user.Text}\n\n");
                    break;
                case ChatMessage_Assistant assistant:
                    {
                        foreach (var call in assistant.Message.ToolCalls())
                        {
                            sb.Append(
                                $"{(call.Id == pending.Id ? "Pending tool call" : "Earlier tool call")} {call.Name}: {call.ArgumentsJson}\n\n");
                        }

                        break;
                    }
                // 질문에 대한 답은 사용자가 한 말이다
                case ChatMessage_ToolResults results:
                    foreach (var result in results.Results.Where(result => result.ToolName == "Ask"))
                        sb.Append($"User's answers to the assistant's questions:\n{result.Content}\n\n");
                    break;
            }
        }

        var profile = ProfileFor(LlmTask.Auto);
        var request = profile.CreateRequest([LlmMessage.User(sb.ToString())]) with { SystemPrompt = Prompts.Judge(Context) };
        var response = await Call(profile, request, false, ct);
        if (response.TokenUsage is { } usage)
            AddUsage(usage, profile);
        var verdict = response.IsSuccess ? response.Message!.Text().Trim() : "";
        if (verdict.StartsWith("ALLOW", StringComparison.OrdinalIgnoreCase))
            return (true, "");
        return (false, verdict.StartsWith("ASK", StringComparison.OrdinalIgnoreCase) ? verdict[3..].TrimStart(':', ' ') : "");
    }

    private string Capped(LlmPart_ToolCall call, string content)
    {
        if (content.Length <= MaxToolResult)
            return content;
        var file = Path.Combine(Context.Scratch, "tool-results", string.Join("_", call.Id.Split(Path.GetInvalidFileNameChars())) + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return content[..MaxToolResult] + Prompts.ToolTruncated(content.Length, file);
    }
}
