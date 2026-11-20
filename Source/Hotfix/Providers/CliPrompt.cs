using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Hotfix.Data;

namespace Hotfix;

internal static class CliPrompt
{
    public const string CallOpen = "<tool_call>";
    public const string CallClose = "</tool_call>";

    private const string Conversation =
        """
        # Conversation format
        This session keeps no chat history of its own. Each request gives you the whole conversation so far as a transcript in which every turn is wrapped in <user> or <assistant> tags. Write the next assistant turn and nothing else. Do not write <user> or <assistant> tags, and never write a turn for the user.
        """;

    private const string ToolCalls =
        """
        # Tool calls
        The tools listed below are the only tools you have. Do not use any other tool this session appears to offer. To call a tool, end your turn with one or more blocks of this form:

        <tool_call>
        {"name": "ToolName", "arguments": {"parameter": "value"}}
        </tool_call>

         - Each block holds one JSON object. Write strings as valid JSON: escape quotes, backslashes and line breaks.
         - Write any text for the user before the blocks and stop right after the last block.
         - The results come back in the next user turn inside <tool_result> blocks, in the order of your calls. Never write a <tool_result> block yourself.
        """;

    private const string NativeCalls =
        """
        # Tool calls
        Call tools through function calling. The transcript shows your earlier calls as <tool_call> blocks and their results as <tool_result> blocks; that is only how history is written down, so never write such a block yourself. Do not use exec, apply_patch or any other built-in tool of this session. The only tools you may use are these functions:
        """;

    public static string System(LlmRequest request, bool native = false)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            text.Append(request.SystemPrompt).Append("\n\n");
        text.Append(Conversation);
        if (request.Tools.Count == 0)
            return text.ToString();
        if (native)
            return text.Append("\n\n").Append(NativeCalls).Append(string.Join(", ", request.Tools.Select(tool => tool.Name))).ToString();

        text.Append("\n\n").Append(ToolCalls);
        foreach (var tool in request.Tools)
        {
            text.Append($"\n\n## {tool.Name}\n{tool.Description}\nParameters (JSON Schema): ")
                .Append(tool.ParametersSchema.ToJsonString(Extensions.RelaxedJson));
        }
        return text.ToString();
    }

    public static List<LlmPart> Transcript(IReadOnlyList<LlmMessage> messages)
    {
        var blocks = new List<LlmPart>();
        foreach (var message in messages)
        {
            var tag = message.Role == LlmRole.User ? "user" : "assistant";
            var text = new StringBuilder($"<{tag}>\n");
            foreach (var part in message.Parts)
            {
                switch (part)
                {
                    case LlmPart_Text plain:
                        text.Append(plain.Text).Append('\n');
                        break;
                    case LlmPart_ToolCall call:
                        text.Append($"{CallOpen}\n{CallOf(call)}\n{CallClose}\n");
                        break;
                    case LlmPart_ToolResult result:
                        text.Append($"<tool_result name=\"{result.ToolName}\"{(result.IsError ? " status=\"error\"" : "")}>\n")
                            .Append(result.Content).Append("\n</tool_result>\n");
                        break;
                    case LlmPart_Image image:
                        blocks.Add(new LlmPart_Text(text.ToString()));
                        blocks.Add(image);
                        text.Clear();
                        break;
                }
            }
            blocks.Add(new LlmPart_Text(text.Append($"</{tag}>").ToString()));
        }
        return blocks;
    }

    private static string CallOf(LlmPart_ToolCall call) => call.ArgumentsJson.TryParseJson() is null
        ? call.ArgumentsJson
        : $$"""{"name": {{JsonSerializer.Serialize(call.Name, Extensions.RelaxedJson)}}, "arguments": {{call.ArgumentsJson}}}""";
}
