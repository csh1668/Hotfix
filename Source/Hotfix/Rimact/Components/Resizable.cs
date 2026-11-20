using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Two panes split by a draggable handle. Panes clip their content.</summary>
    internal sealed record Resizable(Node First, Node Second) : UIComponent
    {
        /// <summary>Panes top to bottom.</summary>
        public bool Vertical { get; init; }
        /// <summary>First pane's initial share. The split is internal state after that.</summary>
        public float Fraction { get; init; } = 0.5f;
        /// <summary>Minimum pane size in pixels.</summary>
        public float MinSize { get; init; }
        /// <summary>New fraction when a drag ends. Save it and pass it back as Fraction.</summary>
        public Action<float>? OnResize { get; init; }
        public float HandleSize { get; init; } = 6f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var self = UseFiber();
            var split = UseState(Mathf.Clamp01(Fraction));
            var grab = UseRef(0f);
            var dragging = UseState(false);
            bool vertical = Vertical;
            float handle = HandleSize, min = MinSize;
            var onResize = OnResize;

            float FractionAt(Vector2 p)
            {
                var r = self.Rect;
                float total = (vertical ? r.height : r.width) - handle;
                if (total <= 0f) return split.Value;
                float at = (vertical ? p.y - r.y : p.x - r.x) - grab.Current;
                float lo = Mathf.Clamp01(min / total);
                return Mathf.Clamp(at / total, lo, Mathf.Max(lo, 1f - lo));
            }

            float f = split.Value;
            // Stack stretches the child over the pane so growing content gets the room
            Node Pane(Node n, float grow) => vertical ? Stack(n).Grow(grow).Height(0).Clip() : Stack(n).Grow(grow).Width(0).Clip();
            var line = Row().Bg(dragging.Value ? t.Indicator : t.Border);
            var grip = (vertical ? Column(line.Height(1).FillWidth()) : Row(line.Width(1).FillHeight())).Center()
                .HoverBg(Theme.WithAlpha(t.Indicator, 0.12f))
                .OnMouseDown(e =>
                {
                    grab.Current = vertical ? e.Local.y : e.Local.x;
                    dragging.Set(true);
                    return true;
                })
                .OnDrag(e => split.Set(FractionAt(e.Position)))
                .OnMouseUp(e =>
                {
                    float done = FractionAt(e.Position);
                    split.Set(done);
                    dragging.Set(false);
                    onResize?.Invoke(done);
                    return true;
                })
                .Key("handle");
            grip = vertical ? grip.Height(handle) : grip.Width(handle);
            var panes = new Node[] { Pane(First, f), grip, Pane(Second, 1f - f) };
            return vertical ? Column(panes) : Row(panes);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Resizable Resizable(Node first, Node second, float fraction = 0.5f) => new(first, second) { Fraction = fraction };

    }
}
