using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Hotfix;

public sealed class UiSnapshotState
{
    public readonly Dictionary<string, string> Refs = [];
    public readonly Dictionary<string, Rect> Boxes = [];
    public string? Last;
}

internal static partial class UiSnapshot
{
    private const int MaxLinesPerWindow = 200;
    private const float MaxLooseIcon = 40f;

    private static readonly Regex Tags = new("</?(b|i|color|size)(=[^>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private sealed class Control(UiItem item)
    {
        public readonly UiItem Item = item;
        public readonly List<UiItem> Texts = [];
        public readonly List<Control> Children = [];
        public Control? Parent;
        public UiItem? Icon;
    }

    private sealed record Entry(Rect Box, Control? Control = null, string Text = "", bool Icon = false, List<Entry>? Group = null);

    private sealed class Node
    {
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<int, Node> _nodes = [];

        public void Add(int[]? path, Entry entry)
        {
            var node = this;
            foreach (var id in path ?? [])
            {
                if (!node._nodes.TryGetValue(id, out var next))
                    node._nodes[id] = next = new Node();
                node = next;
            }
            node._entries.Add(entry);
        }

        public List<Entry> Flatten()
        {
            foreach (var node in _nodes.Values)
            {
                var inner = node.Flatten();
                if (inner.Count == 1)
                    _entries.Add(inner[0]);
                else if (inner.Count > 1)
                    _entries.Add(new Entry(inner.MinBy(entry => (entry.Box.center.y, entry.Box.x))!.Box, Group: inner));
            }
            return _entries is [{ Group: { } only }] ? only : _entries;
        }
    }

    public static string Build(IReadOnlyList<UiWindow> windows, UiSnapshotState state)
    {
        var output = new List<string>();
        foreach (var window in windows)
        {
            var lines = new List<string>();
            var counts = new Dictionary<string, int>();
            Print(lines, "  ", Tree(window, out var loose), loose, window.Type, counts, state);
            if (lines.Count == 0)
                continue;
            var head = window.Type.Length == 0 ? "- screen" : window.Title.Length > 0 ? $"- window {Quote(window.Title)} [{window.Type}]" : $"- window [{window.Type}]";
            output.Add(head + ":");
            output.AddRange(lines.Take(MaxLinesPerWindow));
            if (lines.Count > MaxLinesPerWindow)
                output.Add($"  - text: \"({lines.Count - MaxLinesPerWindow} more lines are not shown)\"");
        }
        return output.Count > 0 ? string.Join("\n", output) : "- screen: \"Nothing is drawn.\"";
    }

    private static List<Control> Tree(UiWindow window, out List<UiItem> loose)
    {
        var texts = new List<UiItem>();
        var icons = new List<UiItem>();
        var controls = new List<Control>();
        var windowArea = window.Box.width * window.Box.height;
        foreach (var item in window.Items)
        {
            if (item.Role == "text")
            {
                var text = Clean(item.Text);
                if (text.Length > 0 && !texts.Any(other => other.Text == text && Near(other.Box, item.Box)))
                    texts.Add(item with { Text = text });
            }
            else if (item.Role == "image")
            {
                if (!icons.Any(other => other.Text == item.Text && Near(other.Box, item.Box)))
                    icons.Add(item);
            }
            else if (windowArea <= 0f || item.Box.width * item.Box.height < windowArea * 0.8f)
            {
                var same = controls.FindIndex(other => Near(other.Item.Box, item.Box));
                if (same < 0)
                    controls.Add(new Control(item));
                else if (Rank(item.Role) > Rank(controls[same].Item.Role))
                    controls[same] = new Control(item);
            }
        }

        controls.Sort((a, b) => Area(a.Item.Box).CompareTo(Area(b.Item.Box)));
        loose = [];
        foreach (var text in texts)
        {
            if (controls.FirstOrDefault(control => control.Item.Box.Contains(text.Box.center)) is { } owner)
                owner.Texts.Add(text);
            else
                loose.Add(text);
        }
        foreach (var icon in icons)
        {
            if (texts.Any(text => icon.Box.Contains(text.Box.center)))
                continue;
            if (controls.FirstOrDefault(control => control.Item.Box.Contains(icon.Box.center)) is { } owner)
            {
                if (owner.Icon is null || Area(icon.Box) >= Area(owner.Icon.Box))
                    owner.Icon = icon;
            }
            else if (icon.Box.width <= MaxLooseIcon && icon.Box.height <= MaxLooseIcon)
            {
                loose.Add(icon);
            }
        }
        for (var i = 0; i < controls.Count; i++)
        {
            controls[i].Parent = controls.Skip(i + 1).FirstOrDefault(outer => outer.Item.Box.Contains(controls[i].Item.Box.center));
            controls[i].Parent?.Children.Add(controls[i]);
        }
        return controls.Where(control => control.Parent is null).ToList();
    }

    private static void Print(List<string> lines, string indent, List<Control> controls, List<UiItem> texts, string window,
        Dictionary<string, int> counts, UiSnapshotState state)
    {
        var root = new Node();
        foreach (var control in controls)
            root.Add(control.Item.Groups, new Entry(control.Item.Box, control));
        foreach (var text in texts)
            root.Add(text.Groups, text.Role == "image" ? new Entry(text.Box, Text: $"[{text.Text}]", Icon: true) : new Entry(text.Box, Text: text.Text));
        Print(lines, indent, root.Flatten(), window, counts, state);
    }

    private static void Print(List<string> lines, string indent, List<Entry> entries, string window, Dictionary<string, int> counts,
        UiSnapshotState state)
    {
        var rows = new List<List<Entry>>();
        foreach (var entry in entries.OrderBy(entry => entry.Box.center.y))
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].Box.center.y - entry.Box.center.y) <= Math.Max(4f, Math.Min(rows[^1][0].Box.height, entry.Box.height) / 2f))
                rows[^1].Add(entry);
            else
                rows.Add([entry]);
        }

        foreach (var row in rows)
        {
            var run = new StringBuilder();
            var afterIcon = false;
            var hasText = false;
            foreach (var entry in row.OrderBy(entry => entry.Box.x))
            {
                if (entry.Group is null && entry.Control is null)
                {
                    if (run.Length > 0)
                        run.Append(afterIcon ? " " : " | ");
                    run.Append(entry.Text);
                    afterIcon = entry.Icon;
                    hasText |= !entry.Icon;
                    continue;
                }
                Flush();
                if (entry.Group is not null)
                    Print(lines, indent, entry.Group, window, counts, state);
                else
                    Print(lines, indent, entry.Control!, window, counts, state);
            }
            Flush();

            void Flush()
            {
                if (hasText)
                    lines.Add($"{indent}- text: {Quote(run.ToString(), 500)}");
                run.Clear();
                afterIcon = hasText = false;
            }
        }
    }

    private static void Print(List<string> lines, string indent, Control control, string window, Dictionary<string, int> counts,
        UiSnapshotState state)
    {
        var item = control.Item;
        var hasChildren = control.Children.Count > 0;
        var name = hasChildren ? "" : string.Join(" ", control.Texts.OrderBy(text => text.Box.center.y).ThenBy(text => text.Box.x).Select(text => text.Text));
        if (name.Length == 0)
            name = Clean(item.Text);
        if (name.Length == 0 && item.Role == "button" && !hasChildren && control.Icon is not null)
            name = $"[{control.Icon.Text}]";
        if (item.Role == "button" && name.Length == 0 && !hasChildren)
            return;

        var key = $"{window}|{item.Role}|{name}";
        counts[key] = counts.GetValueOrDefault(key) + 1;
        key += "|" + counts[key];
        if (!state.Refs.TryGetValue(key, out var reference))
            state.Refs[key] = reference = "e" + (state.Refs.Count + 1);
        state.Boxes[reference] = item.Box;

        var head = item.Role + (name.Length > 0 ? " " + Quote(name) : "") + (item.Checked ? " [checked]" : "") + $" [ref={reference}]";
        lines.Add($"{indent}- {head}{(hasChildren ? ":" : "")}");
        if (hasChildren)
            Print(lines, indent + "  ", control.Children, control.Texts, window, counts, state);
    }

    private static int Rank(string role) => role switch { "checkbox" => 3, "textbox" => 2, _ => 1 };

    private static float Area(Rect box) => box.width * box.height;

    private static bool Near(Rect a, Rect b) =>
        Math.Abs(a.x - b.x) <= 2f && Math.Abs(a.y - b.y) <= 2f && Math.Abs(a.width - b.width) <= 2f && Math.Abs(a.height - b.height) <= 2f;

    private static string Clean(string text) => Spaces.Replace(Tags.Replace(text, ""), " ").Trim();

    private static string Quote(string text, int max = 200)
    {
        if (text.Length > max)
            text = text[..max] + "...";
        return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
