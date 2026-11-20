using System.Text.Json.Nodes;

namespace Hotfix.Data;

public sealed record LlmTool(string Name, string Description, JsonNode ParametersSchema);