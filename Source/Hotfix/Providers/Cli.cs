using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Hotfix;

internal sealed record CliResult(int ExitCode, string Error, Exception? Exception = null)
{
    public bool IsSuccess => Exception is null && ExitCode == 0;
}

internal sealed class CliInput
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    public ChannelReader<string> Reader => _queue.Reader;

    public void Write(string text) => _queue.Writer.TryWrite(text);

    public void Close() => _queue.Writer.TryComplete();
}

internal static class Cli
{
    public static async Task<CliResult> RunAsync(string command, IReadOnlyList<string> arguments, CliInput input,
        string workingDirectory, Action<string> onLine, TimeSpan idleTimeout, CancellationToken ct)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Resolve(command),
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} could not be started.");
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(idleTimeout);
            try
            {
                var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
                var write = WriteAsync(process.StandardInput, input, idle.Token);
                while (await process.StandardOutput.ReadLineAsync(idle.Token).ConfigureAwait(false) is { } line)
                {
                    idle.CancelAfter(idleTimeout);
                    onLine(line);
                }
                input.Close();
                await write.ConfigureAwait(false);
                await process.WaitForExitAsync(idle.Token).ConfigureAwait(false);
                return new CliResult(process.ExitCode, await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TaskCanceledException("CLI idle timeout", new TimeoutException());
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
        }
        catch (Exception e)
        {
            return new CliResult(-1, "", e);
        }
    }

    private static async Task WriteAsync(StreamWriter stdin, CliInput input, CancellationToken ct)
    {
        try
        {
            await foreach (var text in input.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await stdin.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
            stdin.Close();
        }
        catch (IOException)
        {
            // 입력을 다 읽기 전에 끝난 프로세스.
        }
    }

    private static string Resolve(string command)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(command))
            return command;
        var folders = command.Contains('\\') || command.Contains('/')
            ? [""]
            : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return folders.SelectMany(folder => extensions.Select(extension => Path.Combine(folder, command + extension)))
            .FirstOrDefault(File.Exists) ?? command;
    }
}
