using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

internal sealed partial class Tool_Memory(MemoryStore store) : Tool_Base("Memory",
    """
    Saves or deletes a memory: a fact that later chats should still know. The Memory section of the system prompt says what to save and lists the memories that exist. To read one, use Read on its file.
    """,
    """
    {
      "type": "object",
      "properties": {
        "action": { "type": "string", "enum": ["save", "delete"] },
        "name": { "type": "string", "description": "Short kebab-case name such as prefers-short-answers. Saving under a name that exists replaces that memory" },
        "description": { "type": "string", "description": "For save: one line that says what the memory is about. Later chats see only this line until they read the file" },
        "type": { "type": "string", "enum": ["user", "feedback", "project", "reference"], "description": "For save" },
        "content": { "type": "string", "description": "For save: the fact itself" }
      },
      "required": ["action", "name"]
    }
    """)
{
    public override bool Enabled => HotfixMod.Settings.MemoryEnabled;

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var name = Required(arguments, "name").Trim().ToLowerInvariant();
        if (!Regex_FileName().IsMatch(name) || name.Length > 64)
            throw new ArgumentException("name must be lowercase letters, digits and hyphens, at most 64 characters");

        switch (Required(arguments, "action"))
        {
            case "save":
                var type = Required(arguments, "type");
                if (!MemoryStore.Types.Contains(type))
                    throw new ArgumentException($"type must be one of: {string.Join(", ", MemoryStore.Types)}");
                store.Save(new MemoryEntry(name, Required(arguments, "description"), type, Required(arguments, "content")));
                return Task.FromResult($"Saved the memory {name} at {store.PathOf(name)}.");
            case "delete":
                if (!store.Delete(name))
                    throw new FileNotFoundException($"There is no memory named {name}.");
                return Task.FromResult($"Deleted the memory {name}.");
            default:
                throw new ArgumentException("action must be save or delete");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Regex_FileName();
}
