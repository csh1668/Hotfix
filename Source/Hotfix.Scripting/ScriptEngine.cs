using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Hotfix.Scripting;

internal sealed class ScriptContext(string name) : AssemblyLoadContext(name);

public sealed class ScriptEngine : IScriptEngine
{
    private static readonly string[] Usings =
    [
        "System", "System.Collections.Generic", "System.Linq", "System.Text", "System.Reflection",
        "UnityEngine", "Verse", "Verse.AI", "RimWorld", "RimWorld.Planet", "HarmonyLib", "Hotfix.Scripting",
    ];

    private readonly Lock _lock = new();
    private List<MetadataReference> _references = [];
    private List<string> _names = [];
    private CSharpCompilationOptions _options = null!;
    private int _assemblies = -1;
    private int _states;

    public IScriptState CreateState() => new ScriptState(this, Interlocked.Increment(ref _states));

    public byte[] Compile(string assemblyName, IReadOnlyDictionary<string, string> sources) => Compile(assemblyName, sources, new BuildOptions());

    public byte[] Compile(string assemblyName, IReadOnlyDictionary<string, string> sources, BuildOptions build)
    {
        List<MetadataReference> references;
        CSharpCompilationOptions options;
        lock (_lock)
        {
            options = Setup().Options;
            references = _references.Where((_, i) => !build.Exclude.Contains(_names[i], StringComparer.OrdinalIgnoreCase)).ToList();
        }
        references.AddRange(build.References.Select(file => MetadataReference.CreateFromFile(file)));
        var version = LanguageVersion.Latest;
        if (build.LanguageVersion is { } text && !LanguageVersionFacts.TryParse(text, out version))
            throw new ScriptException($"Unknown language version: {text}");
        var parse = CSharpParseOptions.Default.WithLanguageVersion(version)
            .WithPreprocessorSymbols(build.Debug ? build.Defines.Append("DEBUG") : build.Defines);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, parse, source.Key, Encoding.UTF8));
        using var image = new MemoryStream();
        var compilation = CSharpCompilation.Create(assemblyName, trees, references,
            options.WithUsings().WithOptimizationLevel(build.Debug ? OptimizationLevel.Debug : OptimizationLevel.Release));
        var emit = build.Debug ? new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded) : null;
        Check(compilation.Emit(image, options: emit).Diagnostics, compilation);
        return image.ToArray();
    }

    internal static void Check(IEnumerable<Diagnostic> diagnostics, CSharpCompilation? compilation = null)
    {
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(20).ToList();
        if (errors.Count == 0)
            return;
        // 같은 실수가 여러 번 나오면 도움말은 한 번만 붙인다
        var hints = new HashSet<string>();
        throw new ScriptException("The code did not compile.\n" + string.Join("\n", errors.Select(error =>
            compilation is not null && ScriptHints.For(error, compilation) is { Length: > 0 } hint && hints.Add(hint) ? error + hint : error.ToString())));
    }

    internal (IReadOnlyList<MetadataReference> References, CSharpCompilationOptions Options) Setup()
    {
        lock (_lock)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && AssemblyLoadContext.GetLoadContext(assembly) is not ScriptContext
                    && !ScriptReferences.IsExcluded(assembly)).ToList();
            if (loaded.Count == _assemblies)
                return (_references, _options);

            _assemblies = loaded.Count;
            var found = loaded.Select(assembly => (Name: assembly.GetName().Name ?? "", Reference: Reference(assembly)))
                .Where(pair => pair.Reference is not null).ToList();
            _references = found.Select(pair => pair.Reference!).ToList();
            _names = found.Select(pair => pair.Name).ToList();
            var probe = CSharpCompilation.Create("Probe", references: _references);
            _options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: Usings.Where(name => Exists(probe, name)),
                    optimizationLevel: OptimizationLevel.Release, allowUnsafe: true)
                .WithMetadataImportOptions(MetadataImportOptions.All);
            
            var flags = typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags", BindingFlags.Instance | BindingFlags.NonPublic)!;
            flags.SetValue(_options, Enum.ToObject(flags.PropertyType, 1u << 22)); // IgnoreAccessibility = 1u << 22
            return (_references, _options);
        }
    }

    private static unsafe MetadataReference? Reference(Assembly assembly)
    {
        if (assembly.TryGetRawMetadata(out var blob, out var length))
            return AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((IntPtr)blob, length)).GetReference(display: assembly.GetName().Name);
        return string.IsNullOrEmpty(assembly.Location) ? null : MetadataReference.CreateFromFile(assembly.Location);
    }

    private static bool Exists(CSharpCompilation compilation, string name)
    {
        INamespaceSymbol? current = compilation.GlobalNamespace;
        foreach (var part in name.Split('.'))
        {
            current = current.GetNamespaceMembers().FirstOrDefault(member => member.Name == part);
            if (current is null)
                return false;
        }
        return true;
    }
}

