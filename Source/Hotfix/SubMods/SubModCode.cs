using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Hotfix;

internal static class SubModCode
{
    private static readonly Dictionary<string, (Harmony Harmony, Assembly Assembly, byte[] Image)> Applied = [];

    public static ModContentPack HotfixModContentPack =>
        LoadedModManager.RunningModsListForReading.First(mod => mod.assemblies.loadedAssemblies.Contains(typeof(HotfixMod).Assembly));

    public static bool IsApplied(string id) => Applied.ContainsKey(id);

    public static bool Matches(string id, string dll) => Applied.TryGetValue(id, out var applied)
        ? File.Exists(dll) && File.ReadAllBytes(dll).AsSpan().SequenceEqual(applied.Image)
        : !File.Exists(dll);

    public static Assembly? AssemblyOf(string id) => Applied.TryGetValue(id, out var applied) ? applied.Assembly : null;

    public static IReadOnlyList<string> Patched(string id) => Applied.TryGetValue(id, out var applied) ? Methods(applied.Harmony) : [];

    public static void LoadLibraries(string id)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToHashSet();
        var files = SubModManager.LoadFolders(id).Select(folder => Path.Combine(folder, "Assemblies")).Where(Directory.Exists)
            .SelectMany(folder => Directory.GetFiles(folder, "*.dll"))
            .Where(file => !Path.GetFileNameWithoutExtension(file).Equals(id, StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            if (!loaded.Add(AssemblyName.GetAssemblyName(file).Name))
                continue;
            HotfixModContentPack.assemblies.loadedAssemblies.Add(Assembly.LoadFrom(file));
            GenTypes.ClearCache();
        }
    }

    public static IReadOnlyList<string> Apply(string id, byte[] image)
    {
        Remove(id);
        LoadLibraries(id);
        var harmony = new Harmony("seohyeon.hotfix.sub." + id);
        var assembly = Assembly.Load(image);
        Type[] startup;
        try
        {
            HotfixModContentPack.assemblies.loadedAssemblies.Add(assembly);
            GenTypes.ClearCache();
            harmony.PatchAll(assembly);
            startup = assembly.GetTypes().Where(type => type.HasAttribute<StaticConstructorOnStartup>()).ToArray();
        }
        catch
        {
            harmony.UnpatchAll(harmony.Id);
            Drop(assembly);
            throw;
        }
        Applied[id] = (harmony, assembly, image);
        if (PlayDataLoader.Loaded)
        {
            foreach (var type in startup)
            {
                try
                {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
                catch (Exception e)
                {
                    Log.Error($"Error in static constructor of {type}: {e}");
                }
            }
        }
        return Methods(harmony);
    }

    public static void Remove(string id)
    {
        if (!Applied.Remove(id, out var applied))
            return;
        applied.Harmony.UnpatchAll(applied.Harmony.Id);
        Drop(applied.Assembly);
    }

    private static void Drop(Assembly assembly)
    {
        foreach (var mod in LoadedModManager.RunningModsListForReading)
            mod.assemblies.loadedAssemblies.Remove(assembly);
        GenTypes.ClearCache();
        ScriptReferences.Exclude(assembly);
    }

    private static List<string> Methods(Harmony harmony) =>
        harmony.GetPatchedMethods().Select(method => $"{method.DeclaringType?.FullName}.{method.Name}").ToList();
}
