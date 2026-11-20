using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public class Provider_Codex : Provider_Cli
{
    private const int ThreadStart = 2;

    public override string Id => "codex";
    public override string Label => "Codex (CLI)";
    public override string DefaultBaseUrl => "codex";

    private static readonly string[] Arguments =
    [
        "app-server", "--config", "check_for_update_on_startup=false", "--config", "web_search=disabled",
        .. new[]
        {
            "shell_tool", "unified_exec", "apps", "browser_use", "browser_use_external", "computer_use", "image_generation",
            "multi_agent", "plugins", "goals", "hooks", "view_image", "skill_search", "tool_suggest", "sleep_tool",
        }.SelectMany(feature => new[] { "--disable", feature }),
    ];

    public override async Task<LlmResponse> CallAsync(LlmRequest request, IProgress<LlmStreamDelta>? progress = null,
        CancellationToken ct = default)
    {
        var thread = new JsonObject
        {
            ["cwd"] = Folder,
            ["approvalPolicy"] = "never",
            ["sandbox"] = "read-only",
            ["ephemeral"] = true,
            ["baseInstructions"] = CliPrompt.System(request, true),
        };
        if (request.Model.Length > 0)
            thread["model"] = request.Model;
        if (request.Tools.Count > 0)
        {
            thread["dynamicTools"] = request.Tools.ToJsonArray(tool => new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.ParametersSchema.DeepClone(),
            });
        }

        var turn = new JsonObject
        {
            ["summary"] = "auto",
            ["input"] = CliPrompt.Transcript(request.Messages).ToJsonArray(block => block is LlmPart_Image image
                ? new JsonObject { ["type"] = "image", ["url"] = $"data:{image.MediaType};base64,{image.Base64Data}" }
                : new JsonObject { ["type"] = "text", ["text"] = ((LlmPart_Text)block).Text }),
        };
        if (request.Effort is { } effort)
            turn["effort"] = EffortOf(effort);

        var input = new CliInput();
        input.Write(Message(1, "initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject { ["name"] = "hotfix", ["version"] = "1" },
            ["capabilities"] = new JsonObject { ["experimentalApi"] = true },
        }));
        input.Write(Message(null, "initialized", null));
        input.Write(Message(ThreadStart, "thread/start", thread));
        var stream = new StreamState(progress, request.Tools, input, turn);
        var result = await RunAsync(request.Endpoint, Arguments, input, stream.OnLine, ct).ConfigureAwait(false);
        return stream.ToResponse() ?? LlmResponse.Fail(ErrorOf(result));
    }

    public override async Task<(IReadOnlyList<string> Models, LlmError? Error)> GetModelsAsync(LlmEndpoint endpoint,
        CancellationToken ct = default)
    {
        var output = new StringBuilder();
        var result = await RunAsync(endpoint, ["debug", "models"], "", line => output.Append(line).Append('\n'), ct)
            .ConfigureAwait(false);
        if (!result.IsSuccess || (output.ToString().TryParseJson() as JsonObject)?["models"] is not JsonArray models)
            return ([], ErrorOf(result));
        return (models.Where(model => (string?)model!["visibility"] == "list").Select(model => (string)model!["slug"]!).ToList(), null);
    }

    private static string Message(int? id, string method, JsonObject? parameters)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null)
            message["id"] = id;
        if (parameters is not null)
            message["params"] = parameters;
        return message.ToJsonString() + "\n";
    }

    private static string EffortOf(LlmEffort effort) => effort switch
    {
        LlmEffort.Minimal or LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        _ => "max",
    };

    private static LlmError FailureOf(JsonNode? error)
    {
        var message = (string?)error?["message"] ?? "Codex failed";
        if (message.TryParseJson() is JsonObject body && body["error"] is JsonObject inner)
            return ErrorOf((string?)inner["message"] ?? message, (int?)body["status"]);
        var info = error?["codexErrorInfo"];
        return (info is JsonObject { Count: 1 } detail ? detail.First().Key : info?.ToString()) switch
        {
            "unauthorized" => new LlmError(LlmErrorKind.Authentication, message),
            "contextWindowExceeded" => new LlmError(LlmErrorKind.ContextTooLong, message),
            _ => ErrorOf(message),
        };
    }

    private sealed class StreamState(IProgress<LlmStreamDelta>? progress, IReadOnlyList<LlmTool> tools, CliInput input, JsonObject turn)
    {
        private readonly CliReply _reply = new(progress, tools);
        private readonly HashSet<string> _streamed = [];
        private readonly int[] _usage = new int[4];
        private string? _text;
        private string? _reasoning;
        private string? _model;
        private LlmError? _error;
        private bool _done;
        private bool _called;

        public void OnLine(string line)
        {
            if (line.TryParseJson() is not JsonObject data)
                return;
            var method = (string?)data["method"];
            var parameters = data["params"];
            if (method is null)
            {
                if (data["error"] is { } failure)
                    Finish(FailureOf(failure));
                else if (data["id"]?.ToString() == ThreadStart.ToString())
                {
                    _model = (string?)data["result"]?["model"];
                    turn["threadId"] = (string?)data["result"]?["thread"]?["id"];
                    input.Write(Message(3, "turn/start", turn));
                }
                return;
            }
            if (method == "item/tool/call")
            {
                _reply.Call(new LlmPart_ToolCall((string)parameters!["callId"]!, (string)parameters["tool"]!,
                    parameters["arguments"]?.ToJsonString(Extensions.RelaxedJson) ?? "{}"));
                if (!_called)
                {
                    input.Write(Message(4, "turn/interrupt", new JsonObject
                    {
                        ["threadId"] = (string?)parameters["threadId"],
                        ["turnId"] = (string?)parameters["turnId"],
                    }));
                }
                _called = true;
                return;
            }
            if (data["id"] is { } id)
            {
                input.Write(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "not supported" },
                }.ToJsonString() + "\n");
                return;
            }

            switch (method)
            {
                case "item/agentMessage/delta":
                    Text((string?)parameters?["itemId"], (string?)parameters?["delta"] ?? "");
                    break;
                case "item/reasoning/summaryTextDelta":
                    var part = $"{parameters?["itemId"]}:{parameters?["summaryIndex"]}";
                    if (_reasoning is not null && _reasoning != part)
                        _reply.Reasoning("\n\n");
                    _reasoning = part;
                    _reply.Reasoning((string?)parameters?["delta"] ?? "");
                    break;
                case "item/completed":
                    var item = parameters?["item"];
                    if ((string?)item?["type"] == "agentMessage" && !_streamed.Contains((string?)item["id"] ?? ""))
                        Text((string?)item["id"], (string?)item["text"] ?? "");
                    break;
                case "thread/tokenUsage/updated":
                    var last = parameters?["tokenUsage"]?["last"];
                    string[] keys = ["inputTokens", "outputTokens", "cachedInputTokens", "cacheWriteInputTokens"];
                    for (var i = 0; i < keys.Length; i++)
                        _usage[i] += (int?)last?[keys[i]] ?? 0;
                    break;
                case "error":
                    if ((bool?)parameters?["willRetry"] != true)
                        Finish(FailureOf(parameters?["error"]));
                    break;
                case "turn/completed":
                    _done = true;
                    Finish(_called || (string?)parameters?["turn"]?["status"] == "completed" ? null : FailureOf(parameters?["turn"]?["error"]));
                    break;
            }
        }

        public LlmResponse? ToResponse()
        {
            if (_error is not null)
                return LlmResponse.Fail(_error);
            return _done
                ? ResponseOf(_reply, LlmStopReason.EndTurn,
                    new LlmTokenUsage(Math.Max(0, _usage[0] - _usage[2] - _usage[3]), _usage[1], _usage[2], _usage[3]), _model)
                : null;
        }

        private void Text(string? itemId, string text)
        {
            if (_text is not null && _text != itemId)
                _reply.Text("\n\n");
            _text = itemId;
            _streamed.Add(itemId ?? "");
            _reply.Text(text);
        }

        private void Finish(LlmError? error)
        {
            _error ??= error;
            input.Close();
        }
    }
}
