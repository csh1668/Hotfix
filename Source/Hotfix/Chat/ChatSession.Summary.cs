using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

internal sealed partial class ChatSession
{
    private int ContextTokens => _contextTokens + _messages.Skip(_contextIndex).Sum(Estimate);

    private bool NeedsSummary()
    {
        var window = _profile!.ContextWindow;
        return window > 0 && _summaryFailures < MaxSummaryFailures
            && ContextTokens >= window - Math.Min(SummaryReserve, window / 4);
    }

    private int Boundary() => Math.Max(0, _messages.FindLastIndex(message => message is ChatMessage_Summary));

    private async Task<bool> Summarize(CancellationToken ct, bool resume)
    {
        var start = Boundary();
        var end = _messages.FindLastIndex(message => message is ChatMessage_Assistant or ChatMessage_ToolResults) + 1;
        if (!_messages.Skip(start).Take(end - start).Any(message => message is ChatMessage_Assistant))
            return false;

        Summarizing = true;
        var profile = ProfileFor(LlmTask.Summary);
        LlmResponse response;
        for (var truncations = 0; ; truncations++)
        {
            var messages = RequestMessages(start, end);
            Append(messages, LlmMessage.User(Prompts.Summary));
            response = await Call(profile, Request(profile, messages), false, ct);
            if (response.Error?.Kind != LlmErrorKind.ContextTooLong || truncations == MaxSummaryTruncations)
                break;
            var next = _messages.FindIndex(start + Math.Max(1, (end - start) / 5), message => message is ChatMessage_User);
            if (next < 0 || next >= end)
                break;
            start = next;
        }
        Summarizing = false;

        if (response.TokenUsage is { } usage)
            AddUsage(usage, profile);
        var summary = response.IsSuccess ? SummaryOf(response.Message!.Text()) : "";
        if (summary.Length == 0)
        {
            if (!ct.IsCancellationRequested)
                _summaryFailures++;
            return false;
        }

        _summaryFailures = 0;
        _messages.Insert(end, new ChatMessage_Summary(summary));
        if (end + 1 < _messages.Count && _messages[end + 1] is ChatMessage_Meta { Text: Prompts.Continue })
            _messages[end + 1] = new ChatMessage_Meta(Prompts.Resume);
        else if (resume && end == _messages.Count - 1)
            _messages.Add(new ChatMessage_Meta(Prompts.Resume));
        _contextTokens = 0;
        _contextIndex = end;
        return true;
    }

    private static string SummaryOf(string text)
    {
        text = Regex_Analysis().Replace(text, "");
        var match = Regex_Summary().Match(text);
        return (match.Success ? match.Groups[1].Value : text).Trim();
    }

    private static int Estimate(ChatMessage message) => Encoding.UTF8.GetByteCount(message switch
    {
        ChatMessage_User user => user.Text,
        ChatMessage_Meta meta => meta.Text,
        ChatMessage_Summary summary => summary.Text,
        ChatMessage_ToolResults tool => string.Concat(tool.Results.Select(result => result.Content)),
        ChatMessage_Assistant assistant => assistant.Message.Text()
            + string.Concat(assistant.Message.ToolCalls().Select(call => call.ArgumentsJson)),
        _ => "",
    }) / 3 + ImagesOf(message).Count * ImageTokens;

    private static IReadOnlyList<ChatImage> ImagesOf(ChatMessage message) => message switch
    {
        ChatMessage_User { Images: { } images } => images,
        ChatMessage_ToolResults { Images: { } images } => images,
        _ => [],
    };

    [GeneratedRegex("<analysis>.*?</analysis>", RegexOptions.Singleline)]
    private static partial Regex Regex_Analysis();
    [GeneratedRegex("<summary>(.*)</summary>", RegexOptions.Singleline)]
    private static partial Regex Regex_Summary();
}
