using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Grep() : Tool_Base("Grep",
    """
    Searches file contents with a regular expression (.NET syntax). path is the absolute path of a file or a folder; a folder is searched recursively.
    glob filters the files of a folder search, for example "*.xml" or "Defs/**/*.xml".
    output_mode: "files_with_matches" (default) lists the files that match, "content" shows the matching lines as path:line:text, "count" shows the number of matching lines per file.
    Assets.zip asset packs are searched like folders, and path may point into any zip file.
    Binary files and files larger than 10 MB are skipped. The game's code is compiled, so searching the game folder finds XML and text only.
    """,
    """
    {
      "type": "object",
      "properties": {
        "pattern": { "type": "string", "description": "Regular expression to search for" },
        "path": { "type": "string", "description": "Absolute path of the file or folder to search" },
        "glob": { "type": "string", "description": "Only search files matching this glob" },
        "output_mode": { "type": "string", "enum": ["files_with_matches", "content", "count"] },
        "-i": { "type": "boolean", "description": "Ignore case" },
        "-C": { "type": "integer", "description": "Lines of context around each match, for output_mode content" },
        "head_limit": { "type": "integer", "description": "Maximum number of output lines, 250 by default" },
        "timeout": { "type": "integer", "description": "Seconds before the search stops and returns what it has found, 30 by default, 600 at most" }
      },
      "required": ["pattern", "path"]
    }
    """)
{
    private const int DefaultHeadLimit = 250;
    private const int MaxLineLength = 500;
    private const long MaxFileSize = 10 * 1024 * 1024;

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var options = RegexOptions.CultureInvariant | (arguments.GetBool("-i") ? RegexOptions.IgnoreCase : RegexOptions.None);
        // 줄 하나에 오래 걸리는 패턴이 검색 전체를 붙잡지 않게 한다
        var regex = new Regex(Required(arguments, "pattern"), options, TimeSpan.FromSeconds(1));
        var path = context.Readable(Required(arguments, "path"));
        var glob = arguments.GetString("glob");
        var mode = arguments.GetString("output_mode", "files_with_matches");
        var around = Math.Max(0, arguments.GetInt("-C", 0));
        var limit = Math.Max(1, arguments.GetInt("head_limit", DefaultHeadLimit));
        var timeout = ToolFiles.Timeout(arguments);
        return Task.Run(() => Search(context, regex, path, glob, mode, around, limit, timeout, ct), ct);
    }

    private static string Search(ToolContext context, Regex regex, string path, string? glob, string mode, int around, int limit,
        TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        FoundFile single = default;
        var one = !(ZipFiles.TrySplit(path, out _, out var inside) && inside.Length == 0) && ToolFiles.TryGet(path, out single);
        var files = one ? [single] : ToolFiles.Find(context, path, glob, true, timer.Token);
        var output = new List<string>();
        var stopped = false;
        try
        {
            foreach (var file in files)
            {
                if (output.Count > limit)
                    break;
                if (file.Length > MaxFileSize || (file.Packed && !one && !ZipFiles.IsText(file.Path)))
                    continue;
                string[] lines;
                try
                {
                    if (ToolFiles.IsBinary(file.Path))
                        continue;
                    lines = ToolFiles.ReadAllLines(file.Path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    continue;
                }

                var matches = new List<int>();
                try
                {
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                            matches.Add(i);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    output.Add($"{file.Path}: skipped, the pattern took too long on this file");
                    continue;
                }
                if (matches.Count == 0)
                    continue;

                if (mode == "count")
                    output.Add($"{file.Path}:{matches.Count}");
                else if (mode != "content")
                    output.Add(file.Path);
                else
                    AddContent(output, file.Path, lines, matches, around);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopped = true;
        }

        var text = output.Count == 0 ? "No matches found."
            : output.Count > limit ? string.Join("\n", output.Take(limit)) + $"\n\n[Output cut at {limit} lines. Narrow the search or raise head_limit.]"
            : string.Join("\n", output);
        return stopped ? text + ToolFiles.Stopped(timeout) : text;
    }

    private static void AddContent(List<string> output, string path, string[] lines, List<int> matches, int around)
    {
        var last = -1;
        foreach (var match in matches)
        {
            var from = Math.Max(Math.Max(0, match - around), last + 1);
            var to = Math.Min(lines.Length - 1, match + around);
            // 떨어진 묶음 사이
            if (around > 0 && last >= 0 && from > last + 1)
                output.Add("--");
            for (var i = from; i <= to; i++)
            {
                var text = lines[i].Length > MaxLineLength ? lines[i][..MaxLineLength] + "..." : lines[i];
                output.Add($"{path}{(matches.Contains(i) ? ':' : '-')}{i + 1}{(matches.Contains(i) ? ':' : '-')}{text}");
            }
            last = to;
        }
    }
}
