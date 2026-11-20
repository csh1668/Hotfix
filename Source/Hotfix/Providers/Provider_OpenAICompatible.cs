using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public class Provider_OpenAICompatible : Provider_Base
{
    private const string StateId = "openai-compatible";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    public override string Id => StateId;
    public override string Label => "OpenAI Compatible";
    public override string DefaultBaseUrl => "http://localhost:11434/v1";

    // 로컬 서버는 키가 없다
    private static Dictionary<string, string> HeadersOf(LlmEndpoint endpoint) => endpoint.ApiKey.Length > 0
        ? new() { ["Authorization"] = "Bearer " + endpoint.ApiKey }
        : [];

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var stream = new StreamState(progress);
        var result = await Http.PostSseAsync(BaseUrlOf(request.Endpoint) / "chat/completions", HeadersOf(request.Endpoint),
            BuildBody(request), stream.OnEvent, IdleTimeout, ct).ConfigureAwait(false);
        if (stream.Error is { } error)
            return LlmResponse.Fail(error);
        return result.IsSuccess ? stream.ToResponse() : LlmResponse.Fail(ErrorOf(result));
    }

    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var result = await Http.GetAsync(BaseUrlOf(endpoint) / "models", HeadersOf(endpoint), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ([], ErrorOf(result));
        return (JsonNode.Parse(result.Body)!["data"]!.AsArray().Select(model => (string)model!["id"]!).ToList(), null);
    }

    #region Request

    // 출력 한도는 서버마다 허용 범위가 달라서 보내지 않고 서버 기본값에 맡긴다
    private static string BuildBody(LlmRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        foreach (var message in request.Messages.SelectMany(MessagesOf))
            messages.Add(message);

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
        };
        if (request.Effort is { } effort)
            body["reasoning_effort"] = effort.ToString().ToLowerInvariant();
        if (request.StopSequences.Count > 0)
            body["stop"] = request.StopSequences.ToJsonArray(s => s);
        if (request.Tools.Count > 0)
        {
            body["tools"] = request.Tools.ToJsonArray(ToolOf);
            if (ToolChoiceOf(request.ToolChoice) is { } choice)
                body["tool_choice"] = choice;
        }
        return body.ToJsonString();
    }

    private static IEnumerable<JsonObject> MessagesOf(LlmMessage message)
    {
        if (message.Role == LlmRole.Assistant)
        {
            var assistant = new JsonObject { ["role"] = "assistant" };
            var text = message.Text();
            // 글 없이 도구만 부른 메시지의 content는 null
            assistant["content"] = text.Length > 0 ? text : null;
            // 추론 글은 받은 필드 이름으로 돌려보낸다. DeepSeek는 도구를 쓰는 대화에서 이것이 없으면 거부한다
            foreach (var reasoning in message.Parts.OfType<LlmPart_Reasoning>())
            {
                if (reasoning.StateFor(StateId) is { } field)
                    assistant[field] = reasoning.Summary;
            }
            var calls = message.ToolCalls().ToJsonArray(call => new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = call.ArgumentsJson.TryParseJson() is null ? "{}" : call.ArgumentsJson,
                },
            });
            if (calls.Count > 0)
                assistant["tool_calls"] = calls;
            yield return assistant;
            yield break;
        }

        // 도구 결과는 호출한 메시지 바로 뒤에 와야 한다
        foreach (var result in message.Parts.OfType<LlmPart_ToolResult>())
            yield return new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.ToolCallId, ["content"] = result.Content };

        var content = message.Parts.Where(part => part is LlmPart_Text or LlmPart_Image).ToList();
        if (content.Count == 0)
            yield break;
        yield return new JsonObject
        {
            ["role"] = "user",
            // 글뿐이면 배열을 받지 않는 서버를 위해 문자열로 보낸다
            ["content"] = content.All(part => part is LlmPart_Text)
                ? string.Join("\n", content.Select(part => ((LlmPart_Text)part).Text))
                : content.ToJsonArray(part => part is LlmPart_Image image
                    ? new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{image.Base64Data}" },
                    }
                    : new JsonObject { ["type"] = "text", ["text"] = ((LlmPart_Text)part).Text }),
        };
    }

    private static JsonObject ToolOf(LlmTool tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.ParametersSchema.DeepClone(),
        },
    };

    private static JsonNode? ToolChoiceOf(LlmToolChoice choice) => choice.Mode switch
    {
        LlmToolChoiceMode.None => "none",
        LlmToolChoiceMode.Required => "required",
        LlmToolChoiceMode.Specific => new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = choice.ToolName },
        },
        _ => null,
    };

    #endregion

    #region Response

    private static LlmError ErrorOf(HttpResult result)
    {
        var error = result.ToLlmError();
        return result.Body.Contains("context_length_exceeded") || error.Message.Contains("context length")
                                                               || error.Message.Contains("context window")
            ? error with { Kind = LlmErrorKind.ContextTooLong }
            : error;
    }

    private static LlmStopReason StopReasonOf(string? finishReason) => finishReason switch
    {
        "stop" => LlmStopReason.EndTurn,
        "length" => LlmStopReason.MaxTokens,
        "tool_calls" or "function_call" => LlmStopReason.ToolUse,
        "content_filter" => LlmStopReason.ContentFiltered,
        _ => LlmStopReason.Unknown,
    };

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress)
    {
        // 처음 나타난 순서가 PartIndex. Key는 추론 필드 이름, "content", 도구 호출의 index
        private readonly List<Slot> _slots = [];
        private JsonNode? _usage;
        private string? _model;
        private string? _finish;
        private bool _done;

        public LlmError? Error { get; private set; }

        public void OnEvent(SseItem<string> item)
        {
            if (item.Data == "[DONE]")
            {
                _done = true;
                return;
            }

            var data = JsonNode.Parse(item.Data)!;
            // OpenRouter는 스트림 도중의 오류를 청크에 실어 보낸다. code는 HTTP 상태 숫자이거나 문자열
            if (data["error"] is { } error)
            {
                Error = (error as JsonObject)?["code"] is JsonValue code && code.TryGetValue<int>(out var status)
                    ? new HttpResult(status, data.ToJsonString()).ToLlmError()
                    : new LlmError(LlmErrorKind.Unknown, (error as JsonObject)?["message"]?.ToString() ?? error.ToJsonString());
                return;
            }

            _usage = data["usage"] ?? _usage;
            _model = (string?)data["model"] ?? _model;
            if (data["choices"] is not JsonArray { Count: > 0 } choices)
                return;

            _finish = (string?)choices[0]!["finish_reason"] ?? _finish;
            if (choices[0]!["delta"] is not JsonObject delta)
                return;
            // 추론 글의 필드 이름은 서버마다 다르다
            var field = delta["reasoning_content"] is null ? "reasoning" : "reasoning_content";
            if ((string?)delta[field] is { Length: > 0 } reasoning)
            {
                var slot = SlotOf(field);
                slot.Text.Append(reasoning);
                progress?.Report(new LlmStreamDelta_Reasoning(_slots.IndexOf(slot), reasoning));
            }
            if ((string?)delta["content"] is { Length: > 0 } text)
            {
                var slot = SlotOf("content");
                slot.Text.Append(text);
                progress?.Report(new LlmStreamDelta_Text(_slots.IndexOf(slot), text));
            }
            foreach (var call in delta["tool_calls"] as JsonArray ?? [])
            {
                // 첫 조각에 id와 이름이 있고 뒤 조각은 index와 인자만 온다
                var slot = SlotOf(call!["index"]?.ToString() ?? (string?)call["id"] ?? "0");
                slot.Id ??= (string?)call["id"];
                slot.Name ??= (string?)call["function"]?["name"];
                var arguments = (string?)call["function"]?["arguments"] ?? "";
                slot.Text.Append(arguments);
                progress?.Report(new LlmStreamDelta_ToolCall(_slots.IndexOf(slot), slot.Name ?? "", arguments));
            }
        }

        public LlmResponse ToResponse()
        {
            // [DONE]을 보내지 않는 서버도 있다
            if (!_done && _finish is null)
                return LlmResponse.Fail(new LlmError(LlmErrorKind.Network, "Stream ended before finish_reason"));

            var parts = _slots.Select<Slot, LlmPart?>(slot => slot.Key switch
            {
                // 도구 호출 앞에 줄바꿈만 보내는 모델이 있다
                "content" => string.IsNullOrWhiteSpace(slot.Text.ToString()) ? null : new LlmPart_Text(slot.Text.ToString()),
                "reasoning_content" or "reasoning" => new LlmPart_Reasoning(slot.Text.ToString())
                { ProviderState = new LlmProviderState(StateId, slot.Key) },
                _ => new LlmPart_ToolCall(slot.Id is { Length: > 0 } id ? id : "call_" + Guid.NewGuid().ToString("N")[..12],
                    slot.Name ?? "", slot.Text.Length > 0 ? slot.Text.ToString() : "{}"),
            }).Where(part => part is not null).Select(part => part!).ToList();
            int cached = Details("cached_tokens"), written = Details("cache_write_tokens");
            return new LlmResponse
            {
                Message = new LlmMessage(LlmRole.Assistant, parts),
                StopReason = parts.OfType<LlmPart_ToolCall>().Any() ? LlmStopReason.ToolUse : StopReasonOf(_finish),
                // prompt_tokens는 캐시를 포함하고 completion_tokens는 추론을 포함한다. usage를 주지 않는 서버도 있다
                TokenUsage = _usage is null ? null : new LlmTokenUsage(((int?)_usage["prompt_tokens"] ?? 0) - cached - written,
                    (int?)_usage["completion_tokens"] ?? 0, cached, written),
                ModelId = _model,
            };

            int Details(string key) => (int?)_usage?["prompt_tokens_details"]?[key] ?? 0;
        }

        private Slot SlotOf(string key)
        {
            if (_slots.FirstOrDefault(candidate => candidate.Key == key) is not { } slot)
                _slots.Add(slot = new Slot(key));
            return slot;
        }

        private sealed class Slot(string key)
        {
            public string Key { get; } = key;
            public StringBuilder Text { get; } = new();
            public string? Id { get; set; }
            public string? Name { get; set; }
        }
    }

    #endregion
}
