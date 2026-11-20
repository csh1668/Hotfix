using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public class Provider_ClaudeCode : Provider_Cli
{
    public override string Id => "claude-code";
    public override string Label => "Claude Code (CLI)";
    public override string DefaultBaseUrl => "claude";

    // 내장 도구와 사용자 설정(CLAUDE.md, 훅, MCP)을 모두 끄고 대화도 남기지 않음
    private static readonly string[] BaseArguments =
    [
        "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
        "--tools", "", "--safe-mode", "--strict-mcp-config", "--no-session-persistence",
    ];

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var system = Path.Combine(Folder, $"system-{Guid.NewGuid():N}.md");
        File.WriteAllText(system, CliPrompt.System(request));
        try
        {
            List<string> arguments = [.. BaseArguments, "--include-partial-messages", "--system-prompt-file", system];
            if (request.Model is not ("" or "default"))
                arguments.AddRange(["--model", request.Model]);
            if (request.Effort is { } effort)
                arguments.AddRange(["--effort", EffortOf(effort)]);

            var input = new JsonObject
            {
                ["type"] = "user",
                ["message"] = new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = CliPrompt.Transcript(request.Messages).ToJsonArray(BlockOf),
                },
            };
            var stream = new StreamState(progress, request.Tools);
            var result = await RunAsync(request.Endpoint, arguments, input.ToJsonString() + "\n", stream.OnLine, ct)
                .ConfigureAwait(false);
            return stream.ToResponse() ?? LlmResponse.Fail(ErrorOf(result));
        }
        finally
        {
            File.Delete(system);
        }
    }

    // 모델 목록은 명령이 따로 없고 SDK용 제어 요청의 응답에 들어 있다
    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var models = new List<string>();
        var result = await RunAsync(endpoint, BaseArguments,
            """{"type":"control_request","request_id":"models","request":{"subtype":"initialize"}}""" + "\n", line =>
            {
                if (line.TryParseJson() is JsonObject data && (string?)data["type"] == "control_response"
                    && data["response"]?["response"]?["models"] is JsonArray list)
                    models.AddRange(list.Select(model => (string)model!["value"]!));
            }, ct).ConfigureAwait(false);
        return models.Count > 0 ? (models, null) : ([], ErrorOf(result));
    }

    private static JsonNode BlockOf(LlmPart part) => part is LlmPart_Image image
        ? new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MediaType,
                ["data"] = image.Base64Data,
            },
        }
        : new JsonObject { ["type"] = "text", ["text"] = ((LlmPart_Text)part).Text };

    private static string EffortOf(LlmEffort effort) => effort switch
    {
        LlmEffort.Minimal or LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        _ => "max",
    };

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress, IReadOnlyList<LlmTool> tools)
    {
        private readonly CliReply _reply = new(progress, tools);
        private JsonObject? _result;
        private string? _model;

        public void OnLine(string line)
        {
            if (line.TryParseJson() is not JsonObject data)
                return;
            switch ((string?)data["type"])
            {
                // Messages API의 스트림 이벤트가 그대로 들어 있다
                case "stream_event":
                    var delta = data["event"]?["delta"];
                    switch ((string?)delta?["type"])
                    {
                        case "text_delta":
                            _reply.Text((string)delta!["text"]!);
                            break;
                        case "thinking_delta":
                            _reply.Reasoning((string)delta!["thinking"]!);
                            break;
                    }
                    _model ??= (string?)data["event"]?["message"]?["model"];
                    break;
                case "result":
                    _result = data;
                    break;
            }
        }

        // 결과 줄을 받지 못했으면 null
        public LlmResponse? ToResponse()
        {
            if (_result is null)
                return null;
            if ((bool?)_result["is_error"] == true)
                return LlmResponse.Fail(ErrorOf((string?)_result["result"] ?? "Claude Code failed", (int?)_result["api_error_status"]));

            var usage = _result["usage"];
            return ResponseOf(_reply, (string?)_result["stop_reason"] switch
            {
                "max_tokens" or "model_context_window_exceeded" => LlmStopReason.MaxTokens,
                "refusal" => LlmStopReason.ContentFiltered,
                _ => LlmStopReason.EndTurn,
            },
                new LlmTokenUsage(Usage("input_tokens"), Usage("output_tokens"), Usage("cache_read_input_tokens"),
                    Usage("cache_creation_input_tokens")), _model);

            int Usage(string key) => (int?)usage?[key] ?? 0;
        }
    }
}
