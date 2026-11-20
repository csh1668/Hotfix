using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    internal enum ToastVariant : byte { Default, Success, Info, Warning, Destructive }

    /// <summary>The newest toast is always nearest the edge.</summary>
    internal enum ToastPosition : byte { BottomRight, BottomCenter, BottomLeft, TopRight, TopCenter, TopLeft }

    internal sealed record ToastOptions
    {
        public static readonly ToastOptions Default = new();

        public string? Description { get; init; }
        public ToastVariant Variant { get; init; }
        /// <summary>Seconds before it closes by itself. 0 or less keeps it until closed.</summary>
        public float Duration { get; init; } = 4f;
        /// <summary>Action button label, such as "Undo". The toast closes after OnAction runs.</summary>
        public string? ActionLabel { get; init; }
        public Action? OnAction { get; init; }
    }

    internal sealed record ToastEntry(int Id, string Title, ToastOptions Options);

    /// <summary>From UseToast. Can be kept and called later from outside the tree, on the main thread.</summary>
    internal sealed class ToastApi
    {
        internal static readonly Context<ToastApi?> Context = new(null, "Toaster");

        private readonly List<ToastEntry> _pending = new();
        private Action<ToastEntry>? _add;
        private Action<int>? _dismiss;
        private int _nextId;

        /// <summary>Returns an id for Dismiss.</summary>
        public int Show(string title, ToastOptions? options = null)
        {
            var e = new ToastEntry(++_nextId, title, options ?? ToastOptions.Default);
            if (_add != null) _add(e);
            else _pending.Add(e);
            return e.Id;
        }

        public int Success(string title, string? description = null) =>
            Show(title, new ToastOptions { Variant = ToastVariant.Success, Description = description });

        public int Error(string title, string? description = null) =>
            Show(title, new ToastOptions { Variant = ToastVariant.Destructive, Description = description });

        public void Dismiss(int id) => _dismiss?.Invoke(id);

        internal void Attach(Action<ToastEntry>? add, Action<int>? dismiss)
        {
            _add = add;
            _dismiss = dismiss;
            if (add is null) return;
            foreach (var e in _pending) add(e);
            _pending.Clear();
        }
    }

    /// <summary>Wrap a host's root in it to use UseToast. Showing a toast doesn't re-render the child.</summary>
    /// <example><code>new RimactHost(Toaster(new SettingsRoot()))</code></example>
    internal sealed record Toaster(Node Child) : UIComponent
    {
        public int Max { get; init; } = 3;
        public ToastPosition Position { get; init; }

        protected internal override Node Build()
        {
            var api = UseMemo(() => new ToastApi());
            return Fragment(Provide(ToastApi.Context, api, Child), new ToastLayer(api) { Max = Max, Position = Position });
        }
    }

    internal sealed record ToastLayer(ToastApi Api) : UIComponent
    {
        public int Max { get; init; } = 3;
        public ToastPosition Position { get; init; }

        protected internal override Node? Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var items = UseState(Array.Empty<(ToastEntry Entry, float Shown)>());
            int max = Max;
            UseEffect(() =>
            {
                Api.Attach(
                    e => items.Set(l => l.Append((e, backend.RealTime)).TakeLast(Math.Max(1, max)).ToArray()),
                    id => items.Set(l => l.Where(x => x.Entry.Id != id).ToArray()));
                return () => Api.Attach(null, null);
            }, Api, max);

            var list = items.Value;
            UseInterval(() =>
            {
                float now = backend.RealTime;
                bool Expired((ToastEntry Entry, float Shown) x) => x.Entry.Options.Duration > 0f && now >= x.Shown + x.Entry.Options.Duration;
                if (list.Any(Expired)) items.Set(l => l.Where(x => !Expired(x)).ToArray());
            }, list.Length > 0 ? 0.25f : 0f);

            if (list.Length == 0) return null;
            var dismiss = Api.Dismiss;
            bool top = Position is ToastPosition.TopRight or ToastPosition.TopCenter or ToastPosition.TopLeft;
            var ordered = top ? list.Reverse() : list;
            var align = new Vector2(
                Position is ToastPosition.BottomLeft or ToastPosition.TopLeft ? 0f : Position is ToastPosition.BottomCenter or ToastPosition.TopCenter ? 0.5f : 1f,
                top ? 0f : 1f);
            // Placed at its own size so the rest of the host keeps taking clicks
            return Overlay(
                Column(ordered.Select(x => new ToastView(x.Entry, dismiss) { FromTop = top }.Key("toast" + x.Entry.Id)))
                    .Width(320).Gap(t.Space(2)).Margin(t.Space(4)),
                Placement.Fill) with
            { FillAlign = align };
        }
    }

    internal sealed record ToastView(ToastEntry Entry, Action<int> Dismiss) : UIComponent
    {
        public bool FromTop { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            float slide = UseAnimated(0f, 0.2f, from: 24f);
            var o = Entry.Options;
            int id = Entry.Id;
            var dismiss = Dismiss;
            Color? accent = o.Variant switch
            {
                ToastVariant.Success => t.Success,
                ToastVariant.Info => t.Info,
                ToastVariant.Warning => t.Warning,
                ToastVariant.Destructive => t.Destructive,
                _ => null,
            };
            return Row(
                    accent is { } a ? Row().Width(3).Bg(a) : null,
                    Column(
                            Text(Entry.Title).Bold().Wrap().Color(t.Foreground),
                            o.Description is { } d ? Text(d).Wrap().Color(t.SecondaryForeground) : null)
                        .Grow().Shrink().Gap(t.Space(0.5f)).Justify(Justify.Center),
                    o.ActionLabel is { } label
                        ? new Button(label, () =>
                        {
                            o.OnAction?.Invoke();
                            dismiss(id);
                        })
                        { Variant = ButtonVariant.Outline, Size = ButtonSize.Sm }.AlignSelf(Align.Center)
                        : null,
                    IconButton(BuiltinIcon.Close, () => dismiss(id)).Size(20, 20).AlignSelf(Align.Start).Key("close"))
                .Padding(t.Space(3)).Gap(t.Space(2.5f))
                .Bg(t.SurfaceRaised).Radius(t.RadiusLg).Bevel(t.Raised(t.SurfaceRaised, t.BevelWidthSm)).Border(t.Outline)
                .Shadow(t.ShadowLg, t.ShadowColor)
                .Offset(FromTop ? 0 : slide, FromTop ? -slide : 0)
                .OnMouseDown(_ => true); // Keep clicks from reaching what's under the toast
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Toaster Toaster(Node child) => new(child);
    }

    internal static partial class Hooks
    {
        /// <summary>Toast API of the nearest Toaster. Throws if there is none.</summary>
        /// <example><code>var toast = UseToast();
        /// Button("Save", () => { Save(); toast.Success("Saved"); })</code></example>
        public static Components.ToastApi UseToast() =>
            UseContext(Components.ToastApi.Context) ??
            throw new System.InvalidOperationException("Rimact: UseToast needs a Toaster above this component; wrap the root in Toaster(...).");
    }
}
