using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Vanilla always uses the game's button texture, whatever the theme says.</summary>
    internal enum ButtonVariant : byte { Default, Primary, Secondary, Outline, Ghost, Destructive, Link, Vanilla }

    internal enum ButtonSize : byte { Sm, Default, Lg, Icon }

    /// <summary>Button with an optional icon. Use Primary for the main action of a window.</summary>
    /// <example><code>Button("Save", Save, ButtonVariant.Primary) with { IsDefault = true }</code></example>
    internal sealed record Button(string? Label, Action? OnClick) : UIComponent
    {
        public ButtonVariant Variant { get; init; }
        public ButtonSize Size { get; init; } = ButtonSize.Default;
        public Icon? Icon { get; init; }
        public TextAlign TextAlign { get; init; } = TextAlign.MiddleCenter;
        public bool Sound { get; init; } = true;
        /// <summary>Enter key clicks it.</summary>
        public bool IsDefault { get; init; }
        /// <summary>Esc key clicks it.</summary>
        public bool IsCancel { get; init; }
        /// <summary>Shows a spinner and ignores clicks.</summary>
        public bool Loading { get; init; }

        protected internal override Node Build()
        {
            var theme = UseTheme();
            var host = UseHost();
            var onClick = OnClick;
            UseAcceptKey(IsDefault && !Loading ? onClick : null);
            UseCancelKey(IsCancel && !Loading ? onClick : null);

            float h = Size switch
            {
                ButtonSize.Sm => theme.ControlHeightSm,
                ButtonSize.Lg => theme.ControlHeightLg,
                _ => theme.ControlHeight,
            };
            float padX = Size switch
            {
                ButtonSize.Sm => theme.Space(2),
                ButtonSize.Lg => theme.Space(5),
                ButtonSize.Icon => 0,
                _ => theme.Space(3),
            };
            var (normal, hover, pressed) = Styles(theme, Variant);

            float iconSize = Size == ButtonSize.Sm ? 16 : 20;
            Node? icon = Loading ? new Spinner { Size = iconSize - 4 }
                : Icon is null ? null : Image(Icon).Size(iconSize, iconSize);
            Node? label = string.IsNullOrEmpty(Label) ? null : Text(Label!).Anchor(TextAlign).Truncate().Shrink();

            var row = Row(icon, label)
                .Height(h)
                .Padding(padX, 0)
                .Gap(theme.Space(1.5f))
                .Align(Align.Center)
                .Justify(TextAlign is TextAlign.MiddleLeft or TextAlign.UpperLeft or TextAlign.LowerLeft ? Justify.Start : Justify.Center)
                .Visual(normal, hover, pressed)
                .OnClick(() =>
                {
                    if (Sound) host.Backend.PlaySound(UiSound.Click);
                    OnClick?.Invoke();
                });
            if (Loading) row = row.Disabled();
            return Size == ButtonSize.Icon ? row.Width(h) : row;
        }

        internal static (Visual normal, Visual? hover, Visual? pressed) Styles(Theme t, ButtonVariant v) => v switch
        {
            ButtonVariant.Primary => Raised(t, t.Primary, t.PrimaryForeground, new Color(1f, 0.95f, 0.85f, 0.1f), outline: false),
            ButtonVariant.Secondary => Raised(t, t.Surface, t.SecondaryForeground, t.HoverOverlay, t.Foreground),
            ButtonVariant.Outline => (
                new Visual { Border = t.BorderStrong, Fg = t.Foreground, Radius = t.Radius },
                new Visual { Overlay = t.HoverOverlay },
                new Visual { Overlay = t.PressedOverlay }),
            ButtonVariant.Ghost => (
                new Visual { Fg = t.SecondaryForeground, Radius = t.Radius },
                new Visual { Overlay = t.HoverOverlay, Fg = t.Foreground },
                new Visual { Overlay = t.PressedOverlay }),
            ButtonVariant.Destructive => Raised(t, Color.Lerp(t.Destructive, Color.black, 0.35f), Color.white, new Color(1f, 1f, 1f, 0.08f)),
            ButtonVariant.Link => (
                new Visual { Fg = t.Link },
                new Visual { Fg = t.Hover },
                null),
            ButtonVariant.Vanilla => (
                new Visual { Part = VanillaPart.Button, Fg = Color.white },
                null,
                null),
            _ when t.VanillaParts => (
                new Visual { Part = VanillaPart.Button, Fg = t.Foreground },
                null,
                null),
            _ => Raised(t, t.SurfaceRaised, t.Foreground, t.HoverOverlay),
        };

        private static (Visual, Visual?, Visual?) Raised(Theme t, Color face, Color fg, Color hover, Color? hoverFg = null, bool outline = true)
        {
            var bevel = t.Raised(face, t.BevelWidth);
            return (
                new Visual { Bg = face, Fg = fg, Radius = t.Radius, Border = outline ? t.Outline : null, Bevel = bevel },
                new Visual { Overlay = hover, Fg = hoverFg },
                new Visual { Overlay = t.PressedOverlay, Bevel = bevel.Inverted });
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Button Button(string label, Action? onClick = null,
            Components.ButtonVariant variant = Components.ButtonVariant.Default,
            Components.ButtonSize size = Components.ButtonSize.Default, Icon? icon = null) =>
            new(label, onClick) { Variant = variant, Size = size, Icon = icon };

        /// <summary>Square icon-only button, ghost by default.</summary>
        public static Components.Button IconButton(Icon icon, Action? onClick = null, string? tooltip = null,
            Components.ButtonVariant variant = Components.ButtonVariant.Ghost) =>
            new Components.Button(null, onClick) { Variant = variant, Size = Components.ButtonSize.Icon, Icon = icon }.Tooltip(tooltip);
    }
}
