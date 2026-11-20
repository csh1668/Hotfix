using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hotfix;

internal sealed record MemoryEntry(string Name, string Description, string Type, string Content);

internal sealed class MemoryStore(string folder)
{
    public static readonly string[] Types = ["user", "feedback", "project", "reference"];

    public int Version { get; private set; }

    public List<MemoryEntry> List() => Directory.Exists(folder)
        ? new DirectoryInfo(folder).GetFiles("*.md").OrderBy(file => file.Name, StringComparer.Ordinal)
            .Select(file => Read(file.FullName)).OfType<MemoryEntry>().ToList()
        : [];

    public string PathOf(string name) => Path.Combine(folder, name + ".md");

    public void Save(MemoryEntry memory)
    {
        Directory.CreateDirectory(folder);
        var description = memory.Description.ReplaceLineEndings(" ").Trim();
        File.WriteAllText(PathOf(memory.Name),
            $"---\nname: {memory.Name}\ndescription: {description}\ntype: {memory.Type}\n---\n\n{memory.Content.ReplaceLineEndings("\n").Trim()}\n");
        Version++;
    }

    public bool Delete(string name)
    {
        if (!File.Exists(PathOf(name)))
            return false;
        File.Delete(PathOf(name));
        Version++;
        return true;
    }

    private static MemoryEntry? Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path).ReplaceLineEndings("\n");
        }
        catch (IOException e)
        {
            Logger.Warning($"Failed to read the memory {Path.GetFileName(path)}: {e.Message}");
            return null;
        }

        var description = "";
        var type = "";
        if (text.StartsWith("---\n") && text.IndexOf("\n---", 3, StringComparison.Ordinal) is var end and > 0)
        {
            foreach (var line in text[4..end].Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon < 0)
                    continue;
                var value = line[(colon + 1)..].Trim();
                if (line[..colon] == "description")
                    description = value;
                else if (line[..colon] == "type")
                    type = value;
            }
            text = text[(end + 4)..];
        }
        return new MemoryEntry(Path.GetFileNameWithoutExtension(path), description, type, text.Trim());
    }
}
