using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Hotfix;

// Packed: Zip 안에 있는 파일인지 (Assets.zip)
internal readonly record struct FoundFile(string Path, long Length, DateTime Time, bool Packed);

internal static class ToolFiles
{
    public static TimeSpan Timeout(JsonNode arguments) => TimeSpan.FromSeconds(Math.Clamp(arguments.GetInt("timeout", 30), 1, 600));

    public static string Stopped(TimeSpan timeout) =>
        $"\n\n[Stopped after {timeout.TotalSeconds:0} seconds, so the results are incomplete. Narrow the search.]";

    private static readonly RegexOptions PathOptions =
        RegexOptions.CultureInvariant | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);

    public static FileStream OpenFile(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    // 디스크의 파일이든 zip 안의 항목이든 연다
    public static Stream Open(string path) =>
        ZipFiles.TrySplit(path, out var zip, out var inside) && ZipFiles.Find(zip, inside) is { } item
            ? new MemoryStream(ZipFiles.Read(zip, item))
            : OpenFile(path);

    public static bool TryGet(string path, out FoundFile file)
    {
        if (ZipFiles.TrySplit(path, out var zip, out var inside) && inside.Length > 0)
        {
            var item = ZipFiles.Find(zip, inside);
            file = item is { } found ? new FoundFile(ZipFiles.PathOf(zip, found.Name), found.Length, File.GetLastWriteTimeUtc(zip), true) : default;
            return item is not null;
        }
        var info = new FileInfo(path);
        file = info.Exists ? new FoundFile(info.FullName, info.Length, info.LastWriteTimeUtc, false) : default;
        return info.Exists;
    }

    // 폴더이거나, zip 파일이거나, zip 안의 폴더
    public static bool IsFolder(string path) =>
        Directory.Exists(path) || (ZipFiles.TrySplit(path, out var zip, out var inside) && ZipFiles.HasFolder(zip, inside));

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static string[] ReadAllLines(string path)
    {
        using var reader = new StreamReader(Open(path));
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }

    public static IEnumerable<FoundFile> Find(ToolContext context, string directory, string? pattern, bool anyDepth, CancellationToken ct)
    {
        var matcher = pattern is null ? null : Glob(pattern, anyDepth);
        if (ZipFiles.TrySplit(directory, out var zip, out var inside))
        {
            if (!ZipFiles.HasFolder(zip, inside))
                throw new DirectoryNotFoundException($"No such directory: {directory}");
            foreach (var file in Packed(zip, inside.Length == 0 ? "" : inside + "/", "", matcher, ct))
                yield return file;
            yield break;
        }

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"No such directory: {directory}");
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (!context.CanRead(file.FullName))
                continue;
            var relative = Path.GetRelativePath(directory, file.FullName).Replace('\\', '/');
            if (matcher is null || matcher.IsMatch(relative))
                yield return new FoundFile(file.FullName, file.Length, file.LastWriteTimeUtc, false);
            if (!file.Name.Equals(ZipFiles.PackName, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var packed in Packed(file.FullName, "", relative + "/", matcher, ct))
                yield return packed;
        }
    }

    private static IEnumerable<FoundFile> Packed(string zip, string prefix, string shown, Regex? matcher, CancellationToken ct)
    {
        List<ZipItem> items;
        try
        {
            items = ZipFiles.Entries(zip);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            yield break;
        }
        var time = File.GetLastWriteTimeUtc(zip);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (matcher is null || matcher.IsMatch(shown + item.Name[prefix.Length..]))
                yield return new FoundFile(ZipFiles.PathOf(zip, item.Name), item.Length, time, true);
        }
    }

    public static Regex Glob(string pattern, bool anyDepth)
    {
        pattern = pattern.Replace('\\', '/');
        if (anyDepth && !pattern.Contains('/'))
            pattern = "**/" + pattern;
        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                // "**/"는 폴더 0개 이상
                var slash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                regex.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (c == '*')
            {
                regex.Append("[^/]*");
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else if (c == '{' && pattern.IndexOf('}', i) is var close and > 0)
            {
                regex.Append("(?:").Append(string.Join("|", pattern[(i + 1)..close].Split(',').Select(Regex.Escape))).Append(')');
                i = close;
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }
        return new Regex(regex.Append('$').ToString(), PathOptions);
    }

    public static bool IsBinary(string path)
    {
        using var stream = Open(path);
        Span<byte> head = stackalloc byte[1024];
        head = head[..stream.Read(head)];
        // UTF-16은 글자마다 NUL이 있어도 글이다
        if (head.StartsWith<byte>([0xFF, 0xFE]) || head.StartsWith<byte>([0xFE, 0xFF]))
            return false;
        return head.Contains((byte)0);
    }
}
