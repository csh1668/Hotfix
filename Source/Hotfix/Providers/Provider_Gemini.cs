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

public class Provider_Gemini : Provider_Base
{
    private const string StateId = "gemini";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    public override string Id => StateId;
    public override string Label => "Gemini";
    public override string DefaultBaseUrl => "https://generativelanguage.googleapis.com";

    private static Dictionary<string, string> HeadersOf(LlmEndpoint endpoint) => new() { ["x-goog-api-key"] = endpoint.ApiKey };

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var stream = new StreamState(progress);
        var url = BaseUrlOf(request.Endpoint) / $"v1beta/models/{request.Model}:streamGenerateContent?alt=sse";
        var result = await Http.PostSseAsync(url, HeadersOf(request.Endpoint), BuildBody(request), stream.OnEvent,
            IdleTimeout, ct).ConfigureAwait(false);
        if (stream.Error is { } error)
            return LlmResponse.Fail(error);
        return result.IsSuccess ? stream.ToResponse() : LlmResponse.Fail(ErrorOf(result));
    }

    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var models = new List<string>();
        var headers = HeadersOf(endpoint);
        string? pageToken = null;
        do
        {
            var query = pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken);
            var result = await Http.GetAsync(BaseUrlOf(endpoint) / ("v1beta/models?pageSize=1000" + query), headers, ct)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
                return ([], ErrorOf(result));

            var page = JsonNode.Parse(result.Body)!;
            models.AddRange((page["models"] as JsonArray ?? [])
                .Where(model => model!["supportedGenerationMethods"] is JsonArray methods
                                && methods.Any(method => (string?)method == "generateContent"))
                .Select(model => ((string)model!["name"]!).Replace("models/", "")));
            pageToken = (string?)page["nextPageToken"];
        } while (pageToken is not null);
        return (models, null);
    }

    #region Request

    private static string BuildBody(LlmRequest request)
    {
        var thinking = new JsonObject { ["includeThoughts"] = true };
        if (request.Effort is { } effort)
            thinking["thinkingLevel"] = EffortOf(effort);
        var generation = new JsonObject { ["maxOutputTokens"] = request.MaxOutputTokens, ["thinkingConfig"] = thinking };
        if (request.StopSequences.Count > 0)
            generation["stopSequences"] = request.StopSequences.ToJsonArray(s => s);

        var body = new JsonObject
        {
            ["contents"] = request.Messages.ToJsonArray(MessageOf),
            ["generationConfig"] = generation,
            // 유료 프로젝트는 요청을 기본으로 저장한다
            ["store"] = false,
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) };
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = request.Tools.ToJsonArray(ToolOf) });
            if (ToolChoiceOf(request.ToolChoice) is { } choice)
                body["toolConfig"] = new JsonObject { ["functionCallingConfig"] = choice };
        }
        return body.ToJsonString();
    }

    private static JsonObject MessageOf(LlmMessage message) => new()
    {
        ["role"] = message.Role == LlmRole.User ? "user" : "model",
        ["parts"] = new JsonArray(message.Parts.Select(PartOf).Where(p => p is not null).ToArray()),
    };

    private static JsonNode? PartOf(LlmPart part)
    {
        var signature = part.StateFor(StateId);
        JsonObject? json = part switch
        {
            LlmPart_Text text => new JsonObject { ["text"] = text.Text },
            LlmPart_Image image => new JsonObject
            {
                ["inlineData"] = new JsonObject { ["mimeType"] = image.MediaType, ["data"] = image.Base64Data },
            },
            LlmPart_ToolCall call => new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["args"] = call.ArgumentsJson.TryParseJson() as JsonObject ?? new JsonObject(),
                    ["id"] = call.Id,
                },
            },
            LlmPart_ToolResult result => new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = result.ToolName,
                    ["id"] = result.ToolCallId,
                    ["response"] = new JsonObject { [result.IsError ? "error" : "output"] = result.Content },
                },
            },
            // 추론 요약은 돌려보내지 않고 서명만 받은 자리에 돌려보낸다
            LlmPart_Reasoning when signature is not null => new JsonObject { ["text"] = "" },
            _ => null,
        };
        if (json is not null && signature is not null)
            json["thoughtSignature"] = signature;
        return json;
    }

    private static JsonObject ToolOf(LlmTool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["parametersJsonSchema"] = tool.ParametersSchema.DeepClone(),
    };

    private static string EffortOf(LlmEffort effort) => effort switch
    {
        LlmEffort.Minimal => "minimal",
        LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        _ => "xhigh",
    };

    private static JsonObject? ToolChoiceOf(LlmToolChoice choice) => choice.Mode switch
    {
        LlmToolChoiceMode.None => new JsonObject { ["mode"] = "NONE" },
        LlmToolChoiceMode.Required => new JsonObject { ["mode"] = "ANY" },
        LlmToolChoiceMode.Specific => new JsonObject
        {
            ["mode"] = "ANY",
            ["allowedFunctionNames"] = new JsonArray(choice.ToolName),
        },
        _ => null,
    };

    #endregion

    #region Response

    private static LlmError ErrorOf(HttpResult result)
    {
        var error = result.ToLlmError();
        return error.Message.Contains("context is too long")
               || (error.Message.Contains("token count") && error.Message.Contains("exceeds"))
            ? error with { Kind = LlmErrorKind.ContextTooLong }
            : error;
    }

    private static LlmStopReason StopReasonOf(string finishReason) => finishReason switch
    {
        "STOP" => LlmStopReason.EndTurn,
        // CONTINUATION은 이어 받지 않고 잘린 것으로 본다
        "MAX_TOKENS" or "CONTINUATION" => LlmStopReason.MaxTokens,
        "SAFETY" or "RECITATION" or "LANGUAGE" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" => LlmStopReason.ContentFiltered,
        _ => LlmStopReason.Unknown,
    };

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress)
    {
        private readonly List<LlmPart> _parts = [];
        // 청크마다 조각으로 오는 글을 모으는 중인 파트
        private readonly StringBuilder _open = new();
        private bool _reasoning;
        private JsonNode? _usage;
        private string? _model;
        private string? _finish;
        private string? _finishMessage;
        private bool _blocked;

        public LlmError? Error { get; private set; }

        public void OnEvent(SseItem<string> item)
        {
            var data = JsonNode.Parse(item.Data)!;
            if (data["error"] is JsonObject error)
            {
                Error = ErrorOf(new HttpResult((int?)error["code"] ?? 0, data.ToJsonString()));
                return;
            }

            _usage = data["usageMetadata"] ?? _usage;
            _model = (string?)data["modelVersion"] ?? _model;
            _blocked |= data["promptFeedback"]?["blockReason"] is not null;
            if (data["candidates"] is not JsonArray { Count: > 0 } candidates)
                return;

            var candidate = candidates[0]!;
            foreach (var part in candidate["content"]?["parts"] as JsonArray ?? [])
            {
                var signature = (string?)part!["thoughtSignature"];
                if (part["functionCall"] is JsonObject call)
                {
                    // 인자는 조각이 아니라 완성된 객체로 한 번에 온다
                    Close();
                    var name = (string)call["name"]!;
                    var arguments = (call["args"] as JsonObject)?.ToJsonString(Extensions.RelaxedJson) ?? "{}";
                    progress?.Report(new LlmStreamDelta_ToolCall(_parts.Count, name, arguments));
                    _parts.Add(new LlmPart_ToolCall((string?)call["id"] ?? "call_" + Guid.NewGuid().ToString("N")[..12], name, arguments)
                    { ProviderState = StateOf(signature) });
                }
                else if ((string?)part["text"] is { } text)
                {
                    Write((bool?)part["thought"] == true, text);
                    // 서명이 붙은 파트는 뒤에 오는 글과 합치지 않는다
                    if (signature is not null)
                        Close(signature);
                }
            }
            _finish = (string?)candidate["finishReason"] ?? _finish;
            _finishMessage = (string?)candidate["finishMessage"] ?? _finishMessage;
        }

        public LlmResponse ToResponse()
        {
            Close();
            if (_finish is null && !_blocked)
                return LlmResponse.Fail(new LlmError(LlmErrorKind.Network, "Stream ended before finishReason"));
            var stopReason = _finish is null ? LlmStopReason.ContentFiltered : StopReasonOf(_finish);
            if (stopReason == LlmStopReason.Unknown)
                return LlmResponse.Fail(new LlmError(LlmErrorKind.Unknown, $"{_finish}: {_finishMessage}"));

            var cached = Usage("cachedContentTokenCount");
            return new LlmResponse
            {
                Message = new LlmMessage(LlmRole.Assistant, _parts),
                StopReason = _parts.OfType<LlmPart_ToolCall>().Any() ? LlmStopReason.ToolUse : stopReason,
                // promptTokenCount는 캐시를 포함하고 candidatesTokenCount는 추론을 포함하지 않는다
                TokenUsage = new LlmTokenUsage(Usage("promptTokenCount") - cached,
                    Usage("candidatesTokenCount") + Usage("thoughtsTokenCount"), cached),
                ModelId = _model,
            };

            int Usage(string key) => (int?)_usage?[key] ?? 0;
        }

        private void Write(bool reasoning, string text)
        {
            if (text.Length == 0)
                return;
            if (reasoning != _reasoning)
                Close();
            _reasoning = reasoning;
            _open.Append(text);
            progress?.Report(reasoning
                ? new LlmStreamDelta_Reasoning(_parts.Count, text)
                : new LlmStreamDelta_Text(_parts.Count, text));
        }

        // 글 없이 서명만 온 경우에는 서명만 든 추론 파트로 남긴다
        private void Close(string? signature = null)
        {
            if (_open.Length == 0 && signature is null)
                return;
            LlmPart part = _open.Length == 0 || _reasoning ? new LlmPart_Reasoning(_open.Length == 0 ? null : _open.ToString())
                : new LlmPart_Text(_open.ToString());
            _parts.Add(part with { ProviderState = StateOf(signature) });
            _open.Clear();
        }

        private static LlmProviderState? StateOf(string? signature) =>
            signature is null ? null : new LlmProviderState(StateId, signature);
    }

    #endregion
}
