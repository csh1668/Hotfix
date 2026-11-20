using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Build(Func<IScriptEngine> engine) : Tool_Base("Build",
    """
    Compiles C# source files into a DLL with the compiler that ships with Hotfix, so no .NET SDK is needed. It does not read project files: give what a .csproj would set as arguments.
    source is a folder; every .cs file below it is compiled, except those under bin and obj folders. output is the DLL to write and may be anywhere on the computer. Missing folders are created. A DLL that the running game has loaded from a file cannot be replaced until the game is closed.
    The code is compiled against every assembly loaded in the running game: the game, the .NET runtime, Harmony and all active mods. A loaded assembly with the same name as the one being built is left out, so that rebuilding a mod does not see its own old types. Private and internal members of other assemblies can be used directly.
    The DLL is only written. The game loads mod assemblies when it starts, so the result runs after a restart. Compile errors are returned with file and line.
    """,
    """
    {
      "type": "object",
      "properties": {
        "source": { "type": "string", "description": "Absolute path of the folder with the .cs files" },
        "output": { "type": "string", "description": "Absolute path of the DLL to write" },
        "assembly_name": { "type": "string", "description": "Assembly name, by default the file name of output without .dll" },
        "configuration": { "type": "string", "enum": ["Release", "Debug"], "description": "Release (default) optimizes. Debug does not optimize, defines DEBUG and embeds debug information in the DLL" },
        "defines": { "type": "array", "items": { "type": "string" }, "description": "Preprocessor symbols to define" },
        "language_version": { "type": "string", "description": "C# language version such as 7.3 or 12, by default the latest" },
        "references": { "type": "array", "items": { "type": "string" }, "description": "Absolute paths of more DLLs to reference, for assemblies that are not loaded in the game" },
        "exclude_references": { "type": "array", "items": { "type": "string" }, "description": "Names of loaded assemblies to leave out of the references" }
      },
      "required": ["source", "output"]
    }
    """)
{
    private static readonly string[] Skipped = ["bin", "obj"];

    public override bool NeedsApproval(JsonNode arguments, ToolContext context) =>
        !context.InScratch(ToolContext.FullPath(Required(arguments, "output")));

    public override Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var source = context.Readable(Required(arguments, "source"));
        var output = ToolContext.FullPath(Required(arguments, "output"));
        var name = arguments.GetString("assembly_name", Path.GetFileNameWithoutExtension(output));
        var configuration = arguments.GetString("configuration", "Release");
        if (configuration is not ("Release" or "Debug"))
            throw new ArgumentException("configuration must be Release or Debug");
        var build = new BuildOptions
        {
            Debug = configuration == "Debug",
            Defines = Strings(arguments, "defines"),
            LanguageVersion = arguments.GetString("language_version"),
            References = Strings(arguments, "references").Select(context.Readable).ToList(),
            Exclude = [name, .. Strings(arguments, "exclude_references")],
        };
        return Task.Run(() =>
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"No such folder: {source}");
            var files = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(source, file))
                .Where(file => !file.Split(Path.DirectorySeparatorChar).SkipLast(1).Any(part => Skipped.Contains(part, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToDictionary(file => file, file => File.ReadAllText(Path.Combine(source, file)));
            if (files.Count == 0)
                throw new FileNotFoundException($"There are no .cs files under {source}.");
            var image = engine().Compile(name, files, build);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, image);
            return $"Built {output} from {files.Count} file{(files.Count == 1 ? "" : "s")} ({configuration}, {image.Length:N0} bytes).";
        }, ct);
    }

    private static string[] Strings(JsonNode arguments, string name) =>
        arguments.GetArray(name).Select(item => item?.ToString()).OfType<string>().ToArray();
}
