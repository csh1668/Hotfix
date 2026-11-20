using System;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Shows Content in its own window while Open is true. The content keeps the parent's context.</summary>
    /// <remarks>Closing the window reports OnOpenChange(false). From inside, close it with UseHost().Close().</remarks>
    /// <example><code>Dialog(open.Value, open.Set, new SettingsPanel(), "Settings")</code></example>
    internal sealed record Dialog(bool Open, Action<bool>? OnOpenChange, Node Content) : UIComponent
    {
        public WindowOptions Options { get; init; } = new();

        private sealed class Handle
        {
            public Host? Host;
            public IWindowHandle? Window;
            public Action<bool>? OnOpenChange;
            public bool ClosingByProp;
        }

        protected internal override Node? Build()
        {
            var self = UseFiber();
            var backend = UseHost().Backend;
            var h = UseRef(new Handle()).Current;
            h.OnOpenChange = OnOpenChange;
            var content = Content;
            var options = Options;

            UseEffect(() =>
            {
                if (Open && h.Window is null)
                {
                    var child = new Host(content, backend) { ContextParent = self };
                    h.Host = child;
                    h.Window = backend.OpenWindow(new WindowRequest(child, options)
                    {
                        OnClosed = () =>
                        {
                            h.Window = null;
                            h.Host = null;
                            if (!h.ClosingByProp) h.OnOpenChange?.Invoke(false);
                            h.ClosingByProp = false;
                        },
                    });
                }
                else if (!Open && h.Window is { } w)
                {
                    h.ClosingByProp = true;
                    w.Close();
                }
            }, Open);

            // Latest content on every parent render
            UseEffect(() => h.Host?.SetRoot(content), null);

            // Options only when they change
            UseEffect(() => h.Window?.Update(options), options);

            // Close on unmount
            UseEffect(() => () =>
            {
                if (h.Window is { } w)
                {
                    h.ClosingByProp = true;
                    w.Close();
                }
            }, Array.Empty<object>());

            return null;
        }
    }

    /// <summary>Body of a confirm window: heading, text, Cancel and Confirm. Both buttons close the window.</summary>
    internal sealed record AlertContent(string Title, string Description) : UIComponent
    {
        public static readonly WindowOptions WindowOptions = new() { Size = new Vector2(420, 0), AutoHeight = true, Draggable = false };

        /// <summary>Defaults to Strings.Confirm.</summary>
        public string? ConfirmLabel { get; init; }
        /// <summary>Defaults to Strings.Cancel.</summary>
        public string? CancelLabel { get; init; }
        public Action? OnConfirm { get; init; }
        public bool Destructive { get; init; }

        protected internal override Node Build()
        {
            var host = UseHost();
            var strings = UseStrings();
            var onConfirm = OnConfirm;
            return Column(
                    Heading(Title),
                    Elements.Text(Description).Wrap().Anchor(TextAlign.UpperLeft),
                    DialogFooter(
                        Elements.Button(CancelLabel ?? strings.Cancel, host.Close, ButtonVariant.Outline) with { IsCancel = true },
                        Elements.Button(ConfirmLabel ?? strings.Confirm, () =>
                        {
                            onConfirm?.Invoke();
                            host.Close();
                        }, Destructive ? ButtonVariant.Destructive : ButtonVariant.Primary) with
                        { IsDefault = true }))
                .Gap(12);
        }
    }

    /// <summary>Confirm/cancel dialog. Both buttons close it and report OnOpenChange(false).</summary>
    internal sealed record AlertDialog(bool Open, Action<bool>? OnOpenChange, string Title, string Description) : UIComponent
    {
        /// <inheritdoc cref="AlertContent.ConfirmLabel"/>
        public string? ConfirmLabel { get; init; }
        /// <inheritdoc cref="AlertContent.CancelLabel"/>
        public string? CancelLabel { get; init; }
        public Action? OnConfirm { get; init; }
        public bool Destructive { get; init; }
        public float Width { get; init; } = 420;

        protected internal override Node Build()
        {
            var body = new AlertContent(Title, Description)
            {
                ConfirmLabel = ConfirmLabel,
                CancelLabel = CancelLabel,
                OnConfirm = OnConfirm,
                Destructive = Destructive,
            };
            return new Dialog(Open, OnOpenChange, body)
            {
                Options = AlertContent.WindowOptions with { Size = new Vector2(Width, 0) },
            };
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        /// <summary>500x400 unless size is given.</summary>
        public static Components.Dialog Dialog(bool open, Action<bool>? onOpenChange, Node content, string? title = null, Vector2? size = null) =>
            new(open, onOpenChange, content) { Options = new WindowOptions { Title = title, Size = size ?? new Vector2(500, 400) } };

        public static Components.Dialog Dialog(bool open, Action<bool>? onOpenChange, Node content, WindowOptions options) =>
            new(open, onOpenChange, content) { Options = options };

        public static Components.AlertDialog AlertDialog(bool open, Action<bool>? onOpenChange, string title, string description, Action? onConfirm,
            bool destructive = false, string? confirmLabel = null, string? cancelLabel = null) =>
            new(open, onOpenChange, title, description) { OnConfirm = onConfirm, Destructive = destructive, ConfirmLabel = confirmLabel, CancelLabel = cancelLabel };

        /// <summary>Right-aligned button row. Put it last in the dialog content.</summary>
        public static BoxNode DialogFooter(params Node?[] actions) => CardFooter(actions);
    }
}
