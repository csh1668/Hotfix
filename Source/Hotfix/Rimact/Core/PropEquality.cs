using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rimact;

// Props equality for .Memo(). Every render makes new lambdas and modifier arrays, so record equality almost never holds.
// Nodes compare by field, arrays by element, delegates by method and target, closures by captured values.
// Equal closures behave the same, so a skipped render can't keep a stale handler
internal static class PropEquality
{
    private const int MaxDepth = 32;
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> s_fields = new();

    public static bool Equal(object? a, object? b) => Equal(a, b, 0);

    // Hot reload may have added fields
    public static void ClearCache() => s_fields.Clear();

    private static bool Equal(object? a, object? b, int depth)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        var type = a.GetType();
        if (type != b.GetType()) return false;
        if (depth > MaxDepth) return false;
        switch (a)
        {
            case Delegate da:
                {
                    var db = (Delegate)b;
                    if (da.Method != db.Method) return false;
                    if (da.HasSingleTarget) return Equal(da.Target, db.Target, depth + 1);
                    var la = da.GetInvocationList();
                    var lb = db.GetInvocationList();
                    if (la.Length != lb.Length) return false;
                    for (int i = 0; i < la.Length; i++)
                        if (!Equal(la[i], lb[i], depth + 1)) return false;
                    return true;
                }
            case Node:
                return FieldsEqual(a, b, type, depth);
            case Array xa:
                {
                    var xb = (Array)b;
                    if (xa.Length != xb.Length) return false;
                    for (int i = 0; i < xa.Length; i++)
                        if (!Equal(xa.GetValue(i), xb.GetValue(i), depth + 1)) return false;
                    return true;
                }
        }
        if (type.IsDefined(typeof(CompilerGeneratedAttribute), false) && !type.IsValueType)
            return FieldsEqual(a, b, type, depth); // closure display class
        return a.Equals(b);
    }

    private static bool FieldsEqual(object a, object b, Type type, int depth)
    {
        foreach (var f in s_fields.GetOrAdd(type, Fields))
            if (!Equal(f.GetValue(a), f.GetValue(b), depth + 1)) return false;
        return true;
    }

    private static FieldInfo[] Fields(Type type)
    {
        var list = new System.Collections.Generic.List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                list.Add(f);
        return list.ToArray();
    }
}
