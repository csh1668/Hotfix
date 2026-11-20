using System.Collections.Generic;

namespace Rimact;

internal interface IContext
{
    object? DefaultBoxed { get; }
    string Name { get; }
}

/// <summary>Value for a subtree, set with Provide and read with UseContext. Default applies outside any provider.</summary>
internal sealed class Context<T>(T defaultValue, string? name = null) : IContext
{
    public T Default { get; } = defaultValue;
    public string Name { get; } = name ?? typeof(T).Name;
    object? IContext.DefaultBoxed => Default;

    public static bool ValueEquals(object? a, object? b) => EqualityComparer<object?>.Default.Equals(a, b);
}
