using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Right-click menu on Child: a FloatMenu from Items, or a Rimact <see cref="Menu"/> from Entries.</summary>
    /// <remarks>Items is called on each open. The child's own mouse-down handler keeps working.</remarks>
    internal sealed record ContextMenu(Node Child, Func<IEnumerable<MenuItem>>? Items) : UIComponent
    {
        public IReadOnlyList<MenuEntry>? Entries { get; init; }

        protected internal override Node Build()
        {
            var backend = UseHost().Backend;
            var at = UseState((Vector2?)null);
            var items = Items;
            var entries = Entries;
            var child = Child.Modify(n =>
            {
                var inner = n.Events?.OnMouseDown;
                return n with
                {
                    Events = (n.Events ?? new EventProps()) with
                    {
                        OnMouseDown = e =>
                        {
                            if (e.Button != 1) return inner?.Invoke(e) ?? false;
                            if (entries != null)
                            {
                                backend.PlaySound(UiSound.MenuOpen);
                                at.Set(e.Local);
                            }
                            else if (items != null) backend.OpenMenu(items().ToList());
                            return true;
                        },
                    },
                };
            });
            if (entries is null) return child;
            void Close() => at.Set((Vector2?)null);
            // 1px anchor at the pointer so the panel flips like any overlay
            return Stack(child, at.Value is { } p
                ? Row(Overlay(new MenuPanel(entries, Close), Placement.Below, onPointerDownOutside: Close, onEscape: Close) with { SideOffset = 0f })
                    .Size(1, 1).AlignSelf(Align.Start).Offset(p.x, p.y)
                : null);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        /// <summary>Opens a vanilla FloatMenu.</summary>
        public static Components.ContextMenu ContextMenu(Node child, Func<IEnumerable<MenuItem>> items) => new(child, items);

        /// <summary>Opens a Rimact menu panel at the pointer.</summary>
        public static Components.ContextMenu ContextMenu(Node child, params Components.MenuEntry[] entries) => new(child, null) { Entries = entries };
    }
}
