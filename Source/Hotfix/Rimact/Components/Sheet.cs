using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal enum SheetSide : byte { Right, Left, Bottom, Top }

    /// <summary>Panel that slides in from an edge. Backdrop click, Esc and the close button call OnOpenChange(false).</summary>
    /// <remarks>Covers the whole host wherever it sits in the tree. Use Dialog for a separate window.</remarks>
    internal sealed record Sheet(bool Open, Action<bool>? OnOpenChange, Node Content) : UIComponent
    {
        public SheetSide Side { get; init; }
        /// <summary>Panel width, or height for Top and Bottom.</summary>
        public float Size { get; init; } = 360f;
        public string? Title { get; init; }
        public string? Description { get; init; }

        protected internal override Node? Build()
        {
            var t = UseTheme();
            float hidden = UseAnimated(Open ? 0f : 1f, 0.2f, from: 1f);
            if (!Open && hidden >= 0.999f) return null;
            var onOpenChange = OnOpenChange;
            void Close() => onOpenChange?.Invoke(false);

            float shift = hidden * (Size + t.ShadowLg);
            var offset = Side switch
            {
                SheetSide.Left => new Vector2(-shift, 0),
                SheetSide.Bottom => new Vector2(0, shift),
                SheetSide.Top => new Vector2(0, -shift),
                _ => new Vector2(shift, 0),
            };
            bool horizontal = Side is SheetSide.Left or SheetSide.Right;
            var panel = Column(
                    Row(
                            Title is null ? Spacer() : new Heading(Title, 2).Grow().Shrink(),
                            IconButton(BuiltinIcon.Close, Close).Size(24, 24))
                        .Gap(t.Space(2)).Align(Align.Center),
                    Description is null ? null : Muted(Description),
                    Column(Content).Grow().Shrink().Clip())
                .Padding(t.Space(4)).Gap(t.Space(3))
                .Bg(t.Surface).Bevel(t.Raised(t.Surface, t.BevelWidthSm)).Border(t.Outline).Shadow(t.ShadowLg, t.ShadowColor)
                .Offset(offset.x, offset.y)
                .OnMouseDown(_ => true); // Clicks inside the panel shouldn't close it
            panel = horizontal ? panel.Width(Size) : panel.Height(Size);

            var body = Side switch
            {
                SheetSide.Left => Row(panel, Spacer()),
                SheetSide.Bottom => Column(Spacer(), panel),
                SheetSide.Top => Column(panel, Spacer()),
                _ => Row(Spacer(), panel),
            };
            return Overlay(body.Bg(new Color(0f, 0f, 0f, 0.45f * (1f - hidden))).OnClick(Close).Focusable(false), Placement.Fill, onEscape: Close);
        }
    }

    /// <summary>Card shown next to Trigger on hover. Stays open while the pointer is over either, so it can be clicked.</summary>
    internal sealed record HoverCard(Node Trigger, Node Content) : UIComponent
    {
        /// <summary>Seconds of hover before it opens.</summary>
        public float OpenDelay { get; init; } = 0.5f;
        public float CloseDelay { get; init; } = 0.15f;
        public Placement Placement { get; init; } = Placement.Below;
        public float Width { get; init; } = 280f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var open = UseState(false);
            var overTrigger = UseState(false);
            var overCard = UseState(false);
            bool want = overTrigger.Value || overCard.Value;
            UseInterval(() => open.Set(want), want == open.Value ? 0f : want ? OpenDelay : CloseDelay);
            return Stack(
                Trigger.OnHover(overTrigger.Set),
                open.Value
                    ? Overlay(
                        new FadeIn(Column(Content).Width(Width).Padding(t.Space(3)).Gap(t.Space(2))
                            .Bg(t.SurfaceRaised).Radius(t.RadiusLg).Bevel(t.Raised(t.SurfaceRaised, t.BevelWidthSm)).Border(t.Outline)
                            .Shadow(t.ShadowLg, t.ShadowColor)
                            .OnHover(overCard.Set)),
                        Placement)
                    : null);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Sheet Sheet(bool open, Action<bool>? onOpenChange, Node content,
            Components.SheetSide side = Components.SheetSide.Right) => new(open, onOpenChange, content) { Side = side };

        public static Components.HoverCard HoverCard(Node trigger, Node content, Placement placement = Placement.Below) =>
            new(trigger, content) { Placement = placement };
    }
}
