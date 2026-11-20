using System.Linq;
using Verse;

namespace Hotfix;

internal static class GameLog
{

    private sealed record LogMark(LogMessage Entry, int Repeats);

    public static object? Mark() => Log.Messages.LastOrDefault() is { } last ? new LogMark(last, last.repeats) : null;

    public static string Since(object? mark, int max = 20)
    {
        var entries = Log.Messages.ToList();
        var from = 0;
        var again = 0;
        LogMessage? repeated = null;
        if (mark is LogMark(var last, var repeats) && entries.IndexOf(last) is >= 0 and var index)
        {
            again = last.repeats - repeats;
            from = again > 0 ? index : index + 1;
            repeated = again > 0 ? last : null;
        }

        return string.Join("\n", entries.Skip(from).Where(entry => entry.type != LogMessageType.Message).TakeLast(max)
            .Select(entry =>
            {
                int count = entry == repeated ? again : entry.repeats;
                bool capped = entry.repeats >= 99;
                return $"[{entry.type}] {(count > 1 || capped ? $"(x{count}{(capped ? "+" : "")}) " : "")}"
                       + (entry.text.Length > 500 ? entry.text[..500] + "..." : entry.text);
            }));
    }
}
