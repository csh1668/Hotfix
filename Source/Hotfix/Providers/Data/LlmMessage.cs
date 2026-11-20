using System.Collections.Generic;
using System.Linq;

namespace Hotfix.Data;

public sealed record LlmMessage(LlmRole Role, IReadOnlyList<LlmPart> Parts)
{
    public static LlmMessage User(string text) => new(LlmRole.User, [new LlmPart_Text(text)]);

    public IEnumerable<LlmPart_ToolCall> ToolCalls() => Parts.OfType<LlmPart_ToolCall>();
    public string Text() => string.Concat(Parts.OfType<LlmPart_Text>().Select(t => t.Text));
}