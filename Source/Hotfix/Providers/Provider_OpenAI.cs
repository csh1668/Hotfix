using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.ServerSentEvents;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public class Provider_OpenAI : Provider_Base
{
    private const string StateId = "openai";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    public override string Id => StateId;
    public override string Label => "OpenAI";
    public override string DefaultBaseUrl => "https://api.openai.com";

    private static Dictionary<string, string> HeadersOf(LlmEndpoint endpoint) => new()
    {
        ["Authorization"] = "Bearer " + endpoint.ApiKey,
    };

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var stream = new StreamState(progress);
        var result = await Http.PostSseAsync(BaseUrlOf(request.Endpoint) / "v1/responses", HeadersOf(request.Endpoint),
            BuildBody(request), stream.OnEvent, IdleTimeout, ct).ConfigureAwait(false);
        if (stream.Error is { } error)
            return LlmResponse.Fail(error);
        return result.IsSuccess ? stream.ToResponse() : LlmResponse.Fail(ErrorOf(result));
    }

    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var result = await Http.GetAsync(BaseUrlOf(endpoint) / "v1/models", HeadersOf(endpoint), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ([], ErrorOf(result));
        return (JsonNode.Parse(result.Body)!["data"]!.AsArray().Select(model => (string)model!["id"]!).ToList(), null);
    }

    #region Request

    private static string BuildBody(LlmRequest request)
    {
        var reasoning = new JsonObject { ["summary"] = "auto" };
        if (request.Effort is { } effort)
            reasoning["effort"] = effort.ToString().ToLowerInvariant();
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["input"] = new JsonArray(request.Messages.SelectMany(ItemsOf).ToArray()),
            ["max_output_tokens"] = request.MaxOutputTokens,
            ["reasoning"] = reasoning,
            ["stream"] = true,
            // 대화를 서버에 두지 않고 매번 전체를 보낸다
            ["store"] = false,
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            body["instructions"] = request.SystemPrompt;
        if (request.Tools.Count > 0)
        {
            body["tools"] = request.Tools.ToJsonArray(ToolOf);
            if (ToolChoiceOf(request.ToolChoice) is { } choice)
                body["tool_choice"] = choice;
        }
        return body.ToJsonString();
    }

    // 사용자의 글과 이미지는 메시지 하나로 묶고, 그 밖의 파트는 각각 항목 하나가 된다
    private static IEnumerable<JsonNode> ItemsOf(LlmMessage message)
    {
        var content = new JsonArray();
        foreach (var part in message.Parts)
        {
            if (message.Role == LlmRole.User && ContentOf(part) is { } piece)
            {
                content.Add(piece);
                continue;
            }
            if (content.Count > 0)
            {
                yield return new JsonObject { ["role"] = "user", ["content"] = content };
                content = [];
            }
            if (ItemOf(part) is { } item)
                yield return item;
        }
        if (content.Count > 0)
            yield return new JsonObject { ["role"] = "user", ["content"] = content };
    }

    private static JsonNode? ContentOf(LlmPart part) => part switch
    {
        LlmPart_Text text => new JsonObject { ["type"] = "input_text", ["text"] = text.Text },
        LlmPart_Image image => new JsonObject
        {
            ["type"] = "input_image",
            ["image_url"] = $"data:{image.MediaType};base64,{image.Base64Data}",
        },
        _ => null,
    };

    // 이 API가 준 출력 항목은 받은 그대로 돌려보내야 추론이 이어진다. 다른 공급자의 추론은 보낼 수 없다
    private static JsonNode? ItemOf(LlmPart part) => part.StateFor(StateId) is { } raw ? JsonNode.Parse(raw) : part switch
    {
        LlmPart_Text text => new JsonObject { ["role"] = "assistant", ["content"] = text.Text },
        LlmPart_ToolCall call => new JsonObject
        {
            ["type"] = "function_call",
            ["call_id"] = call.Id,
            ["name"] = call.Name,
            ["arguments"] = call.ArgumentsJson.TryParseJson() is null ? "{}" : call.ArgumentsJson,
        },
        LlmPart_ToolResult result => new JsonObject
        {
            ["type"] = "function_call_output",
            ["call_id"] = result.ToolCallId,
            ["output"] = result.Content,
        },
        _ => null,
    };

    private static JsonObject ToolOf(LlmTool tool) => new()
    {
        ["type"] = "function",
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["parameters"] = tool.ParametersSchema.DeepClone(),
        // 생략하면 스키마를 strict 모드로 바꾸려고 한다
        ["strict"] = false,
    };

    private static JsonNode? ToolChoiceOf(LlmToolChoice choice) => choice.Mode switch
    {
        LlmToolChoiceMode.None => "none",
        LlmToolChoiceMode.Required => "required",
        LlmToolChoiceMode.Specific => new JsonObject { ["type"] = "function", ["name"] = choice.ToolName },
        _ => null,
    };

    #endregion

    #region Response

    private static LlmError ErrorOf(HttpResult result)
    {
        var error = result.ToLlmError();
        return result.Body.Contains("context_length_exceeded") || error.Message.Contains("context window")
            ? error with { Kind = LlmErrorKind.ContextTooLong }
            : error;
    }

    // 스트림 안에서 온 오류: {"code": ..., "message": ...}
    private static LlmError ErrorOf(JsonNode? error)
    {
        var code = error?["code"]?.ToString();
        return new LlmError(code switch
        {
            "rate_limit_exceeded" => LlmErrorKind.RateLimited,
            "server_error" => LlmErrorKind.Server,
            "context_length_exceeded" => LlmErrorKind.ContextTooLong,
            _ => LlmErrorKind.Unknown,
        }, error?["message"]?.ToString() ?? code ?? "Stream error");
    }

    private static bool IsPart(JsonNode? item) => (string?)item?["type"] is "message" or "reasoning" or "function_call";

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress)
    {
        // 파트가 되는 출력 항목의 output_index. 여기서의 순서가 PartIndex
        private readonly List<int> _order = [];
        private readonly Dictionary<int, string> _names = [];
        private JsonNode? _response;

        public LlmError? Error { get; private set; }

        public void OnEvent(SseItem<string> item)
        {
            if (item.Data.TryParseJson() is not JsonObject data)
                return;
            var index = (int?)data["output_index"] ?? -1;
            switch ((string?)data["type"])
            {
                case "response.output_item.added":
                    if (!IsPart(data["item"]))
                        break;
                    _order.Add(index);
                    if ((string?)data["item"]!["name"] is { } name)
                    {
                        _names[index] = name;
                        progress?.Report(new LlmStreamDelta_ToolCall(_order.Count - 1, name, ""));
                    }
                    break;
                case "response.output_text.delta" or "response.refusal.delta":
                    progress?.Report(new LlmStreamDelta_Text(_order.IndexOf(index), (string)data["delta"]!));
                    break;
                case "response.reasoning_summary_part.added":
                    // 요약은 문단 여러 개로 나뉘어 온다
                    if ((int?)data["summary_index"] > 0)
                        progress?.Report(new LlmStreamDelta_Reasoning(_order.IndexOf(index), "\n\n"));
                    break;
                case "response.reasoning_summary_text.delta":
                    progress?.Report(new LlmStreamDelta_Reasoning(_order.IndexOf(index), (string)data["delta"]!));
                    break;
                case "response.function_call_arguments.delta":
                    progress?.Report(new LlmStreamDelta_ToolCall(_order.IndexOf(index), _names.GetValueOrDefault(index, ""),
                        (string)data["delta"]!));
                    break;
                case "response.completed" or "response.incomplete":
                    _response = data["response"];
                    break;
                case "response.failed":
                    Error = ErrorOf(data["response"]?["error"]);
                    break;
                case "error":
                    Error = ErrorOf(data);
                    break;
            }
        }

        public LlmResponse ToResponse()
        {
            if (_response is null)
                return LlmResponse.Fail(new LlmError(LlmErrorKind.Network, "Stream ended before response.completed"));

            var output = (_response["output"] as JsonArray ?? []).Where(IsPart).Select(item => item!.AsObject()).ToList();
            var parts = output.Select(PartOf).Where(part => part is not null).Select(part => part!).ToList();
            var refused = output.Any(item => item["content"] is JsonArray content && content.Any(c => (string?)c!["type"] == "refusal"));
            var usage = _response["usage"];
            int cached = Details("input_tokens_details", "cached_tokens"), written = Details("input_tokens_details", "cache_write_tokens");
            return new LlmResponse
            {
                Message = new LlmMessage(LlmRole.Assistant, parts),
                StopReason = parts.OfType<LlmPart_ToolCall>().Any() ? LlmStopReason.ToolUse
                    : (string?)_response["incomplete_details"]?["reason"] switch
                    {
                        "max_output_tokens" => LlmStopReason.MaxTokens,
                        "content_filter" => LlmStopReason.ContentFiltered,
                        null => refused ? LlmStopReason.ContentFiltered : LlmStopReason.EndTurn,
                        _ => LlmStopReason.Unknown,
                    },
                // input_tokens는 캐시 읽기와 쓰기를 포함하고 output_tokens는 추론을 포함한다
                TokenUsage = usage is null ? null : new LlmTokenUsage(((int?)usage["input_tokens"] ?? 0) - cached - written,
                    (int?)usage["output_tokens"] ?? 0, cached, written),
                ModelId = (string?)_response["model"],
            };

            int Details(string group, string key) => (int?)usage?[group]?[key] ?? 0;
        }

        private static LlmPart? PartOf(JsonObject item)
        {
            item.Remove("created_by");
            var state = new LlmProviderState(StateId, item.ToJsonString(Extensions.RelaxedJson));
            switch ((string?)item["type"])
            {
                case "message":
                    var text = string.Concat((item["content"] as JsonArray ?? [])
                        .Select(content => (string?)content!["text"] ?? (string?)content["refusal"]));
                    return text.Length > 0 ? new LlmPart_Text(text) { ProviderState = state } : null;
                case "reasoning":
                    var summary = string.Join("\n\n", (item["summary"] as JsonArray ?? []).Select(s => (string?)s!["text"]));
                    return new LlmPart_Reasoning(summary.Length > 0 ? summary : null) { ProviderState = state };
                default:
                    return new LlmPart_ToolCall((string)item["call_id"]!, (string)item["name"]!,
                        (string?)item["arguments"] is { Length: > 0 } arguments ? arguments : "{}")
                    { ProviderState = state };
            }
        }
    }

    #endregion
}
