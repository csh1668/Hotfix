using System;
using System.Collections.Generic;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Vertical list with rows dragged by a grip. OnMove gets (from, to). Apply it to your list.</summary>
    /// <example><code>SortableList(order, m =&gt; m.PackageId, m =&gt; Text(m.Name), (from, to) =&gt; { var m = order[from]; order.RemoveAt(from); order.Insert(to, m); })</code></example>
    internal sealed record SortableList<T>(IReadOnlyList<T> Items, Func<T, string> KeyOf, Func<T, Node> Render, Action<int, int>? OnMove) : UIComponent
    {
        public float Gap { get; init; }

        private sealed record DragState(int From, float Grab, float Pointer, float[] Tops, float[] Heights)
        {
            // Drop index: after every other row whose center the dragged row has reached
            public int Target
            {
                get
                {
                    float center = Pointer - Grab + Heights[From] / 2f;
                    int target = 0;
                    for (int i = 0; i < Tops.Length; i++)
                        if (i != From && Tops[i] + Heights[i] / 2f <= center) target++;
                    return target;
                }
            }
        }

        protected internal override Node Build()
        {
            var self = UseFiber();
            var drag = UseState((DragState?)null);
            var live = UseRef<DragState?>(null);
            var d = drag.Value;
            int target = d?.Target ?? -1;
            var onMove = OnMove;

            void Grab(int index, PointerEvent e)
            {
                // Rows are the host children of our column. Read their laid-out rects
                var column = self.Children.Count > 0 ? self.Children[0] : null;
                if (column is null || column.Children.Count != Items.Count) return;
                var tops = new float[Items.Count];
                var heights = new float[Items.Count];
                for (int i = 0; i < tops.Length; i++)
                {
                    var row = column.Children[i];
                    var r = row.Rect;
                    float offset = row.Children.Count > 0 ? row.Children[0].Node.Layout.Offset.y : row.Node.Layout.Offset.y;
                    tops[i] = r.y - offset;
                    heights[i] = r.height;
                }
                var s = new DragState(index, e.Position.y - tops[index], e.Position.y, tops, heights);
                live.Current = s;
                drag.Set(s);
            }

            void Move(PointerEvent e)
            {
                if (live.Current is not { } s) return;
                live.Current = s with { Pointer = e.Position.y };
                drag.Set(live.Current);
            }

            void Drop(PointerEvent e)
            {
                if (live.Current is not { } s) return;
                live.Current = null;
                drag.Set((DragState?)null);
                int to = (s with { Pointer = e.Position.y }).Target;
                if (to != s.From) onMove?.Invoke(s.From, to);
            }

            var rows = new Node[Items.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                float shift = 0f;
                if (d != null)
                {
                    float room = d.Heights[d.From] + Gap;
                    if (i == d.From) shift = d.Pointer - d.Grab - d.Tops[i];
                    else if (d.From < i && i <= target) shift = -room;
                    else if (target <= i && i < d.From) shift = room;
                }
                int index = i;
                rows[i] = new SortableRow(Render(Items[i]), shift, d?.From == i, e => Grab(index, e), Move, Drop) { Index = i }.Key(KeyOf(Items[i]));
            }
            return Column(rows).Gap(Gap);
        }
    }

    internal sealed record SortableRow(Node Content, float Shift, bool Lifted, Action<PointerEvent> OnGrab, Action<PointerEvent> OnMoveTo, Action<PointerEvent> OnDrop) : UIComponent
    {
        // When it changes (a drop) the row snaps to its new slot without easing
        public int Index { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var lastIndex = UseRef(Index);
            bool moved = lastIndex.Current != Index;
            lastIndex.Current = Index;
            float eased = UseAnimated(Lifted ? 0f : Shift, moved ? 0f : 0.12f);
            float y = Lifted ? Shift : eased;
            var grab = OnGrab;
            var moveTo = OnMoveTo;
            var drop = OnDrop;
            var dots = t.MutedForeground;
            var grip = Canvas((p, r) =>
                {
                    var c = r.center;
                    for (int col = -1; col <= 1; col += 2)
                        for (int row = -1; row <= 1; row++)
                            p.FillRect(new Rect(Mathf.Round(c.x + col * 2.5f - 1f), Mathf.Round(c.y + row * 4f - 1f), 2f, 2f), dots);
                })
                .Size(14, 18).AlignSelf(Align.Center)
                .OnMouseDown(e =>
                {
                    grab(e);
                    return true;
                })
                .OnDrag(moveTo)
                .OnMouseUp(e =>
                {
                    drop(e);
                    return true;
                })
                .Key("grip");
            var row = Row(grip, Column(Content).Grow().Shrink()).Gap(t.Space(1)).Align(Align.Center).Offset(0, y);
            return Lifted
                ? row.Bg(t.SurfaceRaised).Border(t.Outline).Shadow(t.ShadowSm, t.ShadowColor)
                : row;
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.SortableList<T> SortableList<T>(IReadOnlyList<T> items, Func<T, string> key,
            Func<T, Node> render, Action<int, int>? onMove) => new(items, key, render, onMove);
    }
}