internal sealed class ScriptState(ScriptEngine engine, int number) : IScriptState
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithKind(SourceCodeKind.Script).WithLanguageVersion(LanguageVersion.Latest);
    private static readonly EmitOptions EmitOptions = new(debugInformationFormat: DebugInformationFormat.PortablePdb);

    private readonly ScriptContext _context = new("HotfixScript" + number);
    private object?[] _slots = new object?[64];
    private CSharpCompilation? _previous;
    private int _committed;
    private int _prepared;

    public ScriptGlobals Globals { get; } = new();

    public IScriptRun Prepare(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, ParseOptions, $"script{++_prepared}.csx", Encoding.UTF8);
        ScriptEngine.Check(tree.GetDiagnostics());
        var guarded = tree.WithRootAndOptions(new ScriptGuard.Rewriter().Visit(tree.GetRoot()), ParseOptions);
        var (references, options) = engine.Setup();
        var compilation = CSharpCompilation.CreateScriptCompilation($"HotfixScript{number}_{_prepared}", guarded, references, options,
            _previous, typeof(object), typeof(ScriptGlobals));

        using var image = new MemoryStream();
        using var symbols = new MemoryStream();
        ScriptEngine.Check(compilation.Emit(image, symbols, options: EmitOptions).Diagnostics, compilation);
        image.Position = 0;
        symbols.Position = 0;
        var assembly = _context.LoadFromStream(image, symbols);
        var entry = compilation.GetEntryPoint(default)!;
        var factory = assembly.GetType(entry.ContainingType.MetadataName, true)!
            .GetMethod(entry.MetadataName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return new ScriptRun(this, compilation, factory);
    }

    internal object?[] Slots()
    {
        if (_committed + 2 > _slots.Length)
            Array.Resize(ref _slots, _slots.Length * 2);
        _slots[0] = Globals;
        return _slots;
    }

    internal void Commit(CSharpCompilation compilation)
    {
        _previous = compilation;
        _committed++;
    }

}

internal sealed class ScriptRun(ScriptState state, CSharpCompilation compilation, MethodInfo factory) : IScriptRun
{
    private const int MaxItems = 100;

    private readonly ScriptOutput _output = new();

    public TextWriter Output => _output;

    public string Execute(ScriptLimit limit)
    {
        var output = state.Globals.Output = _output;
        ScriptGuard.Limit = limit;
        try
        {
            var value = ((Task<object?>)factory.Invoke(null, [state.Slots()])!).GetAwaiter().GetResult();
            state.Commit(compilation);
            var text = output.ToString();
            return value is null ? text.TrimEnd() : text + "=> " + Format(value);
        }
        catch (Exception e)
        {
            var error = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
            throw new ScriptException(output.ToString() + Describe(error));
        }
        finally
        {
            ScriptGuard.Limit = null;
        }
    }

    internal static string Format(object? value)
    {
        if (value is null or string || value is not IEnumerable sequence)
            return Short(value);
        var lines = new List<string>();
        foreach (var item in sequence)
        {
            if (lines.Count == MaxItems)
            {
                lines.Add($"... (only the first {MaxItems} items are shown)");
                break;
            }
            lines.Add(Short(item));
        }
        return lines.Count == 0 ? "(empty)" : string.Join("\n", lines);
    }

    internal static string Short(object? value)
    {
        try
        {
            return value?.ToString() ?? "null";
        }
        catch (Exception e)
        {
            return $"<{value!.GetType().Name}: ToString threw {e.GetType().Name}>";
        }
    }

    // 스크립트와 그 아래의 프레임만 보여 준다
    private static string Describe(Exception error)
    {
        if (error is ScriptTimeoutException)
            return error.Message;
        var frames = (error.StackTrace ?? "").Split('\n').Select(line => line.TrimEnd())
            .TakeWhile(line => !line.Contains("Hotfix.Scripting.ScriptRun") && !line.Contains("System.RuntimeMethodHandle"))
            .Where(line => line.TrimStart().StartsWith("at ")).Take(12);
        return $"{error.GetType().FullName}: {error.Message}\n{string.Join("\n", frames)}".TrimEnd();
    }
}
