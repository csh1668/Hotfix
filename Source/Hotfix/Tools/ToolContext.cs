using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hotfix;

public sealed class ToolContext(ToolPaths paths, string scratch, AskHandler ask)
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly Dictionary<Type, object> _state = [];

    public ToolPaths Paths => paths;
    public string Scratch { get; } = Path.GetFullPath(scratch);

    public string Readable(string path)
    {
        var full = FullPath(path);
        if (!CanRead(full))
            throw new InvalidOperationException($"Access denied: {full} is not inside a folder this chat can read.");
        return full;
    }

    public string Writable(string path)
    {
        var full = FullPath(path);
        if (!IsUnder(full, paths.ModRoot) && !IsUnder(full, paths.SubModRoot) && !IsUnder(full, Scratch))
            throw new InvalidOperationException($"Access denied: {full} is not inside a folder this chat can write to.");
        return full;
    }

    public bool CanRead(string full) => paths.ReadRoots.Any(root => IsUnder(full, root)) || IsUnder(full, paths.ModRoot) || IsUnder(full, Scratch);

    public List<ChatImage> Attachments { get; } = [];

    public AskHandler Ask => ask;

    public bool InScratch(string full) => IsUnder(full, Scratch);

    // 대화 동안 유지되는 도구의 상태
    public T State<T>(Func<T> create) where T : class =>
        (T)(_state.TryGetValue(typeof(T), out var value) ? value : _state[typeof(T)] = create());

    public void Drop<T>() => _state.Remove(typeof(T));

    public static string FullPath(string path) => Path.IsPathFullyQualified(path)
        ? Path.GetFullPath(path)
        : throw new InvalidOperationException($"Use an absolute path. \"{path}\" is relative.");

    private static bool IsUnder(string full, string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.Equals(root, PathComparison) || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }
}
