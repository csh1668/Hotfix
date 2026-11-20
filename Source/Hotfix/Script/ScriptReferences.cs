using System.Collections.Generic;
using System.Reflection;

namespace Hotfix;

public static class ScriptReferences
{
    private static readonly HashSet<Assembly> Excluded = [];

    public static void Exclude(Assembly assembly)
    {
        lock (Excluded)
            Excluded.Add(assembly);
    }

    public static bool IsExcluded(Assembly assembly)
    {
        lock (Excluded)
            return Excluded.Contains(assembly);
    }
}
