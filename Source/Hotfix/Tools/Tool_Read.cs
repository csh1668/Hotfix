using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Read() : Tool_Base("Read",
    """
    Reads a file. file_path must be an absolute path inside one of the folders listed in the system prompt. It may continue into a zip file as if the zip were a folder, as the paths in Glob results do.
    What comes back depends on the file:
    - Text: up to 2000 lines or about 40,000 characters in `cat -n` format: the line number, a tab, then the line. Line numbers start at 1. If the file goes on, a note at the end says which lines were shown; read on with offset and limit. Lines longer than 2000 characters are cut.
    - Image (png, jpg, psd, dds, and textures inside Assets.zip packs): a picture.
    - Any other binary file: an error.
    To look for files or for text inside files, use Glob and Grep first instead of reading files one by one.
    """,
    """
    {
      "type": "object",
      "properties": {
        "file_path": { "type": "string", "description": "Absolute path of the file" },
        "offset": { "type": "integer", "description": "Line number to start at, 1 by default" },
        "limit": { "type": "integer", "description": "Number of lines to read, 2000 by default" }
      },
      "required": ["file_path"]
    }
    """)
{
    private const int DefaultLimit = 2000;
    private const int MaxLineLength = 2000;
    private const int MaxOutput = 40_000;

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var path = context.Readable(Required(arguments, "file_path"));
        if (ToolFiles.TryGet(path, out var file) && ImageFile.IsImage(file.Path))
            return ReadImage(file.Path, context);
        var offset = Math.Max(1, arguments.GetInt("offset", 1));
        var limit = Math.Max(1, arguments.GetInt("limit", DefaultLimit));
        return Task.Run(() => Read(path, offset, limit, ct), ct);
    }

    private static async Task<string> ReadImage(string path, ToolContext context)
    {
        var picture = await ImageFile.Load(path, Path.Combine(context.Scratch, "images"));
        context.Attachments.Add(picture.Image);
        return $"Image, {picture.Width}x{picture.Height} pixels. The picture follows."
            + (picture.Transparent ? " Transparent areas are shown gray." : "");
    }

    private static string Read(string path, int offset, int limit, CancellationToken ct)
    {
        if (ToolFiles.IsFolder(path))
            throw new IOException($"{path} is a {(File.Exists(path) ? "zip file" : "directory")}. Use Glob to list its files.");
        if (!ToolFiles.TryGet(path, out var file))
            throw new FileNotFoundException($"No such file: {path}");
        if (ToolFiles.IsBinary(file.Path))
            throw new IOException($"{path} is a binary file ({file.Length:N0} bytes) and cannot be read as text.");

        var text = new StringBuilder();
        var number = 0;
        var last = 0;
        using var reader = new StreamReader(ToolFiles.Open(file.Path));
        while (reader.ReadLine() is { } line)
        {
            ct.ThrowIfCancellationRequested();
            number++;
            if (number < offset || number >= offset + limit || text.Length >= MaxOutput)
                continue;
            text.Append(number.ToString().PadLeft(6)).Append('\t');
            if (line.Length > MaxLineLength)
                text.Append(line, 0, MaxLineLength).Append($"...[{line.Length - MaxLineLength:N0} more characters]");
            else
                text.Append(line);
            text.Append('\n');
            last = number;
        }
        if (number == 0)
            return "(the file is empty)";
        if (offset > number)
            return $"(the file has {number} lines, offset {offset} is past the end)";
        if (last < number)
            text.Append($"\n[Lines {offset}-{last} of {number}. Read more with offset.]");
        return text.ToString();
    }
}
