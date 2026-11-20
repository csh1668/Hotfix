using System.Collections.Generic;
using System.Text;

namespace Rimact;

// The rich-text tags the game draws (color, b, i, size), as made by Colorize() and keyed translations
internal static class RichTags
{
    private static readonly string[] Names = { "color", "b", "i", "size" };

    // Cheap check before the slow paths
    public static bool Has(string text) => text.IndexOf('<') >= 0;

    public static string Strip(string text)
    {
        if (!Has(text)) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (TagAt(text, i, out int end, out _, out _))
            {
                i = end - 1;
                continue;
            }
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    public static int VisibleLength(string text) => Has(text) ? Strip(text).Length : text.Length;

    // Keeps the tags before the cut and closes any left open:
    // Cut("<color=red>Danger</color> zone", 3, "...") = "<color=red>Dan...</color>"
    public static string Cut(string text, int visible, string suffix)
    {
        var sb = new StringBuilder(text.Length + suffix.Length + 16);
        var open = new List<string>();
        int shown = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (TagAt(text, i, out int end, out string name, out bool closing))
            {
                if (shown >= visible && !closing) break; // an opening tag after the cut has nothing to wrap
                sb.Append(text, i, end - i);
                if (closing)
                {
                    int at = open.LastIndexOf(name);
                    if (at >= 0) open.RemoveAt(at);
                }
                else open.Add(name);
                i = end - 1;
                continue;
            }
            if (shown >= visible) break;
            sb.Append(text[i]);
            shown++;
        }
        // "Steel..." not "Steel ...", like plain truncation
        int len = sb.Length;
        while (len > 0 && sb[len - 1] == ' ') len--;
        sb.Length = len;
        sb.Append(suffix);
        for (int k = open.Count - 1; k >= 0; k--) sb.Append("</").Append(open[k]).Append('>');
        return sb.ToString();
    }

    // end = index just past the tag
    private static bool TagAt(string s, int i, out int end, out string name, out bool closing)
    {
        end = i;
        name = "";
        closing = false;
        if (s[i] != '<') return false;
        int close = s.IndexOf('>', i + 1);
        if (close < 0) return false;
        int p = i + 1;
        if (p < close && s[p] == '/')
        {
            closing = true;
            p++;
        }
        foreach (var n in Names)
        {
            if (close - p < n.Length || string.CompareOrdinal(s, p, n, 0, n.Length) != 0) continue;
            int after = p + n.Length;
            // <b> and </b> end right after the name. <color=...> and <size=...> take a value
            bool valid = after == close || (!closing && s[after] == '=' && (n == "color" || n == "size"));
            if (!valid) continue;
            name = n;
            end = close + 1;
            return true;
        }
        return false;
    }
}
