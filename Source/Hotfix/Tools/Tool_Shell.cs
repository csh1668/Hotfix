using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Shell() : Tool_Base("Shell",
    $"""
    Runs a command line on the user's computer in {(OperatingSystem.IsWindows() ? "PowerShell" : "bash")}. The command starts in the chat's scratch folder. Returns its standard output and standard error.
    The command is stopped after timeout_seconds, 60 by default and 600 at most. The game keeps running while it runs.
    """,
    """
    {
      "type": "object",
      "properties": {
        "command": { "type": "string", "description": "The command line to run" },
        "description": { "type": "string", "description": "A few words on what the command does, shown to the user" },
        "timeout_seconds": { "type": "integer", "description": "Time limit in seconds, 60 by default" }
      },
      "required": ["command"]
    }
    """)
{
    public override bool NeedsApproval(JsonNode arguments, ToolContext context) => true;

    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var command = Required(arguments, "command");
        var seconds = Math.Clamp(arguments.GetInt("timeout_seconds", 60), 1, 600);
        Directory.CreateDirectory(context.Scratch);
        var start = new ProcessStartInfo
        {
            WorkingDirectory = context.Scratch,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = "powershell.exe";
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + command })
                start.ArgumentList.Add(argument);
        }
        else
        {
            start.FileName = "bash";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The shell could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            ct.ThrowIfCancellationRequested();
            timedOut = true;
        }

        var text = new StringBuilder(await output);
        if (await error is { Length: > 0 } failures)
            text.Append(text.Length > 0 ? "\n" : "").Append("[stderr]\n").Append(failures);
        if (timedOut)
            text.Append($"\n[The command was stopped after {seconds} seconds.]");
        else if (process.ExitCode != 0)
            text.Append($"\n[Exit code {process.ExitCode}]");
        return text.Length > 0 ? text.ToString().TrimEnd() : "(no output)";
    }
}
