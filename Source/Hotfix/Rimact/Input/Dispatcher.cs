using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

// Hit tests use the last layout pass. Overlays go first, then the main tree, and what painted last is on top.
// Events bubble up from the target until handled
internal sealed class Dispatcher
{
    private readonly Host _host;
    private int _captureButton;
    // Where the capture last got a down or drag
    private Vector2 _dragAt;
    private Vector2 _pointer = new(float.NaN, float.NaN);
    private HashSet<Fiber> _hover = new();
    private List<Fiber> _hoverOrder = new();
    private List<Fiber> _hoverNextOrder = new();
    private HashSet<Fiber> _hoverNext = new();
    private readonly HashSet<Fiber> _entered = new();

    public Dispatcher(Host host) => _host = host;

    public Fiber? Capture { get; private set; }

    public void Reset()
    {
        Capture = null;
        _hover.Clear();
        _hoverOrder.Clear();
    }

    public void OnUnmount(Fiber f)
    {
        if (Capture == f) Capture = null;
        if (_hover.Remove(f)) _hoverOrder.Remove(f);
    }

    public InteractionState StateOf(Fiber f)
    {
        if (f.DisabledTree) return InteractionState.Disabled;
        var s = InteractionState.None;
        if (_hover.Contains(f)) s |= InteractionState.Hovered;
        if (Capture == f && Visible(f).Contains(_pointer)) s |= InteractionState.Pressed;
        return s;
    }

    // On the hit path under the pointer: not clipped away or covered by an overlay. Disabled fibers count
    public bool IsHovered(Fiber f) => _hover.Contains(f);

    private static Rect Visible(Fiber f) => LayoutEngine.Intersect(f.Rect, f.ClipRect);

    #region Hit testing

    public bool HitOverlay(Vector2 p)
    {
        foreach (var ov in _host.Overlays)
            if (ov.Rect.Contains(p)) return true;
        return false;
    }

    public Fiber? HitTest(Vector2 p)
    {
        if (!_host.Rect.Contains(p)) return null;
        var ovs = _host.Overlays;
        for (int i = ovs.Count - 1; i >= 0; i--)
        {
            var hit = HitChildren(ovs[i], p);
            if (hit != null) return hit;
        }
        return _host.Root is { } root ? Hit(root, p) : null;
    }

    private static Fiber? HitChildren(Fiber f, Vector2 p)
    {
        for (int i = f.Children.Count - 1; i >= 0; i--)
        {
            var hit = Hit(f.Children[i], p);
            if (hit != null) return hit;
        }
        return null;
    }

    private static Fiber? Hit(Fiber f, Vector2 p)
    {
        if (f.Node is OverlayNode) return null;
        if (f.IsTransparent) return HitChildren(f, p);
        if (!f.ClipRect.Contains(p)) return null;
        var child = HitChildren(f, p);
        if (child != null) return child;
        return f.Rect.Contains(p) ? f : null;
    }

    #endregion

    #region Hover

