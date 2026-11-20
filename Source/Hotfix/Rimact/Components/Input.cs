using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Controlled text input on top of a vanilla IMGUI text field (caret, IME, clipboard).</summary>
    /// <remarks>Tab while typing moves focus to the next control.</remarks>
    internal sealed record Input(string Value, Action<string>? OnChange) : UIComponent
    {
        public string? Placeholder { get; init; }
        public Icon? Icon { get; init; }
        public bool Multiline { get; init; }
        /// <summary>Height when Multiline. Defaults to three control heights.</summary>
        public float? AreaHeight { get; init; }
        /// <summary>IMGUI control name for GUI.FocusControl. Generated when null.</summary>
        public string? ControlName { get; init; }
        /// <summary>Takes keyboard focus when mounted.</summary>
        public bool AutoFocus { get; init; }
        /// <summary>Destructive-colored border, e.g. while the text doesn't parse.</summary>
        public bool Invalid { get; init; }
        /// <summary>Stands in for a natural width, which a text field doesn't have. Widen with .Grow() or .Width().</summary>
        public float MinWidth { get; init; } = 120f;
        /// <summary>Node before the field, such as a prefix. Goes after Icon if both are set.</summary>
        public Node? Leading { get; init; }
        public Node? Trailing { get; init; }
        /// <summary>Shows a clear button while there is text. Clearing reports an empty string.</summary>
        public bool Clearable { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var self = UseFiber();
            var value = Value ?? "";
            var generated = UseMemo(() => Hooks.IdPrefix + "input." + ++s_names);
            var name = ControlName ?? generated;
            var chrome = UseMemo(() => new FieldChrome(t.Input, Invalid ? t.Destructive : t.Outline, Invalid ? t.Destructive : t.Ring, t.Radius, PadX,
                t.Recessed(t.Input, t.BevelWidthSm)), t, Invalid);
            UseEffect(() =>
            {
                if (AutoFocus) backend.FocusControl(name);
            }, System.Array.Empty<object>());
            UseKeyDown(k =>
            {
                if (k.Key != KeyCode.Tab || k.Ctrl || k.Alt || backend.FocusedControl != name) return false;
                backend.ClearFocus();
                return self.Host.MoveFocus(k.Shift, self);
            }, beforeTextFields: true);
            float h = Multiline ? AreaHeight ?? t.ControlHeight * 3 : t.ControlHeight;
            // With adornments the chrome wraps them and the field, and the field itself is bare
            bool adorned = Icon != null || Leading != null || Trailing != null || Clearable;
            var fieldChrome = adorned ? Bare : chrome;
            var field = Native(r =>
            {
                var next = backend.TextField(r, value, Multiline, name, fieldChrome);
                if (next != value) OnChange?.Invoke(next);
            }).FillWidth().FillHeight();

            Node? placeholder = string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(Placeholder)
                ? Elements.Text(Placeholder!).Color(t.MutedForeground).Anchor(Multiline ? TextAlign.UpperLeft : TextAlign.MiddleLeft)
                    .Truncate().Margin(new Edges(fieldChrome.PadX + 3, Multiline ? 4 : 0, fieldChrome.PadX, 0)).AlignSelf(Align.Stretch)
                : null;

            var box = Stack(field, placeholder).Height(h).Grow();
            // A text field's natural width is 0. MinWidth keeps it visible in a Row without Grow
            Node root;
            if (!adorned) root = Row(box).Height(h).MinWidth(MinWidth);
            else
            {
                var onChange = OnChange;
                Node? clear = Clearable && value.Length > 0
                    ? Row(Image(Glyph.X).Size(12, 12)).Size(20, 20).Center().Fg(t.MutedForeground).HoverFg(t.Foreground)
                        .OnClick(() => onChange?.Invoke("")).Focusable(false).Key("clear")
                    : null;
                var well = Canvas((p, r) =>
                {
                    bool focused = backend.FocusedControl == name;
                    p.FillRounded(r, chrome.Bg, chrome.Radius);
                    if (chrome.Bevel is { } bevel && r.width > 2f && r.height > 2f)
                        p.DrawBevel(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), bevel.Light, bevel.Dark, bevel.Width);
                    var border = focused ? chrome.FocusBorder : chrome.Border;
                    if (chrome.Radius > 0f) p.StrokeRounded(r, border, 1f, chrome.Radius);
                    else p.DrawBorder(r, border, 1f);
                });
                var content = Row(Icon is null ? null : Image(Icon).Size(16, 16).Tint(t.MutedForeground), Leading, box, Trailing, clear)
                    .Padding(PadX, 0).Gap(t.Space(1.5f)).Align(Multiline ? Align.Start : Align.Center)
                    .Fg(t.MutedForeground);
                root = Stack(well, content).Height(h).MinWidth(MinWidth);
            }
            return root.Focusable().OnFocus(on =>
            {
                if (on) backend.FocusControl(name);
                // Only when focus moved to another node. Cleared focus can mean a native control, maybe this field, took the click
                else if (self.Host.Focused != null && backend.FocusedControl == name) backend.ClearFocus();
            });
        }

        private const float PadX = 6f;
        private static readonly FieldChrome Bare = new(Color.clear, Color.clear, Color.clear, 0f, 2f);
        private static int s_names;
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Input Input(string value, Action<string>? onChange, string? placeholder = null, Icon? icon = null) =>
            new(value, onChange) { Placeholder = placeholder, Icon = icon };

        /// <summary>Multi-line Input, three control heights tall unless height is given.</summary>
        public static Components.Input Textarea(string value, Action<string>? onChange, float? height = null, string? placeholder = null) =>
            new(value, onChange) { Multiline = true, AreaHeight = height, Placeholder = placeholder };

        public static Components.Input SearchInput(string value, Action<string>? onChange, string? placeholder = null) =>
            new(value, onChange) { Placeholder = placeholder, Icon = BuiltinIcon.Search };
    }
}
