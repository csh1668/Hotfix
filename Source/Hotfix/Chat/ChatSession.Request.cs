using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

internal sealed partial class ChatSession
{
    #region Call

    private async Task<LlmResponse> Call(LlmProfile profile, LlmRequest request, bool stream, CancellationToken ct)
    {
        var delay = TimeSpan.Zero;
        for (var attempt = 1; ; attempt++)
        {
            var live = _live = stream ? [] : null;
            Version++;
            IProgress<LlmStreamDelta>? progress = live is null ? null : new Progress<LlmStreamDelta>(delta =>
            {
                if (_live != live)
                    return;
                RetryAttempt = 0;
                Apply(live, delta);
                Version++;
            });

            LlmResponse response;
            try
            {
                // 첫 시도는 기다리지 않음
                await Task.Delay(delay, ct);
                response = profile.Provider is { } provider
                    ? await provider.CallAsync(request, progress, ct)
                    : LlmResponse.Fail(new LlmError(LlmErrorKind.InvalidRequest, $"Unknown provider: {profile.ProviderId}"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                response = LlmResponse.Fail(new LlmError(LlmErrorKind.Cancelled, "Cancelled"));
            }
            catch (Exception e)
            {
                response = LlmResponse.Fail(new LlmError(LlmErrorKind.Unknown, e.Message));
            }

            if (response.IsSuccess || response.Error is not { IsRetryable: true } error || attempt > MaxRetries
                || ct.IsCancellationRequested)
            {
                RetryAttempt = 0;
                return response;
            }
            RetryAttempt = attempt;
            delay = DelayOf(error, attempt);
        }
    }

    private LlmRequest Request(LlmProfile profile, IReadOnlyList<LlmMessage> messages) => profile.CreateRequest(messages) with
    {
        SystemPrompt = _systemPrompt,
        Tools = Tools.Where(tool => tool.Enabled).Select(tool => tool.Definition).ToList(),
    };

    private TimeSpan DelayOf(LlmError error, int attempt)
    {
        if (error.RetryAfter is { } wait)
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        var delay = Math.Min(RetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1), MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(delay * (1 + Random.Shared.NextDouble() * 0.25));
    }

    #endregion

    #region Request

    private List<LlmMessage> RequestMessages(int start, int end)
    {
        var request = new List<LlmMessage>();
        var dropped = Enumerable.Range(start, end - start).Sum(i => ImagesOf(_messages[i]).Count) - MaxRequestImages;
        for (var i = start; i < end; i++)
        {
            var images = ImagesOf(_messages[i]).Select(image => ImagePart(image, dropped-- > 0));
            var next = _messages[i] switch
            {
                ChatMessage_User user => new LlmMessage(LlmRole.User, [.. user.Text.Length > 0 ? [new LlmPart_Text(user.Text)] : Array.Empty<LlmPart>(), .. images]),
                ChatMessage_Meta meta => LlmMessage.User(meta.Text),
                ChatMessage_Summary summary => LlmMessage.User(Prompts.SummaryPrefix + summary.Text),
                ChatMessage_ToolResults tool => new LlmMessage(LlmRole.User, [.. tool.Results, .. images]),
                ChatMessage_Assistant assistant => assistant.Message,
                _ => null,
            };
            if (next is not null)
                Append(request, next);
        }
        return request;
    }

    private static LlmPart ImagePart(ChatImage image, bool dropped)
    {
        if (dropped)
            return new LlmPart_Text(Prompts.ImageDropped);
        return File.Exists(image.Path)
            ? new LlmPart_Image(image.MediaType, Convert.ToBase64String(File.ReadAllBytes(image.Path)))
            : new LlmPart_Text(Prompts.ImageMissing);
    }

    private static void Append(List<LlmMessage> request, LlmMessage next)
    {
        if (request.Count > 0 && request[^1].Role == next.Role)
            request[^1] = new LlmMessage(next.Role, [.. request[^1].Parts, .. next.Parts]);
        else
            request.Add(next);
    }

    #endregion

    #region Live

    private static void Apply(SortedDictionary<int, LivePart> live, LlmStreamDelta delta)
    {
        var (reasoning, text) = delta switch
        {
            LlmStreamDelta_Text t => (false, t.Text),
            LlmStreamDelta_Reasoning r => (true, r.Text),
            _ => (false, null),
        };
        if (text is null)
            return;
        if (!live.TryGetValue(delta.PartIndex, out var part))
            live[delta.PartIndex] = part = new LivePart(reasoning);
        part.Text.Append(text);
    }

    private sealed class LivePart(bool reasoning)
    {
        public readonly bool Reasoning = reasoning;
        public readonly StringBuilder Text = new();

        public LlmPart ToPart() => Reasoning ? new LlmPart_Reasoning(Text.ToString()) : new LlmPart_Text(Text.ToString());
    }

    #endregion
}
