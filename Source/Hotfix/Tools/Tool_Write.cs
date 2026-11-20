using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Write() : Tool_Base("Write",
    """
    Writes a text file, replacing it if it exists. file_path must be an absolute path inside one of the read and write folders listed under Files in the system prompt. Missing folders are created.
    To change part of an existing file, use Edit instead.
    """,
    """
    {
      "type": "object",
      "properties": {
        "file_path": { "type": "string", "description": "Absolute path of the file" },
        "content": { "type": "string", "description": "The whole content of the file" }
      },
      "required": ["file_path", "content"]
    }
    """)
{
    // 임시 폴더 안의 쓰기는 묻지 않음
    public override bool NeedsApproval(JsonNode arguments, ToolContext context) =>
        !context.InScratch(context.Writable(Required(arguments, "file_path")));

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var path = context.Writable(Required(arguments, "file_path"));
        var content = Required(arguments, "content");
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return $"Wrote {path} ({content.Length:N0} characters).";
        }, ct);
    }
}
