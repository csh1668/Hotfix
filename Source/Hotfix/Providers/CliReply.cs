using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hotfix.Data;

namespace Hotfix;

internal sealed partial class CliReply(IProgress<LlmStreamDelta>? progress, IReadOnlyList<LlmTool> tools)
{
    private const string InvokeOpen = "<invoke";
    private const string InvokeClose = "</invoke>";

    private const string ResultOpen = "<tool_result";
    private static readonly string[] Opens = [CliPrompt.CallOpen, InvokeOpen, ResultOpen];

    private readonly List<LlmPart> _parts = [];
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _open = new();
    private bool _reasoning;
    private string? _close;
    private bool _called;
    private bool _done;

    public void Text(string delta)
    {
        if (_done)
            return;
        _buffer.Append(delta);
        while (true)
        {
            var buffer = _buffer.ToString();
            if (_close is not null)
            {
                var end = buffer.IndexOf(_close, StringComparison.Ordinal);
                if (end < 0)
                    return;
                AddCall(buffer[..end]);
                _buffer.Remove(0, end + _close.Length);
                _close = null;
                continue;
            }

            var open = Opens.Select(tag => (Tag: tag, At: buffer.IndexOf(tag, StringComparison.Ordinal))).Where(found => found.At >= 0)
                .OrderBy(found => found.At).FirstOrDefault();
            var cut = open.Tag is null ? buffer.Length - Tail(buffer) : open.At;
            Plain(buffer[..cut]);
            if (open.Tag == ResultOpen)
                _done = true;
            if (_done)
            {
                _buffer.Clear();
                return;
            }
            if (open.Tag is null)
            {
                _buffer.Remove(0, cut);
                return;
            }
            _buffer.Remove(0, cut + open.Tag.Length);
            _close = open.Tag == InvokeOpen ? InvokeClose : CliPrompt.CallClose;
        }
    }

    public void Reasoning(string delta) => Write(true, delta);

    public List<LlmPart> Finish()
    {
        if (_close is not null)
            AddCall(_buffer.ToString());
        else if (!_done)
            Plain(_buffer.ToString());
        _buffer.Clear();
        Close();
        return _parts;
    }

    private void Plain(string text)
    {
        if (!_called)
            Write(false, text);
        else if (!string.IsNullOrWhiteSpace(text))
            _done = true;
    }

    private static int Tail(string buffer)
    {
        for (var keep = Math.Min(buffer.Length, Opens.Max(tag => tag.Length) - 1); keep > 0; keep--)
        {
            if (Opens.Any(tag => tag.StartsWith(buffer[^keep..], StringComparison.Ordinal)))
                return keep;
        }
        return 0;
    }

    private void Write(bool reasoning, string text)
    {
        if (reasoning != _reasoning)
            Close();
        _reasoning = reasoning;
        if (_open.Length == 0)
            text = text.TrimStart();
        if (text.Length == 0)
            return;
        _open.Append(text);
        progress?.Report(reasoning
            ? new LlmStreamDelta_Reasoning(_parts.Count, text)
            : new LlmStreamDelta_Text(_parts.Count, text));
    }

    private void Close()
    {
        if (_open.Length == 0)
            return;
        var text = _open.ToString().TrimEnd();
        _parts.Add(_reasoning ? new LlmPart_Reasoning(text) : new LlmPart_Text(text));
        _open.Clear();
    }

    public void Call(LlmPart_ToolCall call)
    {
        Close();
        progress?.Report(new LlmStreamDelta_ToolCall(_parts.Count, call.Name, call.ArgumentsJson));
        _parts.Add(call);
    }

    private void AddCall(string body)
    {
        _called = true;
        var id = "call_" + Guid.NewGuid().ToString("N")[..12];
        if (_close == InvokeClose)
        {
            Call(Invoke(id, body));
            return;
        }
        body = body.Trim();
        var call = body.TryParseJson() is JsonObject json && json["name"] is JsonValue value && value.TryGetValue<string>(out var name)
            ? new LlmPart_ToolCall(id, name, (json["arguments"] as JsonObject ?? new JsonObject()).ToJsonString(Extensions.RelaxedJson))
            : new LlmPart_ToolCall(id, Regex_NameJsonField().Match(body).Groups[1].Value, body);
        Call(call);
    }

    private LlmPart_ToolCall Invoke(string id, string body)
    {
        var name = Regex_NameField().Match(body).Groups[1].Value;
        var properties = tools.FirstOrDefault(tool => tool.Name == name)?.ParametersSchema["properties"];
        var arguments = new JsonObject();
        foreach (Match parameter in Regex_ParameterName().Matches(body))
        {
            var key = parameter.Groups[1].Value;
            var value = parameter.Groups[2].Value;
            arguments[key] = (string?)properties?[key]?["type"] == "string" ? value : value.TryParseJson() ?? value;
        }
        return new LlmPart_ToolCall(id, name, arguments.ToJsonString(Extensions.RelaxedJson));
    }

    [GeneratedRegex("\"name\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex Regex_NameJsonField();
    [GeneratedRegex("^\\s*name=\"([^\"]*)\"")]
    private static partial Regex Regex_NameField();
    [GeneratedRegex("<parameter name=\"([^\"]*)\">(.*?)</parameter>", RegexOptions.Singleline)]
    private static partial Regex Regex_ParameterName();
}
