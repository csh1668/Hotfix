using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Glob() : Tool_Base("Glob",
    """
    Finds files by name pattern. path is the absolute path of the folder to search, and pattern is matched against paths relative to it.
    Patterns: * matches within one path segment, ** matches across folders, ? matches one character, {a,b} matches either.
    Examples: "**/*.xml" for every XML file below the folder, "About/About.xml", "**/Patches/*.xml".
    Returns absolute paths, most recently modified first, at most 100.
    Assets.zip asset packs are searched like folders, and path may point into any zip file.
    """,
    """
    {
      "type": "object",
      "properties": {
        "pattern": { "type": "string", "description": "Glob pattern, relative to path" },
        "path": { "type": "string", "description": "Absolute path of the folder to search" },
        "timeout": { "type": "integer", "description": "Seconds before the search stops and returns what it has found, 30 by default, 600 at most" }
      },
      "required": ["pattern", "path"]
    }
    """)
{
    private const int MaxResults = 100;

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var pattern = Required(arguments, "pattern");
        var directory = context.Readable(Required(arguments, "path"));
        var timeout = ToolFiles.Timeout(arguments);
        return Task.Run(() =>
        {
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timer.CancelAfter(timeout);
            var found = new List<FoundFile>();
            var stopped = false;
            try
            {
                foreach (var file in ToolFiles.Find(context, directory, pattern, false, timer.Token))
                    found.Add(file);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                stopped = true;
            }

            var files = found.OrderByDescending(file => file.Time).Select(file => file.Path).ToList();
            var shown = string.Join("\n", files.Take(MaxResults));
            var text = files.Count == 0 ? "No files found."
                : files.Count > MaxResults
                    ? shown + $"\n\n[{files.Count} files matched, showing the {MaxResults} most recent. Narrow the pattern or the path.]"
                : shown;
            return stopped ? text + ToolFiles.Stopped(timeout) : text;
        }, ct);
    }
}
