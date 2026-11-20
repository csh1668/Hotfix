using System;
using System.Collections.Generic;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal readonly record struct ScrollState(float Offset, float Viewport, float Content, Action<float> SetOffset)
    {
        // Adds to the latest offset, so several shifts in one pass add up.
        // Unlike SetOffset it leaves stick-to-end alone. Clamped on the next render
        public Action<float> Shift { get; init; } = _ => { };

        public float Max => Mathf.Max(0, Content - Viewport);
        public bool CanScroll => Max > 0.5f;
    }

    // Shared by the scroll containers below. Their roots clip, so a border on the container stays
    // above the scrolled content (see LayoutEngine.ClipArea)
    internal static class ScrollParts
    {
        public const float BarWidth = 8f;

        // Offset plus viewport and content sizes from the last layout. The offset is clamped right away.
        // stickToEnd follows the end until the user scrolls away, and sticks again back at the end
        public static ScrollState UseScroll(float? contentOverride = null, bool horizontal = false, bool stickToEnd = false)
        {
            var layout = UseLayout();
            var offset = UseState(0f);
            var atEnd = UseRef(true);
            float viewport = horizontal ? layout.Rect.width : layout.Rect.height;
            float content = contentOverride ?? (horizontal ? layout.ContentSize.x : layout.ContentSize.y);
            float max = Mathf.Max(0, content - viewport);
            float clamped = stickToEnd && atEnd.Current ? max : Mathf.Clamp(offset.Value, 0, max);
            return new ScrollState(clamped, viewport, content, v =>
            {
                float next = Mathf.Clamp(v, 0, max);
                atEnd.Current = next >= max - 0.5f;
                offset.Set(next);
            })
            {
                Shift = d => offset.Set(cur => Mathf.Max(0f, Mathf.Min(cur, max) + d)),
            };
        }

        // Horizontal uses the wheel's x, or its y while Shift is held
        public static Func<WheelEvent, bool> Wheel(ScrollState s, float speed, bool horizontal = false) => w =>
        {
            float delta = !horizontal ? (w.Shift ? 0f : w.Delta.y) : !Mathf.Approximately(w.Delta.x, 0) ? w.Delta.x : w.Shift ? w.Delta.y : 0f;
            if (!s.CanScroll || Mathf.Approximately(delta, 0)) return false;
            float next = Mathf.Clamp(s.Offset + delta * speed, 0, s.Max);
            if (Mathf.Approximately(next, s.Offset)) return false; // At either end, let the parent scroll
            s.SetOffset(next);
            return true;
        };

        // null when there is nothing to scroll. drag holds (pointer position at press, offset at press)
        public static Node? Scrollbar(Theme t, ScrollState s, Ref<Vector2> drag, bool horizontal = false)
        {
            if (!s.CanScroll || s.Viewport <= 0) return null;
            float thumb = Mathf.Max(20f, s.Viewport * s.Viewport / s.Content);
            float travel = Mathf.Max(1f, s.Viewport - thumb);
            float at = travel * (s.Offset / s.Max);
            float Along(Vector2 p) => horizontal ? p.x : p.y;
            var face = Row()
                .Visual(new Visual { Bg = t.BorderStrong, Bevel = t.Raised(t.BorderStrong, 1f), Border = t.Outline },
                    new Visual { Overlay = t.HoverOverlay },
                    new Visual { Overlay = t.PressedOverlay, Bevel = t.Raised(t.BorderStrong, 1f).Inverted })
                .AlignSelf(Align.Start)
                .OnMouseDown(e =>
                {
                    drag.Current = new Vector2(Along(e.Position), s.Offset);
                    return true;
                })
                .OnDrag(e => s.SetOffset(drag.Current.y + (Along(e.Position) - drag.Current.x) * (s.Max / travel)));
            return horizontal
                ? Stack(face.Size(thumb, BarWidth - 2).Offset(at, 1)).Size(Size.Fill, BarWidth).AlignSelf(Align.End)
                : Stack(face.Size(BarWidth - 2, thumb).Offset(1, at)).Size(BarWidth, Size.Fill).AlignSelf(Align.End);
        }
    }

    /// <summary>Scroll area with a draggable scrollbar. Vertical unless Horizontal is set.</summary>
    /// <remarks>No height of its own. Size it with .Grow(), .Height() or MaxHeight.</remarks>
    internal sealed record ScrollArea(Node Child) : UIComponent
    {
        public float WheelSpeed { get; init; } = 12f;
        /// <summary>Fits the content up to this height, then scrolls.</summary>
        public float? MaxHeight { get; init; }
        /// <summary>Lays the child out at its natural width. Scrolls with Shift+wheel or a horizontal wheel.</summary>
        public bool Horizontal { get; init; }
        /// <summary>Stays at the bottom as content is added, until the user scrolls up.</summary>
        public bool StickToBottom { get; init; }
        /// <summary>Shows a button at the bottom right that scrolls to the end, while not there. Vertical only.</summary>
        public bool JumpToBottom { get; init; }

        protected internal override Node Build()
        {
            var s = ScrollParts.UseScroll(horizontal: Horizontal, stickToEnd: StickToBottom && !Horizontal);
            var drag = UseRef(Vector2.zero);
            var view = UseLayout().Rect;
            if (Horizontal)
            {
                var row = Row(Child).ScrollX(s.Offset).FillHeight()
                    .Padding(new Edges(0, 0, 0, s.CanScroll ? ScrollParts.BarWidth + 2 : 0))
                    .OnReveal(r =>
                    {
                        if (r.xMin < view.xMin) s.SetOffset(s.Offset - (view.xMin - r.xMin));
                        else if (r.xMax > view.xMax) s.SetOffset(s.Offset + Mathf.Min(r.xMax - view.xMax, r.xMin - view.xMin));
                    });
                return Stack(row, ScrollParts.Scrollbar(UseTheme(), s, drag, horizontal: true)).Clip()
                    .OnWheel(ScrollParts.Wheel(s, WheelSpeed, horizontal: true));
            }
            var content = Column(Child).ScrollY(s.Offset).FillWidth()
                .Padding(new Edges(0, 0, s.CanScroll ? ScrollParts.BarWidth + 2 : 0, 0))
                .OnReveal(r =>
                {
                    if (r.yMin < view.yMin) s.SetOffset(s.Offset - (view.yMin - r.yMin));
                    else if (r.yMax > view.yMax) s.SetOffset(s.Offset + Mathf.Min(r.yMax - view.yMax, r.yMin - view.yMin));
                });
            if (MaxHeight is { } mh) content = content.MaxHeight(mh);
            const float jumpSize = 28f, jumpInset = 8f;
            Node? jump = JumpToBottom && s.Offset < s.Max - 0.5f
                ? IconButton(Glyph.ArrowDown, () => s.SetOffset(s.Max), variant: ButtonVariant.Default).Size(jumpSize, jumpSize)
                    .Offset(view.width - jumpSize - jumpInset - ScrollParts.BarWidth, s.Viewport - jumpSize - jumpInset).Key("jump")
                : null;
            return Stack(content, ScrollParts.Scrollbar(UseTheme(), s, drag), jump).Clip()
                .OnWheel(ScrollParts.Wheel(s, WheelSpeed));
        }
    }

    /// <summary>Fixed-height rows. Builds only the visible ones plus Overscan, so thousands of rows are fine.</summary>
    /// <remarks>Size it from outside with .Grow() or .Height().</remarks>
    internal sealed record VirtualList(int Count, float ItemHeight, Func<int, Node> RenderItem) : UIComponent
    {
        public Func<int, string>? KeyOf { get; init; }
        public int Overscan { get; init; } = 3;
        public float WheelSpeed { get; init; } = 12f;
        /// <summary>Scrolls just enough to show this row when the value changes.</summary>
        public int? RevealIndex { get; init; }
        /// <summary>Follows new rows at the end until the user scrolls up.</summary>
        public bool StickToBottom { get; init; }

        protected internal override Node Build()
        {
            float total = Count * ItemHeight;
            var s = ScrollParts.UseScroll(total, stickToEnd: StickToBottom);
            var drag = UseRef(Vector2.zero);
            float itemHeight = ItemHeight, offset = s.Offset, viewport = s.Viewport;
            var setOffset = s.SetOffset;
            UseEffect(() =>
            {
                if (RevealIndex is not { } reveal || viewport <= 0f) return;
                float top = reveal * itemHeight;
                if (top < offset) setOffset(top);
                else if (top + itemHeight > offset + viewport) setOffset(top + itemHeight - viewport);
            }, RevealIndex, viewport > 0f);

            int first = Mathf.Max(0, Mathf.FloorToInt(s.Offset / ItemHeight) - Overscan);
            int last = Mathf.Min(Count - 1, Mathf.CeilToInt((s.Offset + Mathf.Max(s.Viewport, ItemHeight)) / ItemHeight) + Overscan);
            var rows = new Node?[Mathf.Max(0, last - first + 1) + 2];
            rows[0] = first > 0 ? Gap(first * ItemHeight).Key("__top") : null;
            for (int i = first; i <= last; i++)
                rows[i - first + 1] = RenderItem(i).Height(ItemHeight).Key(KeyOf?.Invoke(i) ?? i.ToString());
            int after = Count - 1 - last;
            rows[^1] = after > 0 ? Gap(after * ItemHeight).Key("__bottom") : null;

            var content = Column(rows).ScrollY(s.Offset).FillWidth()
                .Padding(new Edges(0, 0, s.CanScroll ? ScrollParts.BarWidth + 2 : 0, 0));
            return Stack(content, ScrollParts.Scrollbar(UseTheme(), s, drag)).Clip()
                .OnWheel(ScrollParts.Wheel(s, WheelSpeed));
        }
    }
}

