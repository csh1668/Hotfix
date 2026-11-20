using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Edit() : Tool_Base("Edit",
    """
    Replaces text in a file. file_path must be an absolute path inside one of the read and write folders listed under Files in the system prompt.
    old_string must match the file exactly, including indentation, and must occur exactly once unless replace_all is true. Read the file first so that you copy the text exactly.
    """,
    """
    {
      "type": "object",
      "properties": {
        "file_path": { "type": "string", "description": "Absolute path of the file" },
        "old_string": { "type": "string", "description": "Text to replace" },
        "new_string": { "type": "string", "description": "Text to put in its place" },
        "replace_all": { "type": "boolean", "description": "Replace every occurrence, false by default" }
      },
      "required": ["file_path", "old_string", "new_string"]
    }
    """)
{
    public override bool NeedsApproval(JsonNode arguments, ToolContext context) =>
        !context.InScratch(context.Writable(Required(arguments, "file_path")));

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var path = context.Writable(Required(arguments, "file_path"));
        var old = Required(arguments, "old_string");
        var replacement = Required(arguments, "new_string");
        var all = arguments.GetBool("replace_all");
        return Task.Run(() =>
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"No such file: {path}");
            var text = File.ReadAllText(path);
            var count = old.Length == 0 ? 0 : (text.Length - text.Replace(old, "").Length) / old.Length;
            if (count == 0)
                throw new IOException("old_string was not found in the file. It must match exactly, including whitespace.");
            if (count > 1 && !all)
                throw new IOException($"old_string occurs {count} times. Add surrounding text to make it unique, or set replace_all.");
            File.WriteAllText(path, text.Replace(old, replacement));
            return $"Edited {path} ({count} replacement{(count == 1 ? "" : "s")}).";
        }, ct);
    }
}
