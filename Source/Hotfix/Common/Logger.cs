using Verse;

namespace Hotfix;

internal static class Logger
{
    private const string LogPrefix = "<color=#fcba03>[Hotfix]</color> ";

    public static void Message(string message, bool debugOnly = false)
    {
        if (!debugOnly || Prefs.DevMode)
            Log.Message(LogPrefix + message);
    }

    public static void Warning(string message, bool debugOnly = false, object? onceKey = null)
    {
        if (debugOnly && !Prefs.DevMode) return;

        if (onceKey is null)
            Log.Warning(LogPrefix + message);
        else
            Log.WarningOnce(LogPrefix + message, onceKey.GetHashCode());
    }

    public static void Error(string message, bool debugOnly = false, object? onceKey = null)
    {
        if (debugOnly && !Prefs.DevMode) return;

        if (onceKey is null)
            Log.Error(LogPrefix + message);
        else
            Log.ErrorOnce(LogPrefix + message, onceKey.GetHashCode());
    }
}