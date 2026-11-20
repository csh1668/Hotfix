using System;
using Verse;

namespace Rimact;

/// <summary>Text in Rimact's own components. Rimact ships no translations, so the using mod sets these.</summary>
/// <remarks>Each mod compiles its own Rimact, so setting Default once after language data loads covers the whole mod.</remarks>
/// <example><code>Strings.Default = Strings.Translated("MyMod.Rimact.");</code></example>
internal sealed record Strings
{
    public static Strings Default { get; set; } = new();
    public static readonly Context<Strings?> Context = new(null, "Strings");

    public string Search { get; init; } = "Search...";
    public string NoResults { get; init; } = "No results.";
    public string Confirm { get; init; } = "Confirm";
    public string Cancel { get; init; } = "Cancel";
    public string Retry { get; init; } = "Retry";
    public string ErrorTitle { get; init; } = "UI error";
    /// <summary>The filter choice that shows everything.</summary>
    public string All { get; init; } = "All";
    /// <summary>The empty choice in pickers that allow none.</summary>
    public string None { get; init; } = "None";

    /// <summary>Reads keys named prefix + property name. Missing keys use vanilla's word if it has one, then English.</summary>
    public static Strings Translated(string prefix)
    {
        var d = new Strings();
        string T(string name, string fallback) => Elements.T(prefix + name, fallback);
        string V(string name, string fallback) => T(name, Elements.T(name, fallback)); // vanilla's key has the same name
        return new Strings
        {
            Search = T(nameof(Search), d.Search),
            NoResults = T(nameof(NoResults), d.NoResults),
            Confirm = V(nameof(Confirm), d.Confirm),
            Cancel = V(nameof(Cancel), d.Cancel),
            Retry = T(nameof(Retry), d.Retry),
            ErrorTitle = T(nameof(ErrorTitle), d.ErrorTitle),
            All = T(nameof(All), d.All),
            None = V(nameof(None), d.None),
        };
    }
}

internal static partial class Elements
{
    public static ProviderNode StringsProvider(Strings strings, Node child) => Provide(Strings.Context, (Strings?)strings, child);

    /// <summary>Translates key, or returns fallback if neither the active language nor English has it.</summary>
    /// <remarks>args fill {0}, {1}... in either text. Color tags in the result draw as colors.</remarks>
    public static string T(string key, string fallback, params object[] args)
    {
        if (TryTranslate(key, args, out var text)) return text;
        if (args.Length == 0) return fallback;
        try
        {
            return string.Format(fallback, args);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static bool TryTranslate(string key, object[] args, out string text)
    {
        text = "";
        try
        {
            if (LanguageDatabase.activeLanguage is null) return false;
            // Translate() falls back to English itself. Only a key missing from both uses our fallback
            if (!key.CanTranslate() && LanguageDatabase.defaultLanguage?.HaveTextForKey(key) != true) return false;
            if (args.Length == 0)
            {
                text = Components.GameText.Resolve(key.Translate());
                return true;
            }
            var named = new NamedArgument[args.Length];
            for (int i = 0; i < args.Length; i++) named[i] = new NamedArgument(args[i], null);
            text = Components.GameText.Resolve(key.Translate(named));
            return true;
        }
        catch (Exception)
        {
            // No language database outside a loaded game (tests, early startup)
            return false;
        }
    }
}
