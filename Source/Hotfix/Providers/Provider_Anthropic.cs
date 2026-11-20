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

public class Provider_Anthropic : Provider_Base
{
    private const string StateId = "anthropic";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    public override string Id => StateId;
    public override string Label => "Anthropic";
    public override string DefaultBaseUrl => "https://api.anthropic.com";

    private static Dictionary<string, string> HeadersOf(LlmEndpoint endpoint) => new()
    {
        ["x-api-key"] = endpoint.ApiKey,
        ["anthropic-version"] = "2026-10-21",
    };

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var stream = new StreamState(progress);
        var result = await Http.PostSseAsync(BaseUrlOf(request.Endpoint) / "v1/messages", HeadersOf(request.Endpoint),
            BuildBody(request), stream.OnEvent, IdleTimeout, ct).ConfigureAwait(false);
        if (stream.Error is { } error)
            return LlmResponse.Fail(error);
        return result.IsSuccess ? stream.ToResponse() : LlmResponse.Fail(ErrorOf(result));
    }

    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var models = new List<string>();
        var headers = HeadersOf(endpoint);
        string? afterId = null;
        while (true)
        {
            var query = afterId is null ? "" : "&after_id=" + Uri.EscapeDataString(afterId);
            var result = await Http.GetAsync(BaseUrlOf(endpoint) / ("v1/models?limit=1000" + query), headers, ct)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return ([], ErrorOf(result));

            var page = JsonNode.Parse(result.Body)!;
            models.AddRange(page["data"]!.AsArray().Select(model => (string)model!["id"]!));
            if ((bool?)page["has_more"] != true)
                return (models, null);
            afterId = (string?)page["last_id"];
        }
    }

    #region Request

    private static string BuildBody(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["stream"] = true,
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            ["thinking"] = new JsonObject { ["type"] = "adaptive", ["display"] = "summarized" },
            ["messages"] = request.Messages.ToJsonArray(MessageOf),
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            body["system"] = request.SystemPrompt;
        if (request.Effort is { } effort)
            body["output_config"] = new JsonObject { ["effort"] = EffortOf(effort) };
        if (request.StopSequences.Count > 0)
            body["stop_sequences"] = request.StopSequences.ToJsonArray(s => s);
        if (request.Tools.Count > 0)
        {
            body["tools"] = request.Tools.ToJsonArray(ToolOf);
            if (ToolChoiceOf(request.ToolChoice) is { } choice)
                body["tool_choice"] = choice;
        }
        return body.ToJsonString();
    }

    private static JsonObject MessageOf(LlmMessage message) => new()
    {
        ["role"] = message.Role == LlmRole.User ? "user" : "assistant",
        ["content"] = new JsonArray(message.Parts.Select(BlockOf).Where(b => b is not null).ToArray()),
    };

    private static JsonNode? BlockOf(LlmPart part) => part switch
    {
        LlmPart_Text text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        LlmPart_Image image => new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MediaType,
                ["data"] = image.Base64Data,
            },
        },
        LlmPart_ToolCall call => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = call.Id,
            ["name"] = call.Name,
            ["input"] = InputOf(call.ArgumentsJson),
        },
        LlmPart_ToolResult result => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = result.ToolCallId,
            ["content"] = result.Content,
            ["is_error"] = result.IsError,
        },
        // 서명이 있는 블록을 받은 그대로 돌려보내야 함, 다른 공급자의 추론은 보낼 수 없음
        LlmPart_Reasoning reasoning => reasoning.StateFor(StateId) is { } state ? JsonNode.Parse(state) : null,
        _ => null,
    };

    // eager_input_streaming이라 max_tokens에서 잘린 JSON이 남아 있을 수 있음
    private static JsonNode InputOf(string argumentsJson) => argumentsJson.TryParseJson() as JsonObject ?? new JsonObject();

    private static JsonObject ToolOf(LlmTool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["input_schema"] = tool.ParametersSchema.DeepClone(),
        ["eager_input_streaming"] = true,
    };

    private static string EffortOf(LlmEffort effort) => effort switch
    {
        LlmEffort.Minimal or LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        _ => "max",
    };

    private static JsonObject? ToolChoiceOf(LlmToolChoice choice) => choice.Mode switch
    {
        LlmToolChoiceMode.None => new JsonObject { ["type"] = "none" },
        LlmToolChoiceMode.Required => new JsonObject { ["type"] = "any" },
        LlmToolChoiceMode.Specific => new JsonObject { ["type"] = "tool", ["name"] = choice.ToolName },
        _ => null,
    };

    #endregion

    #region Response

    private static LlmError ErrorOf(HttpResult result)
    {
        var error = result.ToLlmError();
        return error.Message.Contains("prompt is too long") || error.Message.Contains("exceed context limit")
            ? error with { Kind = LlmErrorKind.ContextTooLong }
            : error;
    }

    private static LlmErrorKind KindOf(string? errorType) => errorType switch
    {
        "authentication_error" or "permission_error" => LlmErrorKind.Authentication,
        "rate_limit_error" => LlmErrorKind.RateLimited,
        "overloaded_error" => LlmErrorKind.Overloaded,
        "api_error" => LlmErrorKind.Server,
        "invalid_request_error" or "request_too_large" => LlmErrorKind.InvalidRequest,
        _ => LlmErrorKind.Unknown,
    };

    private static LlmStopReason StopReasonOf(string? stopReason) => stopReason switch
    {
        "end_turn" => LlmStopReason.EndTurn,
        "tool_use" => LlmStopReason.ToolUse,
        "max_tokens" or "model_context_window_exceeded" => LlmStopReason.MaxTokens,
        "stop_sequence" => LlmStopReason.StopSequence,
        "refusal" => LlmStopReason.ContentFiltered,
        _ => LlmStopReason.Unknown,
    };

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress)
    {
        private readonly List<Block> _blocks = [];
        private readonly Dictionary<int, Block> _byIndex = [];
        private readonly JsonObject _usage = new();
        private string? _model;
        private string? _stopReason;
        private bool _stopped;

        public LlmError? Error { get; private set; }

        public void OnEvent(SseItem<string> item)
        {
            var data = JsonNode.Parse(item.Data)!;
            switch ((string?)data["type"])
            {
                case "message_start":
                    _model = (string?)data["message"]?["model"];
                    MergeUsage(data["message"]?["usage"]);
                    break;
                case "content_block_start":
                    Start((int)data["index"]!, data["content_block"]!.AsObject());
                    break;
                case "content_block_delta":
                    if (_byIndex.TryGetValue((int)data["index"]!, out var block))
                        Delta(block, data["delta"]!);
                    break;
                case "message_delta":
                    _stopReason = (string?)data["delta"]?["stop_reason"] ?? _stopReason;
                    MergeUsage(data["usage"]);
                    break;
                case "message_stop":
                    _stopped = true;
                    break;
                case "error":
                    Error = new LlmError(KindOf((string?)data["error"]?["type"]),
                        (string?)data["error"]?["message"] ?? "Stream error");
                    break;
            }

            return;

            void Start(int index, JsonObject raw)
            {
                var type = (string?)raw["type"];
                if (type is not ("text" or "thinking" or "redacted_thinking" or "tool_use"))
                    return;

                var block = new Block(_blocks.Count, type, (JsonObject)raw.DeepClone());
                _blocks.Add(block);
                _byIndex[index] = block;
                if (type == "text")
                    block.Text.Append((string?)raw["text"]);
                else if (type == "thinking")
                    block.Text.Append((string?)raw["thinking"]);
                else if (type == "tool_use")
                    progress?.Report(new LlmStreamDelta_ToolCall(block.PartIndex, (string)raw["name"]!, ""));
            }

            void Delta(Block block, JsonNode delta)
            {
                switch ((string?)delta["type"])
                {
                    case "text_delta":
                        var text = (string)delta["text"]!;
                        block.Text.Append(text);
                        progress?.Report(new LlmStreamDelta_Text(block.PartIndex, text));
                        break;
                    case "thinking_delta":
                        var thinking = (string)delta["thinking"]!;
                        block.Text.Append(thinking);
                        progress?.Report(new LlmStreamDelta_Reasoning(block.PartIndex, thinking));
                        break;
                    case "signature_delta":
                        block.Signature.Append((string)delta["signature"]!);
                        break;
                    case "input_json_delta":
                        var json = (string)delta["partial_json"]!;
                        block.Text.Append(json);
                        progress?.Report(new LlmStreamDelta_ToolCall(block.PartIndex, (string)block.Raw["name"]!, json));
                        break;
                }
            }
        }

        public LlmResponse ToResponse()
        {
            if (!_stopped)
                return LlmResponse.Fail(new LlmError(LlmErrorKind.Network, "Stream ended before message_stop"));

            return new LlmResponse
            {
                Message = new LlmMessage(LlmRole.Assistant, _blocks.Select<Block, LlmPart>(block =>
                {
                    switch (block.Type)
                    {
                        case "text":
                            return new LlmPart_Text(block.Text.ToString());
                        case "tool_use":
                            return new LlmPart_ToolCall((string)block.Raw["id"]!, (string)block.Raw["name"]!,
                                block.Text.Length > 0 ? block.Text.ToString() : "{}");
                        case "thinking":
                            block.Raw["thinking"] = block.Text.ToString();
                            block.Raw["signature"] = block.Signature.ToString();
                            return new LlmPart_Reasoning(block.Text.Length > 0 ? block.Text.ToString() : null)
                            { ProviderState = new LlmProviderState(StateId, block.Raw.ToJsonString()) };
                        default:
                            return new LlmPart_Reasoning(null)
                            { ProviderState = new LlmProviderState(StateId, block.Raw.ToJsonString()) };
                    }
                }).ToList()),
                StopReason = StopReasonOf(_stopReason),
                TokenUsage = new LlmTokenUsage(Usage("input_tokens"), Usage("output_tokens"),
                    Usage("cache_read_input_tokens"), Usage("cache_creation_input_tokens")),
                ModelId = _model,
            };

            int Usage(string key) => (int?)_usage[key] ?? 0;
        }

        private void MergeUsage(JsonNode? usage)
        {
            if (usage is not JsonObject fields)
                return;
            foreach (var (key, value) in fields)
                if (value is JsonValue)
                    _usage[key] = value.DeepClone();
        }

        private sealed class Block(int partIndex, string type, JsonObject raw)
        {
            public int PartIndex { get; } = partIndex;
            public string Type { get; } = type;
            public JsonObject Raw { get; } = raw;
            public StringBuilder Text { get; } = new();
            public StringBuilder Signature { get; } = new();
        }
    }

    #endregion
}