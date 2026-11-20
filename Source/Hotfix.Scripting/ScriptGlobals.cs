using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using HarmonyLib;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using Verse;

namespace Hotfix.Scripting;

public sealed class ScriptGlobals
{
    private static readonly Dictionary<string, CSharpDecompiler> Decompilers = [];

    internal ScriptOutput Output = new();

    public void Print(object? value) => Output.WriteLine(ScriptRun.Format(value));

    public void Dump(object? value, int depth = 1)
    {
        var text = new StringBuilder();
        Dump(text, value, depth, "");
        Output.Write(text.ToString());
    }

    public string Decompile(Type type)
    {
        var source = Source(type.Assembly, type.MetadataToken);
        var patched = Harmony.GetAllPatchedMethods().Where(method => method.DeclaringType == type)
            .Select(method => (method.Name, Counts: PatchCounts(method))).Where(pair => pair.Counts.Length > 0)
            .Select(pair => $"{pair.Name} ({pair.Counts})").ToList();
        return patched.Count == 0 ? source : $"// Methods with Harmony patches in this type: {string.Join(", ", patched)}\n{source}";
    }

    public string Decompile(MethodBase method)
    {
        var source = Source(method.Module.Assembly, method.MetadataToken);
        var patches = PatchLines(method);
        return patches.Count == 0
            ? source
            : "// Harmony patches on this method. The source below is the original, without them.\n"
                + string.Concat(patches.Select(line => $"// {line}\n")) + source;
    }

    // "Namespace.Type" 또는 "Namespace.Type:Method"
    public string Decompile(string name) => name.Contains(':') ? Decompile(FindMethod(name)) : Decompile(FindType(name));

    public string Errors(int count = 10)
    {
        var entries = Log.Messages.Where(entry => entry.type != LogMessageType.Message).ToList();
        return entries.Count == 0
            ? "The game log has no warnings or errors."
            : string.Join("\n\n", entries.TakeLast(count).Select(entry => $"[{entry.type}] {Repeats(entry)}{entry.text}\n{entry.StackTrace}"));
    }

    private static string Repeats(LogMessage entry) => entry.repeats > 1 ? $"(x{entry.repeats}{(entry.repeats >= 99 ? "+" : "")}) " : "";

    public string Patches(string name) => Patches(FindMethod(name));

    public string Patches(MethodBase method) =>
        PatchLines(method) is { Count: > 0 } lines ? string.Join("\n", lines) : "No Harmony patches.";

    private static List<string> PatchLines(MethodBase method)
    {
        var lines = new List<string>();
        if (Harmony.GetPatchInfo(method) is not { } info)
            return lines;
        Add("prefix", info.Prefixes);
        Add("postfix", info.Postfixes);
        Add("transpiler", info.Transpilers);
        Add("finalizer", info.Finalizers);
        return lines;

        void Add(string kind, IEnumerable<Patch> patches) => lines.AddRange(patches.Select(patch =>
            $"{kind} by {patch.owner}, priority {patch.priority}: {patch.PatchMethod.DeclaringType?.FullName}:{patch.PatchMethod.Name}"));
    }

    // "2 prefix, 1 postfix"
    private static string PatchCounts(MethodBase method)
    {
        if (Harmony.GetPatchInfo(method) is not { } info)
            return "";
        var counts = new[]
        {
            (info.Prefixes.Count, "prefix"), (info.Postfixes.Count, "postfix"),
            (info.Transpilers.Count, "transpiler"), (info.Finalizers.Count, "finalizer"),
        };
        return string.Join(", ", counts.Where(pair => pair.Count > 0).Select(pair => $"{pair.Count} {pair.Item2}"));
    }

    public string Locate(string defName)
    {
        var defs = GenDefDatabase.AllDefTypesWithDatabases().Select(type => GenDefDatabase.GetDefSilentFail(type, defName, false))
            .Where(def => def is not null).ToList();
        return defs.Count == 0 ? $"No def named {defName}." : string.Join("\n\n", defs.Select(Locate));
    }

    public string Locate(Def def)
    {
        var title = $"{def.GetType().Name} {def.defName}";
        if (def.modContentPack is not { } mod)
            return title + "\nMade in code, not loaded from a mod.";

        var files = def.fileName is null
            ? []
            : mod.foldersToLoadDescendingOrder.Select(folder => Path.Combine(folder, "Defs")).Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, def.fileName, SearchOption.AllDirectories)).Distinct().ToList();
        // 이름이 같은 파일이 여럿이면 def 이름이 적힌 것만 남긴다
        var named = files.Where(file => File.ReadAllText(file).Contains(def.defName)).ToList();
        if (named.Count > 0)
            files = named;
        return $"{title}\nMod: {mod.Name} ({mod.PackageIdPlayerFacing})\nMod folder: {mod.RootDir}\n"
            + (files.Count == 0 ? "File: none, made in code" : "File: " + string.Join("\n      ", files));
    }

    private static Type FindType(string name) =>
        AccessTools.TypeByName(name) ?? throw new ArgumentException($"No type named {name}. Use the full name with its namespace.");

    private static MethodBase FindMethod(string name) =>
        AccessTools.Method(name) ?? throw new ArgumentException($"No method {name}. Write it as Namespace.Type:Method.");

    private static string Source(Assembly assembly, int token)
    {
        if (string.IsNullOrEmpty(assembly.Location))
            throw new InvalidOperationException($"{assembly.GetName().Name} was not loaded from a file, so it cannot be decompiled.");
        CSharpDecompiler decompiler;
        lock (Decompilers)
        {
            if (!Decompilers.TryGetValue(assembly.Location, out decompiler!))
            {
                Decompilers[assembly.Location] = decompiler =
                    new CSharpDecompiler(assembly.Location, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
            }
        }
        return decompiler.DecompileAsString(MetadataTokens.EntityHandle(token));
    }

    private static void Dump(StringBuilder text, object? value, int depth, string indent)
    {
        if (value is null or string || depth <= 0 || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            text.Append(ScriptRun.Short(value)).Append('\n');
            return;
        }
        if (value is IEnumerable sequence)
        {
            text.Append(value.GetType().Name).Append('\n');
            foreach (var item in sequence.Cast<object?>().Take(50))
            {
                text.Append(indent).Append("  - ");
                Dump(text, item, depth - 1, indent + "    ");
            }
            return;
        }
        text.Append(ScriptRun.Short(value)).Append(" (").Append(value.GetType().FullName).Append(")\n");
        for (var type = value.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                text.Append(indent).Append("  ").Append(field.Name).Append(" = ");
                Dump(text, field.GetValue(value), depth - 1, indent + "  ");
            }
        }
    }
}

internal sealed class ScriptOutput : StringWriter
{
    private const int MaxLength = 200_000;

    public ScriptOutput() => NewLine = "\n";

    public override void Write(char value)
    {
        if (GetStringBuilder().Length < MaxLength)
            base.Write(value);
    }

    public override void Write(string? value)
    {
        if (GetStringBuilder().Length < MaxLength)
            base.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (GetStringBuilder().Length < MaxLength)
            base.Write(buffer, index, count);
    }
}
