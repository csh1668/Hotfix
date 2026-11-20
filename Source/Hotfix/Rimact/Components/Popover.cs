using System;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Panel that opens next to Trigger on click. Closes on an outside click or Esc.</summary>
    internal sealed record Popover(Node Trigger, Node Content) : UIComponent
    {
        /// <summary>Set it to control the popover. Pair with OnOpenChange.</summary>
        public bool? Open { get; init; }
        public Action<bool>? OnOpenChange { get; init; }
        public Placement Placement { get; init; } = Placement.Below;
        public float Width { get; init; } = 260f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var inner = UseState(false);
            bool open = Open ?? inner.Value;

            void Set(bool v)
            {
                if (Open is null) inner.Set(v);
                OnOpenChange?.Invoke(v);
            }

            return Stack(
                Trigger.OnClick(() => Set(!open)),
                open
                    ? Overlay(
                        new FadeIn(Column(Content).Width(Width).Padding(t.Space(3)).Gap(t.Space(2))
                            .Bg(t.SurfaceRaised).Radius(t.RadiusLg).Bevel(t.Raised(t.SurfaceRaised, t.BevelWidthSm)).Border(t.Outline).Shadow(t.ShadowLg, t.ShadowColor)
                            .OnMouseDown(_ => true)), // Stop clicks inside from falling through
                        Placement,
                        onPointerDownOutside: () => Set(false),
                        onEscape: () => Set(false))
                    : null);
        }
    }
}

namespace Rimact.Components
{
    /// <summary>Fades Child in when it mounts.</summary>
    internal sealed record FadeIn(Node Child) : UIComponent
    {
        public float Seconds { get; init; } = 0.12f;

        protected internal override Node Build() => Child.Opacity(UseAnimated(1f, Seconds, from: 0f));
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Popover Popover(Node trigger, Node content, Placement placement = Placement.Below) =>
            new(trigger, content) { Placement = placement };
    }
}
