using System;
using System.IO;
using System.Reflection;

namespace Hotfix;

internal static class ScriptHost
{
    public static readonly Lazy<IScriptEngine> Engine = new(Load);

    private static IScriptEngine Load()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(HotfixMod.RootDir, "Lib", "Hotfix.Scripting.dll"));
        return (IScriptEngine)Activator.CreateInstance(assembly.GetType("Hotfix.Scripting.ScriptEngine", true)!)!;
    }
}
