using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hotfix.Scripting;

public static class ScriptExtensions
{
    public static string Grep(this string text, string pattern, int context = 0, bool ignoreCase = false)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
            TimeSpan.FromSeconds(1));
        context = Math.Max(0, context);
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var hits = lines.Select(line => regex.IsMatch(line)).ToArray();
        var output = new List<string>();
        var last = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!hits[i])
                continue;
            var from = Math.Max(Math.Max(0, i - context), last + 1);
            var to = Math.Min(lines.Length - 1, i + context);
            if (context > 0 && last >= 0 && from > last + 1)
                output.Add("--");
            for (var j = from; j <= to; j++)
                output.Add($"{j + 1}{(hits[j] ? ':' : '-')}{lines[j]}");
            last = to;
        }
        return output.Count == 0 ? "No matches found." : string.Join("\n", output);
    }
}