    // Returns true if any OnHover handler ran
    public bool UpdateHover(Vector2 p)
    {
        _pointer = p;
        var target = HitTest(p);
        var next = _hoverNext;
        var order = _hoverNextOrder;
        next.Clear();
        order.Clear();
        for (var f = target; f != null; f = f.Parent)
        {
            if (f.IsTransparent) continue;
            if (next.Add(f)) order.Add(f); // deepest first
        }
        bool fired = false;
        foreach (var f in _hoverOrder)
            if (!next.Contains(f) && _entered.Remove(f) && !f.Unmounted && f.Node.Events?.OnHover is { } leave)
            {
                leave(false); // only fibers that got an enter get a leave
                fired = true;
            }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var f = order[i];
            if (!_hover.Contains(f) && f.Node.Events?.OnHover is { } enter && !f.DisabledTree)
            {
                _entered.Add(f);
                enter(true);
                fired = true;
            }
        }
        (_hover, _hoverNext) = (next, _hover);
        (_hoverOrder, _hoverNextOrder) = (order, _hoverOrder);
        foreach (var f in _hoverOrder)
            if (!f.DisabledTree && f.Node.Events?.OnPointerMove is { } move)
                move(new PointerEvent(p, p - f.Rect.position, 0, default, 0, false, false, false));
        return fired;
    }

    #endregion

    #region Dispatch

    public bool Dispatch(InputEvent e)
    {
        _pointer = e.Position;
        switch (e.Type)
        {
            case InputType.MouseDown: return MouseDown(e);
            case InputType.MouseDrag: return MouseDrag(e);
            case InputType.MouseUp: return MouseUp(e);
            case InputType.Wheel: return Wheel(e);
            default: return false;
        }
    }

    private static PointerEvent Pointer(InputEvent e, Fiber f) =>
        new(e.Position, e.Position - f.Rect.position, e.Button, e.Delta, e.ClickCount, e.Shift, e.Ctrl, e.Alt);

    private bool MouseDown(InputEvent e)
    {
        // A press outside an overlay and its anchor asks it to close. The event is not used up
        var ovs = _host.Overlays;
        for (int i = ovs.Count - 1; i >= 0; i--)
        {
            var ov = ovs[i];
            if (ov.Unmounted) continue;
            var node = (OverlayNode)ov.Node;
            if (node.OnPointerDownOutside != null && !ov.Rect.Contains(e.Position) && !ov.AnchorRect.Contains(e.Position))
                node.OnPointerDownOutside();
        }

        var target = HitTest(e.Position);
        Fiber? focus = null;
        for (var f = target; f != null && focus == null; f = f.Parent)
            if (!f.IsTransparent && !f.DisabledTree && f.Node.Events?.IsFocusable == true) focus = f;
        _host.Focus(focus, false);
        for (var f = target; f != null; f = f.Parent)
        {
            if (f.IsTransparent) continue;
            if (f.DisabledTree) return true; // swallow clicks on disabled areas so they don't leak through
            var ev = f.Node.Events;
            if (ev is null || !ev.HandlesPointerDown) continue;
            bool handled = ev.OnMouseDown?.Invoke(Pointer(e, f)) ?? false;
            if (e.Button == 0 && (ev.OnClick != null || ev.OnDrag != null)) handled = true;
            if (!handled) continue;
            Capture = f;
            _captureButton = e.Button;
            _dragAt = e.Position;
            return true;
        }
        return false;
    }

    private bool MouseDrag(InputEvent e)
    {
        var c = Capture;
        if (c is null || c.Unmounted) return false;
        _dragAt = e.Position;
        c.Node.Events?.OnDrag?.Invoke(Pointer(e, c));
        return true;
    }

    // 1.7 sends mouse events only to the window under the pointer, but repaints every window.
    // So on repaint, follow a drag that left the window and end one released over another window.
    // Returns true if a handler ran
    public bool TrackCapture(Vector2 p)
    {
        var c = Capture;
        if (c is null || c.Unmounted) return false;
        var ev = c.Node.Events;
        if (!_host.Backend.IsMouseHeld(_captureButton))
        {
            // Released elsewhere, so no click
            Capture = null;
            if (ev?.OnMouseUp is not { } up) return false;
            up(new PointerEvent(p, p - c.Rect.position, _captureButton, default, 0, false, false, false));
            return true;
        }
        if (p == _dragAt) return false;
        var delta = p - _dragAt;
        _dragAt = p;
        if (ev?.OnDrag is not { } drag) return false;
        drag(new PointerEvent(p, p - c.Rect.position, _captureButton, delta, 0, false, false, false));
        return true;
    }

    private bool MouseUp(InputEvent e)
    {
        var c = Capture;
        if (c != null && !c.Unmounted)
        {
            Capture = null;
            var ev = c.Node.Events;
            ev?.OnMouseUp?.Invoke(Pointer(e, c));
            if (e.Button == _captureButton && e.Button == 0 && ev?.OnClick != null && Visible(c).Contains(e.Position))
                ev.OnClick();
            return true;
        }
        Capture = null;
        for (var f = HitTest(e.Position); f != null; f = f.Parent)
        {
            if (f.IsTransparent) continue;
            if (f.DisabledTree) return false;
            if (f.Node.Events?.OnMouseUp is { } up && up(Pointer(e, f))) return true;
        }
        return false;
    }

    private bool Wheel(InputEvent e)
    {
        var we = new WheelEvent(e.Position, e.Delta, e.Shift, e.Ctrl);
        for (var f = HitTest(e.Position); f != null; f = f.Parent)
        {
            if (f.IsTransparent) continue;
            if (f.DisabledTree) return false;
            if (f.Node.Events?.OnWheel is { } wheel && wheel(we)) return true;
        }
        return false;
    }

    // Runs OnEscape of the topmost overlay that has one
    public bool CloseTopOverlay()
    {
        var ovs = _host.Overlays;
        for (int i = ovs.Count - 1; i >= 0; i--)
            if (!ovs[i].Unmounted && ((OverlayNode)ovs[i].Node).OnEscape is { } esc)
            {
                esc();
                return true;
            }
        return false;
    }

    // Tab moves focus. Other keys bubble up OnKeyDown from the focused node, then Space or Enter clicks it
    private bool FocusKey(InputEvent e, KeyEvent ke)
    {
        if (e.Key == KeyCode.Tab && !e.Ctrl && !e.Alt) return _host.MoveFocus(e.Shift);
        var focused = _host.Focused;
        if (focused is null || focused.Unmounted || focused.DisabledTree) return false;
        for (var f = focused; f != null; f = f.Parent)
        {
            if (f.IsTransparent || f.Node.Events is not { } ev) continue;
            if (ev.OnKeyDown is { } key && key(ke)) return true;
            if (ev.FocusGroup is { } mode && ArrowDirection(e.Key) is { } backward && _host.MoveFocus(backward, scope: f))
            {
                if (mode == FocusGroupMode.Select) ActivateFocused(KeyCode.Space);
                return true;
            }
        }
        // Like Host.Accept, only a node focused by keyboard gets clicked
        return _host.FocusVisible && ActivateFocused(e.Key);
    }

    private static bool? ArrowDirection(KeyCode key) => key switch
    {
        KeyCode.LeftArrow or KeyCode.UpArrow => true,
        KeyCode.RightArrow or KeyCode.DownArrow => false,
        _ => null,
    };

    public bool ActivateFocused(KeyCode key)
    {
        var f = _host.Focused;
        if (key is not (KeyCode.Space or KeyCode.Return or KeyCode.KeypadEnter)) return false;
        if (f is null || f.Unmounted || f.DisabledTree || f.Node.Events?.OnClick is not { } click) return false;
        click();
        return true;
    }

    // UseKeyDown handlers of one kind, newest first. Only the regular pass closes overlays on Esc and moves focus
    public bool DispatchKey(InputEvent e, bool beforeTextFields = false)
    {
        var ke = new KeyEvent(e.Key, e.Character, e.Shift, e.Ctrl, e.Alt);
        if (!beforeTextFields)
        {
            if (e.Key == KeyCode.Escape && CloseTopOverlay()) return true;
            if (FocusKey(e, ke)) return true;
        }
        var handlers = _host.KeyHandlers;
        for (int i = handlers.Count - 1; i >= 0; i--)
        {
            var h = handlers[i];
            if (h.Fiber.Unmounted || h.BeforeTextFields != beforeTextFields) continue;
            if (h.Handler(ke)) return true;
        }
        return false;
    }

    #endregion
}
