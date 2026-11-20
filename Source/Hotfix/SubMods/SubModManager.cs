using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using RimWorld;
using Verse;

namespace Hotfix;

internal static class SubModManager
{
    private static readonly string[] ContentFolders = ["Defs", "Patches", "Textures", "Sounds", "Languages"];

    public static readonly string Root = Path.Combine(GenFilePaths.SaveDataFolderPath, "Hotfix", "SubMods");

    public static int Version { get; private set; }

    public static string Folder(string id) => Path.Combine(Root, id);

    public static string VersionFolder(string id) => Path.Combine(Root, id, VersionControl.CurrentVersionStringWithoutBuild);

    public static string Dll(string id) => Path.Combine(VersionFolder(id), "Assemblies", id + ".dll");

    public static IEnumerable<string> LoadFolders(string id) =>
        new[] { VersionFolder(id), Path.Combine(Root, id, "Common") }.Where(Directory.Exists);

    public static bool Supported(SubMod sub) => sub.SupportedVersions.Contains(VersionControl.CurrentVersionStringWithoutBuild) && Directory.Exists(VersionFolder(sub.Id));

    public static string[] Structure(string id) =>
    [
        .. new[] { "Source", "Assemblies", Path.Combine("Defs", id), Path.Combine("Patches", id), "Textures", "Sounds", "Languages" }
            .Select(part => Path.Combine(VersionControl.CurrentVersionStringWithoutBuild, part)),
        "Common",
    ];

    public static List<SubMod> List() => Directory.Exists(Root)
        ? Directory.GetDirectories(Root).Select(folder => Find(Path.GetFileName(folder))).OfType<SubMod>()
            .OrderBy(sub => sub.Created).ThenBy(sub => sub.Id, StringComparer.Ordinal).ToList()
        : [];

    public static SubMod? Find(string id)
    {
        var file = Path.Combine(Folder(id), "SubMod.xml");
        if (!File.Exists(file))
            return null;
        try
        {
            var sub = XmlHelper.Load<SubMod>(file)!;
            sub.Id = id;
            return sub;
        }
        catch (Exception e)
        {
            Logger.Warning($"Failed to read the sub-mod {id}: {e.Message}");
            return null;
        }
    }

    public static void Save(SubMod sub)
    {
        XmlHelper.Save(Path.Combine(Folder(sub.Id), "SubMod.xml"), sub);
        Version++;
    }

    public static SubMod Create(string id, string name, string description, IEnumerable<string> requires, string? chat)
    {
        var folder = Folder(id);
        if (Directory.Exists(folder))
            throw new InvalidOperationException($"There already is a sub-mod named {id}.");
        foreach (var part in Structure(id))
            Directory.CreateDirectory(Path.Combine(folder, part));
        var sub = new SubMod
        {
            Id = id,
            Name = name,
            Description = description,
            Created = DateTime.Now,
            Chat = chat,
            SupportedVersions = [VersionControl.CurrentVersionStringWithoutBuild],
            Requires = requires.ToList(),
        };
        Save(sub);
        return sub;
    }

    private static bool ContentLoaded(string id) => SubModStartup.Content.GetValueOrDefault(id, "").Length > 0;

    public static bool CanApply(string id) => !ContentLoaded(id) && !ContentFiles(id).Any();

    public static bool CanRemove(string id) => !ContentLoaded(id);

    public static void Delete(SubMod sub)
    {
        if (CanRemove(sub.Id))
            SubModCode.Remove(sub.Id);
        Directory.Delete(Folder(sub.Id), true);
        Version++;
    }

    public static List<string> MissingRequires(SubMod sub) =>
        sub.Requires.Where(id => ModLister.GetActiveModWithIdentifier(id, true) is null).ToList();

    public static string Stamp(string id)
    {
        var files = ContentFiles(id).ToList();
        return files.Count == 0 ? "" : $"{files.Count}:{files.Max(File.GetLastWriteTimeUtc).Ticks}";
    }

    public static IEnumerable<string> ContentFiles(string id) => LoadFolders(id)
        .SelectMany(root => ContentFolders.Select(name => Path.Combine(root, name))).Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));

    public static List<string> Misplaced(string id) => LoadFolders(id)
        .SelectMany(root => new[] { "Defs", "Patches" }.Select(name => Path.Combine(root, name))).Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(Path.Combine(folder, id) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    public static SubModStatus Status(SubMod sub)
    {
        var loaded = SubModStartup.Content.GetValueOrDefault(sub.Id, "");
        if (!sub.Enabled)
            return sub.DisabledReason is not null ? SubModStatus.Failed : loaded.Length > 0 ? SubModStatus.Restart : SubModStatus.Off;
        if (MissingRequires(sub).Count > 0)
            return SubModStatus.Missing;
        if (!Supported(sub))
            return SubModStatus.Incompatible;
        return Stamp(sub.Id) != loaded || !SubModCode.Matches(sub.Id, Dll(sub.Id)) ? SubModStatus.Restart : SubModStatus.On;
    }

    public static void SetEnabled(SubMod sub, bool enabled)
    {
        sub.Enabled = enabled;
        sub.DisabledReason = null;
        Save(sub);
        if (!enabled)
        {
            if (CanRemove(sub.Id))
                SubModCode.Remove(sub.Id);
        }
        else if (CanApply(sub.Id) && MissingRequires(sub).Count == 0 && Supported(sub) && File.Exists(Dll(sub.Id)))
            SubModCode.Apply(sub.Id, File.ReadAllBytes(Dll(sub.Id)));
    }
    
    public static byte[]? Compile(SubMod sub)
    {
        var source = Path.Combine(VersionFolder(sub.Id), "Source");
        SubModCode.LoadLibraries(sub.Id);
        var files = Directory.Exists(source)
            ? Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal)
                .ToDictionary(file => Path.GetRelativePath(source, file), File.ReadAllText)
            : [];
        return files.Count == 0 ? null : ScriptHost.Engine.Value.Compile("HotfixSub_" + sub.Id, files);
    }

    public static IReadOnlyList<string>? Install(SubMod sub, byte[]? image)
    {
        IReadOnlyList<string>? patched = null;
        if (image is null)
        {
            if (CanRemove(sub.Id))
                SubModCode.Remove(sub.Id);
        }
        else if (CanApply(sub.Id) && sub.Enabled && MissingRequires(sub).Count == 0 && Supported(sub))
            patched = SubModCode.Apply(sub.Id, image);
        WriteDll(sub, image);
        sub.Revision++;
        Save(sub);
        return patched;
    }

    public static void WriteDll(SubMod sub, byte[]? image)
    {
        var dll = Dll(sub.Id);
        if (image is null)
        {
            if (File.Exists(dll))
                File.Delete(dll);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllBytes(dll, image);
        using var pe = new PEReader(new MemoryStream(image));
        var metadata = pe.GetMetadataReader();
        var names = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToHashSet();
        var own = SubModCode.HotfixModContentPack;
        foreach (var mod in LoadedModManager.RunningMods)
        {
            if (mod != own && !sub.Requires.Contains(mod.PackageIdPlayerFacing, StringComparer.OrdinalIgnoreCase)
                && mod.assemblies.loadedAssemblies.Any(assembly => assembly.GetName().Name is { } name and not "0Harmony" && names.Contains(name)))
                sub.Requires.Add(mod.PackageIdPlayerFacing);
        }
    }
}
