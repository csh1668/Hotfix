using System;
using UnityEngine;

namespace Rimact;

/// <summary>Arrow keys in a focus group: Move only moves focus, Select also clicks the new node.</summary>
internal enum FocusGroupMode : byte { Move, Select }

internal readonly record struct PointerEvent(Vector2 Position, Vector2 Local, int Button, Vector2 Delta, int ClickCount, bool Shift, bool Ctrl, bool Alt);

internal readonly record struct WheelEvent(Vector2 Position, Vector2 Delta, bool Shift, bool Ctrl);

internal readonly record struct KeyEvent(KeyCode Key, char Character, bool Shift, bool Ctrl, bool Alt);

/// <summary>A node's event handlers. A bool handler returns true when it handled the event, which stops bubbling.</summary>
internal sealed record EventProps
{
    public Action? OnClick { get; init; }
    public Func<PointerEvent, bool>? OnMouseDown { get; init; }
    public Func<PointerEvent, bool>? OnMouseUp { get; init; }
    /// <summary>Drags while this node has the mouse capture, which it gets by handling the mouse down.</summary>
    public Action<PointerEvent>? OnDrag { get; init; }
    public Func<WheelEvent, bool>? OnWheel { get; init; }
    public Action<bool>? OnHover { get; init; }
    /// <summary>Runs on every repaint while the pointer is over the node, before it paints.</summary>
    /// <remarks>For a crosshair, keep the position in a ref and read it in a canvas. Nothing re-renders.</remarks>
    public Action<PointerEvent>? OnPointerMove { get; init; }
    /// <summary>Keys while this node or one inside it has keyboard focus.</summary>
    public Func<KeyEvent, bool>? OnKeyDown { get; init; }
    /// <summary>Whether Tab stops here. null means only when it has OnClick.</summary>
    public bool? Focusable { get; init; }
    /// <summary>true when the node gains keyboard focus, false when it loses it.</summary>
    public Action<bool>? OnFocus { get; init; }
    /// <summary>Arrow keys move focus among the focusable nodes inside, wrapping. Tab treats them as one stop.</summary>
    public FocusGroupMode? FocusGroup { get; init; }
    /// <summary>Where Tab lands when entering the focus group, e.g. the checked radio button.</summary>
    public bool? FocusEntry { get; init; }
    /// <summary>Takes keyboard focus, with the focus ring, on mount.</summary>
    public bool? AutoFocus { get; init; }
    /// <summary>Gets a rect in host coordinates to scroll into view when keyboard focus moves inside.</summary>
    public Action<Rect>? OnReveal { get; init; }

    public bool HandlesPointerDown => OnClick != null || OnMouseDown != null || OnDrag != null;
    public bool IsFocusable => Focusable ?? OnClick != null;

    public EventProps Merge(EventProps? over) => over is null ? this : new EventProps
    {
        OnClick = over.OnClick ?? OnClick,
        OnMouseDown = over.OnMouseDown ?? OnMouseDown,
        OnMouseUp = over.OnMouseUp ?? OnMouseUp,
        OnDrag = over.OnDrag ?? OnDrag,
        OnWheel = over.OnWheel ?? OnWheel,
        OnHover = over.OnHover ?? OnHover,
        OnPointerMove = over.OnPointerMove ?? OnPointerMove,
        OnKeyDown = over.OnKeyDown ?? OnKeyDown,
        Focusable = over.Focusable ?? Focusable,
        OnFocus = over.OnFocus ?? OnFocus,
        FocusGroup = over.FocusGroup ?? FocusGroup,
        FocusEntry = over.FocusEntry ?? FocusEntry,
        OnReveal = over.OnReveal ?? OnReveal,
        AutoFocus = over.AutoFocus ?? AutoFocus,
    };
}
