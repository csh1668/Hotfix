using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

public abstract class Tool_Base(string name, string description, string parametersSchema)
{
    public LlmTool Definition { get; } = new(name, description, JsonNode.Parse(parametersSchema)!);

    public virtual bool Enabled => true;

    public virtual bool NeedsApproval(JsonNode arguments, ToolContext context) => false;

    public abstract Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct);

    protected static string Required(JsonNode arguments, string name) =>
        arguments.GetString(name) ?? throw new System.ArgumentException($"{name} is required");
}
