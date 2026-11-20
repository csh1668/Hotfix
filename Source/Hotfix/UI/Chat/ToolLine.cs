using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotfix.Data;
using Rimact;
using UnityEngine;
using Verse;
using static Rimact.Elements;

namespace Hotfix;

// 도구 호출 하나를 제목 한 줄, 결과 요약, 펼친 내용으로 보여 주는 규칙
internal static class ToolLine
{
    // 펼친 내용에서 따로 그리는 인자
    private static readonly string[] Bulk = ["code", "content", "command", "old_string", "new_string", "description", "questions"];

    // 출력 한도에서 잘린 호출은 JSON이 깨져 있을 수 있다
    public static JsonObject? Arguments(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Title(LlmPart_ToolCall call, JsonObject? a)
    {
        var detail = call.Name switch
        {
            "Read" => FileName(a) + Range(a),
            "Edit" or "Write" => FileName(a),
            "Grep" => $"\"{a.GetString("pattern", "")}\" in {Path.GetFileName(a.GetString("path", "").TrimEnd('/', '\\'))}",
            "Glob" => a.GetString("pattern", ""),
            "Shell" => a.GetString("description", "") is { Length: > 0 } description ? description : a.GetString("command", "").Split('\n')[0],
            "SubMod" => $"{a.GetString("action", "")} {a.GetString("id", "")}",
            "Build" => Path.GetFileName(a.GetString("output", "")),
            "Memory" => a.GetString("action", "") == "save" ? $"{a.GetString("name", "")}: {a.GetString("description", "")}" : $"{a.GetString("action", "")} {a.GetString("name", "")}",
            "Ask" => a.GetArray("questions") is { Count: > 0 } questions
                ? questions[0].GetString("question", "") + (questions.Count > 1 ? $" (+{questions.Count - 1})" : "")
                : "",
            _ => a.GetString("description", ""),
        };
        return detail.Length > 0 ? $"<b>{call.Name}</b>  {Markdown.Literal(detail)}" : $"<b>{call.Name}</b>";
    }

    // 접힌 줄의 오른쪽에 적는 결과 크기. 실패하면 경고 아이콘이 대신한다
    public static string? Summary(LlmPart_ToolCall call, JsonObject? a, LlmPart_ToolResult? result)
    {
        if (result is null || result.IsError)
            return null;
        switch (call.Name)
        {
            case "Edit":
                var (removed, added) = Changed(a.GetString("old_string", ""), a.GetString("new_string", ""));
                return $"+{added.Count} -{removed.Count}";
            case "Write":
                return "Hotfix_ChatLines".Translate(Lines(a.GetString("content", "")).ToString("N0"));
            case "Read" or "Grep" or "Glob" or "Shell" or "CSharp" or "Snapshot":
                return "Hotfix_ChatLines".Translate(Lines(result.Content).ToString("N0"));
            case "Ask":
                return result.Content == "The user closed the questions without answering." ? "Hotfix_AskSkipped".Translate() : null;
            default:
                return null;
        }
    }

    // 질문에 사용자가 한 답. 답하지 않았으면 null. 질문이 여럿이면 답한 것만 번호를 붙여 적는다
    public static string? Reply(LlmPart_ToolCall call, LlmPart_ToolResult? result)
    {
        if (call.Name != "Ask" || result is null || result.IsError || result.Content == "The user closed the questions without answering.")
            return null;
        var answers = Tool_Ask.Answers(result.Content);
        return answers.Count == 1
            ? answers[0]
            : string.Join("\n", answers.Select((answer, i) => (answer, i)).Where(pair => pair.answer != "(no answer)")
                .Select(pair => $"{pair.i + 1}. {pair.answer}"));
    }

    // full이 아니면 긴 인자는 잘라서 보여 준다
    public static Node Body(Theme t, LlmPart_ToolCall call, JsonObject? a, bool full = false)
    {
        if (a is null)
            return Transcript.Detail(t, call.ArgumentsJson, full: full);
        var rows = new List<Node?>();
        var fields = a.Where(pair => !Bulk.Contains(pair.Key) && pair.Value is not null)
            .Select(pair => $"{pair.Key}: {(pair.Value is JsonValue value && value.TryGetValue<string>(out var text) ? text : pair.Value!.ToJsonString())}")
            .ToList();
        if (fields.Count > 0)
            rows.Add(Transcript.Detail(t, string.Join("\n", fields), full: full));

        if (call.Name == "Edit")
            rows.Add(Diff(t, a.GetString("old_string", ""), a.GetString("new_string", ""), full));
        else if (a.GetString("code") is { } code)
            rows.Add(new CodeBlock("csharp", code) { Clip = !full });
        else if (a.GetString("command") is { } command)
            rows.Add(new CodeBlock("shell", command) { Clip = !full });
        else if (a.GetString("content") is { } content)
            rows.Add(new CodeBlock(Path.GetExtension(a.GetString("file_path", "")).TrimStart('.'), content) { Clip = !full });
        else if (a.GetArray("questions") is { Count: > 0 } questions)
            rows.Add(Transcript.Detail(t, string.Join("\n\n", questions.Select(question => question.GetString("question", "")
                + string.Concat(question.GetArray("options").Select(option => $"\n - {option.GetString("label", "")}")))), full: full));
        return Column(rows).Gap(t.Space(1));
    }

    private static Node Diff(Theme t, string before, string after, bool full)
    {
        var (removed, added) = Changed(before, after);
        var red = "#" + ColorUtility.ToHtmlStringRGB(t.Destructive);
        var green = "#" + ColorUtility.ToHtmlStringRGB(t.Success);
        var text = string.Join("\n", removed.Select(line => "- " + line).Concat(added.Select(line => "+ " + line)));
        return full ? Render(text) : new Clipped(text, Render);

        // 줄마다 첫 글자로 색을 정한다
        Node Render(string shown) => Column(
            Text(string.Join("\n", shown.Split('\n')
                    .Select(line => $"<color={(line.StartsWith("-") ? red : green)}>{Markdown.Literal(line)}</color>")).Replace("\t", "    "))
                .TextSize(t.TextCaption + 1f).Wrap().Anchor(TextAlign.UpperLeft)
        ).Padding(t.Space(2)).Bg(t.Sunken).Border(t.Border);
    }

    // 앞뒤의 같은 줄을 뺀 나머지
    private static (List<string> Removed, List<string> Added) Changed(string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n');
        var b = after.Replace("\r\n", "\n").Split('\n');
        var head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head])
            head++;
        var tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)])
            tail++;
        return (a[head..^tail].ToList(), b[head..^tail].ToList());
    }

    private static string FileName(JsonObject? a) => Path.GetFileName(a.GetString("file_path", ""));

    private static string Range(JsonObject? a)
    {
        var offset = a.GetInt("offset");
        var limit = a.GetInt("limit");
        if (offset is null && limit is null)
            return "";
        var first = offset ?? 1;
        return limit is null ? $":{first}-" : $":{first}-{first + limit - 1}";
    }

    private static int Lines(string text) => text.Length == 0 ? 0 : text.TrimEnd('\n').Count(c => c == '\n') + 1;
}
