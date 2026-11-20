using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Waylith.Assets;
using Waylith.Graphics.Textures;

namespace Hotfix;

internal readonly record struct ZipItem(string Name, string Entry, long Length)
{
    public bool IsTexture => Name != Entry;
}

internal static class ZipFiles
{
    public const string PackName = "Assets.zip";

    private static readonly string[] TextureSuffixes = [".dds.split", ".dds", "-premul.dds.split", "-premul.dds"];
    private static readonly string[] TextExtensions = [".xml", ".txt", ".json", ".md", ".cs", ".csv", ".yml", ".yaml", ".ini", ".cfg"];

    public static bool TrySplit(string path, out string zip, out string inside)
    {
        zip = inside = "";
        for (var at = path.IndexOf(".zip", StringComparison.OrdinalIgnoreCase); at >= 0;
             at = path.IndexOf(".zip", at + 4, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + 4;
            if (end < path.Length && path[end] is not ('\\' or '/'))
                continue;
            if (!File.Exists(path[..end]))
                continue;
            zip = path[..end];
            inside = path[end..].Replace('\\', '/').Trim('/');
            return true;
        }
        return false;
    }

    public static string PathOf(string zip, string name) => Path.Combine(zip, name.Replace('/', Path.DirectorySeparatorChar));

    public static bool IsText(string name) => TextExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    // zip의 항목들, 이름순
    public static List<ZipItem> Entries(string zip)
    {
        using var archive = new ZipArchive(ToolFiles.OpenFile(zip), ZipArchiveMode.Read);
        var items = new Dictionary<string, (ZipItem Item, int Rank)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            // 폴더 항목
            if (entry.FullName.EndsWith('/'))
                continue;
            var (name, rank) = Shown(entry.FullName);
            if (!items.TryGetValue(name, out var known) || rank < known.Rank)
                items[name] = (new ZipItem(name, entry.FullName, entry.Length), rank);
        }
        return items.Values.Select(value => value.Item).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ZipItem? Find(string zip, string inside)
    {
        foreach (var item in Entries(zip))
        {
            if (item.Name.Equals(inside, StringComparison.OrdinalIgnoreCase) || item.Entry.Equals(inside, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    public static bool HasFolder(string zip, string inside) =>
        inside.Length == 0 || Entries(zip).Any(item => item.Name.StartsWith(inside + "/", StringComparison.OrdinalIgnoreCase));

    public static byte[] Read(string zip, ZipItem item)
    {
        using (var archive = new ZipArchive(ToolFiles.OpenFile(zip), ZipArchiveMode.Read))
        {
            var entry = archive.GetEntry(item.Entry) ?? throw new FileNotFoundException($"No such entry: {item.Entry}");
            try
            {
                using var stream = entry.Open();
                var data = new byte[entry.Length];
                stream.ReadExactly(data);
                return data;
            }
            catch (InvalidDataException)
            {
            }
        }
        using var pack = AssetPack.TryOpen(zip);
        return pack is not null && pack.TryRead(item.Entry, out var bytes)
            ? bytes
            : throw new InvalidDataException($"The entry {item.Entry} could not be decompressed.");
    }

    public static byte[] ReadTexture(string zip, ZipItem item)
    {
        var data = Read(zip, item);
        if (!item.Entry.EndsWith(ContentPack.SplitSuffix, StringComparison.OrdinalIgnoreCase))
            return data;
        if (!DdsFile.TryJoin(data, out var dds, out var length))
            throw new InvalidDataException($"The packed texture {item.Entry} could not be restored.");
        try
        {
            return dds.AsSpan(0, length).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(dds);
        }
    }

    private static (string Name, int Rank) Shown(string entry)
    {
        for (var i = 0; i < TextureSuffixes.Length; i++)
        {
            if (!entry.EndsWith(TextureSuffixes[i], StringComparison.OrdinalIgnoreCase))
                continue;
            var source = entry[..^TextureSuffixes[i].Length];
            if (ImageFile.IsImage(source))
                return (source, i + 1);
        }
        return (entry, 0);
    }
}
