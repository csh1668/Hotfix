using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hotfix;

internal static partial class UiSnapshot
{
    private const string DiffNote =
        "Changes since the last snapshot (+ added, - removed, with everything under a removed line). The rest is unchanged and its refs still work. Pass full=true for the whole screen.";

    public static string Diff(string before, string after)
    {
        if (before == after)
            return "- diff: \"Nothing changed since the last snapshot.\"";
        
        string[] a = before.Split('\n'), b = after.Split('\n');
        var head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head])
            head++;
        var tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(1 + tail)] == b[^(1 + tail)])
            tail++;
        if (Changes(a[head..(a.Length - tail)], b[head..(b.Length - tail)]) is not { } middle)
            return after;

        List<(char Mark, string Line)> ops = [.. b[..head].Select(line => (' ', line)), .. middle, .. b[(b.Length - tail)..].Select(line => (' ', line))];

        int[] depths = new int[ops.Count], outerBefore = new int[ops.Count], outerAfter = new int[ops.Count];
        Stack<int> openBefore = new(), openAfter = new();
        for (var at = 0; at < ops.Count; at++)
        {
            var line = ops[at].Line;
            while (depths[at] < line.Length && char.IsWhiteSpace(line[depths[at]]))
                depths[at]++;
            if (ops[at].Mark != '+')
                outerBefore[at] = Outer(openBefore, at);
            if (ops[at].Mark != '-')
                outerAfter[at] = Outer(openAfter, at);
        }
        var kept = new bool[ops.Count];
        for (var at = ops.Count - 1; at >= 0; at--)
        {
            if (ops[at].Mark != '+' && outerBefore[at] >= 0 && (ops[at].Mark == ' ' || kept[at]))
                kept[outerBefore[at]] = true;
        }

        var shown = new bool[ops.Count];
        var changes = 0;
        for (var at = 0; at < ops.Count; at++)
        {
            if (ops[at].Mark == ' ')
                continue;
            var outer = ops[at].Mark == '-' ? outerBefore : outerAfter;
            if (ops[at].Mark == '-' && outer[at] >= 0 && ops[outer[at]].Mark == '-' && !kept[outer[at]])
                continue;
            changes++;
            shown[at] = true;
            for (var i = outer[at]; i >= 0; i = outer[i])
                shown[i] = true;
        }
        if (changes > b.Length / 2.0)
            return after;
        return string.Join("\n", [$"- diff: {Quote(DiffNote, 500)}", .. Enumerable.Range(0, ops.Count).Where(at => shown[at]).Select(at => $"{ops[at].Mark} {ops[at].Line}")]);

        int Outer(Stack<int> open, int at)
        {
            while (open.Count > 0 && depths[open.Peek()] >= depths[at])
                open.Pop();
            var outer = open.Count > 0 ? open.Peek() : -1;
            open.Push(at);
            return outer;
        }
    }

    // Bitset LCS
    private static List<(char Mark, string Line)>? Changes(string[] a, string[] b)
    {
        var words = (b.Length + 63) / 64;
        if ((long)(a.Length + 1) * words > 8_000_000)
            return null;
        
        var lines = new Dictionary<string, (int Last, int Count)>();
        var prev = new int[b.Length];
        for (var j = 0; j < b.Length; j++)
        {
            var (last, count) = lines.GetValueOrDefault(b[j], (-1, 0));
            prev[j] = last;
            lines[b[j]] = (j, count + 1);
        }
        var masks = new Dictionary<string, ulong[]>();
        
        var rows = new ulong[(a.Length + 1) * words];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            var row = i * words;
            if (lines.TryGetValue(a[i], out var line))
            {
                if (line.Count < words)
                {
                    Mark(rows, row, line.Last);
                }
                else
                {
                    if (!masks.TryGetValue(a[i], out var mask))
                        Mark(masks[a[i]] = mask = new ulong[words], 0, line.Last);
                    mask.CopyTo(rows, row);
                }
            }
            // 마법의 공식
            // 행 = X & (X ^ (X - Y)), X = 같은 줄의 자리 | 아랫행, Y = (아랫행 << 1) | 1
            ulong carry = 1, borrow = 0;
            for (var w = row; w < row + words; w++)
            {
                var below = rows[w + words];
                var have = rows[w] | below;
                var shifted = (below << 1) | carry;
                carry = below >> 63;
                var difference = have - shifted - borrow;
                borrow = have < shifted || (have == shifted && borrow != 0) ? 1UL : 0UL;
                rows[w] = have & (have ^ difference);
            }
        }

        var ops = new List<(char, string)>();
        int x = 0, y = 0;
        int here = Count(0), next = Count(1);
        while (x < a.Length && y < b.Length)
        {
            var bit = b.Length - 1 - y;
            if (a[x] == b[y])
            {
                ops.Add((' ', b[y]));
                x++;
                y++;
                here = next - Bit(x, bit);
                next = Count(x + 1);
            }
            else if (next >= here - Bit(x, bit))
            {
                ops.Add(('-', a[x++]));
                here = next;
                next = Count(x + 1);
            }
            else
            {
                ops.Add(('+', b[y++]));
                here -= Bit(x, bit);
                next -= Bit(x + 1, bit);
            }
        }
        while (x < a.Length)
            ops.Add(('-', a[x++]));
        while (y < b.Length)
            ops.Add(('+', b[y++]));
        return ops;

        void Mark(ulong[] bits, int at, int last)
        {
            for (var j = last; j >= 0; j = prev[j])
            {
                var bit = b.Length - 1 - j;
                bits[at + (bit >> 6)] |= 1UL << (bit & 63);
            }
        }

        int Count(int i)
        {
            if (i >= a.Length)
                return 0;
            int bits = b.Length - y, at = i * words, count = 0;
            for (; bits >= 64; bits -= 64)
                count += BitOperations.PopCount(rows[at++]);
            return bits > 0 ? count + BitOperations.PopCount(rows[at] << (64 - bits)) : count;
        }

        int Bit(int i, int bit) => (int)(rows[i * words + (bit >> 6)] >> (bit & 63)) & 1;
    }
}
