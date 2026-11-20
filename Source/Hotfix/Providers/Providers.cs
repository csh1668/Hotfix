using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Hotfix;

public static class Providers
{
    public static IReadOnlyList<Provider_Base> List { get; }

    static Providers()
    {
        var list = new List<Provider_Base>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && typeof(Provider_Base).IsAssignableFrom(t)))
        {
            try
            {
                if (Activator.CreateInstance(type) is Provider_Base instance)
                    list.Add(instance);
            }
            catch (Exception e)
            {
                Logger.Warning($"Failed to instantiate providers for {type}: {e.Message}");
            }
        }

        List = list;
    }


    public static bool TryGet(string id, [MaybeNullWhen(false)] out Provider_Base provider)
    {
        provider = List.FirstOrDefault(p => p.Id == id);
        return provider != null;
    }
}