namespace Rimact.Components
{
    /// <summary>Virtualized list whose rows size themselves, for uneven heights like wrapped log lines.</summary>
    /// <remarks>Measured heights are remembered by key, so give rows stable keys. Size it from outside.</remarks>
    internal sealed record VariableList(int Count, Func<int, Node> RenderItem) : UIComponent
    {
        /// <summary>Height used for rows not measured yet.</summary>
        public float EstimatedHeight { get; init; } = 28f;
        public Func<int, string>? KeyOf { get; init; }
        public float Spacing { get; init; }
        public int Overscan { get; init; } = 3;
        public float WheelSpeed { get; init; } = 12f;
        /// <summary>Follows new rows at the end until the user scrolls up.</summary>
        public bool StickToBottom { get; init; }

        protected internal override Node Build()
        {
            var heights = UseRef(new Dictionary<string, float>());
            var measured = UseState(0);
            int count = Count;
            float spacing = Spacing;
            var keys = new string[count];
            // tops[i] = top of row i, tops[count] = content height
            var tops = new float[count + 1];
            for (int i = 0; i < count; i++)
            {
                keys[i] = KeyOf?.Invoke(i) ?? i.ToString();
                float h = heights.Current.TryGetValue(keys[i], out var known) ? known : EstimatedHeight;
                tops[i + 1] = tops[i] + h + (i < count - 1 ? spacing : 0f);
            }
            float total = tops[count];
            var s = ScrollParts.UseScroll(total, stickToEnd: StickToBottom);
            var drag = UseRef(Vector2.zero);

            int first = count == 0 ? 0 : Mathf.Max(0, RowAt(tops, count, s.Offset) - Overscan);
            int last = count == 0 ? -1 : Mathf.Min(count - 1, RowAt(tops, count, s.Offset + Mathf.Max(s.Viewport, EstimatedHeight)) + Overscan);
            float offset = s.Offset;
            var shift = s.Shift;
            // A sticky list at the bottom anchors to the end, not to the rows above
            bool anchorToEnd = StickToBottom && s.Offset >= s.Max - 0.5f;
            void Report(int index, string key, float height)
            {
                var map = heights.Current;
                bool known = map.TryGetValue(key, out var old);
                if (!known) old = EstimatedHeight;
                if (known && Mathf.Abs(old - height) < 0.5f) return;
                map[key] = height;
                // Keep visible rows still when a row above settles to its real height
                if (!anchorToEnd && tops[index] < offset && Mathf.Abs(old - height) >= 0.5f) shift(height - old);
                measured.Set(v => v + 1);
            }

            var rows = new List<Node?>(last - first + 3) { first > 0 ? Gap(tops[first]).Key("__top") : null };
            for (int i = first; i <= last; i++)
            {
                int index = i;
                string key = keys[i];
                var row = new MeasuredRow(RenderItem(i), h => Report(index, key, h)).Key(key);
                // Same shape for every row, so the last one keeps its state when a row is added after it
                rows.Add(spacing > 0 ? Column(row, Gap(i < count - 1 ? spacing : 0f)).Key(key) : row);
            }
            float after = last >= 0 ? total - tops[last + 1] : 0f;
            rows.Add(after > 0.5f ? Gap(after).Key("__bottom") : null);

            var content = Column(rows).ScrollY(s.Offset).FillWidth()
                .Padding(new Edges(0, 0, s.CanScroll ? ScrollParts.BarWidth + 2 : 0, 0));
            return Stack(content, ScrollParts.Scrollbar(UseTheme(), s, drag)).Clip()
                .OnWheel(ScrollParts.Wheel(s, WheelSpeed));
        }

        // Last row with top <= y
        private static int RowAt(float[] tops, int count, float y)
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (tops[mid] <= y) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }
    }

    // Reports the child's laid-out height when it changes
    internal sealed record MeasuredRow(Node Child, Action<float> Report) : UIComponent
    {
        protected internal override Node Build()
        {
            float h = UseLayout().Rect.height;
            var report = Report;
            UseEffect(() =>
            {
                if (h > 0f) report(h);
            }, h);
            return Child;
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.ScrollArea ScrollArea(params Node?[] children) => new(Column(children));

        /// <summary>Rows are keyed by index unless keyOf is given.</summary>
        public static Components.VirtualList VirtualList(int count, float itemHeight, Func<int, Node> renderItem, Func<int, string>? keyOf = null) =>
            new(count, itemHeight, renderItem) { KeyOf = keyOf };

        public static Components.VariableList VariableList(int count, Func<int, Node> renderItem, Func<int, string> keyOf) =>
            new(count, renderItem) { KeyOf = keyOf };
    }
}
