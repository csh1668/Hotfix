using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rimact;

// Runs one UI root: render, effects, layout, paint and input
internal sealed class Host : IDisposable
{
    public Host(Node root, IBackend backend)
    {
        _rootNode = root;
        Backend = backend;
        Dispatcher = new Dispatcher(this);
    }

    public IBackend Backend { get; }
    internal Dispatcher Dispatcher { get; }
    public Fiber? Root { get; private set; }

    public Rect Rect { get; private set; }

    // Context comes from here too, e.g. a dialog's host inherits from the host that opened it
    public Fiber? ContextParent { get; set; }
    // UseHost().Close() calls this, usually to close the window
    public Action? CloseRequested { get; set; }
    public Exception? Error { get; private set; }
    public int MaxRenderPasses { get; set; } = 50;
    public bool ReduceMotion { get; set; }
    // null = follow RimactDiagnostics.Inspector
    public bool? Inspector { get; set; }
    internal bool InspectorOn => Inspector ?? RimactDiagnostics.Inspector;
    public HostStats Stats { get; } = new();

    internal readonly List<IExternalSync> Externals = new();
    internal readonly List<LayoutSlot> LayoutWatchers = new();
    internal readonly List<IntervalSlot> Intervals = new();
    internal readonly List<KeyDownSlot> KeyHandlers = new();
    internal readonly List<DialogKeySlot> AcceptHandlers = new();
    internal readonly List<DialogKeySlot> CancelHandlers = new();
    internal readonly List<Fiber> Natives = new();
    internal readonly List<Fiber> Overlays = new();
    private readonly HashSet<Fiber> _frameRequests = new();
    // If a render leaves this false, the old layout is kept
    private bool _layoutAffected;
    // Offset or scroll changes only. These are moved in place without a layout pass
    private readonly List<(Fiber Fiber, Vector2 Offset, Vector2 Scroll)> _translations = new();
    // UseLayout watchers still need a check after a move
    private bool _translated;
    private readonly List<IExternalSync> _externalScratch = new();
    private readonly List<IntervalSlot> _intervalScratch = new();

    private Node _rootNode;
    private bool _rootChanged;
    private bool _needsRender = true;
    private bool _layoutValid;
    private bool _disposed;
    private int _lastFrame = int.MinValue;
    private readonly List<EffectSlot> _layoutEffects = new();
    private readonly List<EffectSlot> _effects = new();
    private readonly HashSet<string> _loggedErrors = new();
    // Fibers created under an error boundary, so a failed pass can unmount the ones it never attached
    private readonly List<Fiber> _mountLog = new();
    private int _boundaryDepth;

    public int RenderPassCount { get; private set; }

    public void SetRoot(Node node)
    {
        _rootNode = node;
        _rootChanged = true;
        _needsRender = true;
    }

    #region Pipeline

    // Returns true if the event was used. The caller should call Event.Use() then
    public bool Process(Rect rect, InputEvent e)
    {
        if (_disposed) return false;
        if (Error != null) return ProcessError(rect, e);
        try
        {
            if (rect != Rect)
            {
                Rect = rect;
                _layoutValid = false;
            }
            // Every event, not once a frame. Otherwise an input bound to a static field flickers back for a frame
            SyncExternals();
            if (_reloadGeneration != HotReload.Generation) ReloadAll();
            int frame = Backend.FrameId;
            if (frame != _lastFrame)
            {
                _lastFrame = frame;
                RunIntervals();
                RunFrameRequests();
            }
            Settle();

            bool used = false;
            switch (e.Type)
            {
                case InputType.Repaint:
                    if (Dispatcher.TrackCapture(e.Position)) Settle();
                    if (Dispatcher.UpdateHover(e.Position)) Settle();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Painter.Paint(this);
                    HostStats.Average(ref Stats.PaintMs, Ms(t0));
                    if (InspectorOn) Rimact.Inspector.Paint(this, e.Position);
                    break;
                case InputType.KeyDown:
                    if (RimactDiagnostics.InspectorKey != KeyCode.None && e.Key == RimactDiagnostics.InspectorKey && e.Ctrl && e.Shift)
                    {
                        Inspector = !InspectorOn;
                        used = true;
                        break;
                    }
                    used = Dispatcher.DispatchKey(e, beforeTextFields: true) || NativePass(e);
                    if (!used && !e.TextFieldFocused) used = Dispatcher.DispatchKey(e);
                    break;
                default:
                    used = NativePass(e);
                    if (used && e.Type == InputType.MouseDown) Focus(null, false); // A native control took the click.
                    if (!used && e.IsMouse) used = Dispatcher.Dispatch(e);
                    break;
            }
            FlushEffects(_effects);
            return used;
        }
        catch (Exception ex)
        {
            Fail(ex);
            // Draw the error panel now instead of a blank frame
            return e.Type == InputType.Repaint && ProcessError(rect, e);
        }
    }

    // Render and layout can trigger each other. Give up after 4 rounds
    private void Settle()
    {
        for (int i = 0; i < 4; i++)
        {
            if (_needsRender)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                RenderPending();
                HostStats.Average(ref Stats.RenderMs, Ms(t0));
            }
            if (!_layoutValid)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                LayoutEngine.Run(this, Rect);
                HostStats.Average(ref Stats.LayoutMs, Ms(t0));
                Stats.LayoutPasses++;
                _layoutValid = true;
                _translated = false;
                CheckLayoutWatchers();
            }
            if (_autoFocus is { } focus && _layoutValid)
            {
                // After layout so scroll containers can reveal it
                _autoFocus = null;
                if (!focus.Unmounted) Focus(focus, true);
            }
            else if (_translated)
            {
                _translated = false;
                CheckLayoutWatchers();
            }
            if (!_needsRender) return;
        }
    }

    #endregion

    #region Window support

    private int _measuredPass = -1;
    private float _measuredWidth = float.NaN, _measuredHeight;

    // Cached until the next render or a different width
    public float ContentHeight(float width)
    {
        if (_disposed) return 0f;
        if (Error != null)
        {
            // Fit the error message and the Retry button
            return Mathf.Max(_measuredHeight, ErrorRetryRect(new Rect(0, 0, width, 0), out _, out _).yMax + 8);
        }
        try
        {
            SyncExternals();
            if (_needsRender) RenderPending();
            if (RenderPassCount != _measuredPass || width != _measuredWidth)
            {
                _measuredPass = RenderPassCount;
                _measuredWidth = width;
                _measuredHeight = LayoutEngine.NaturalHeight(this, width);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        return _measuredHeight;
    }

    // Enter: the focused node if focus came from the keyboard, else the newest UseAcceptKey handler
    public bool Accept()
    {
        if (_disposed || Error != null) return false;
        if (FocusVisible)
        {
            try
            {
                if (Dispatcher.ActivateFocused(KeyCode.Return)) return true;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return true;
            }
        }
        return RunDialogKey(AcceptHandlers);
    }

    // Esc: close the top overlay, else the newest UseCancelKey handler, else close the window
    public bool Cancel()
    {
        if (_disposed) return false;
        if (Error == null)
        {
            try
            {
                if (Dispatcher.CloseTopOverlay()) return true;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return true;
            }
            if (RunDialogKey(CancelHandlers)) return true;
        }
        if (CloseRequested is not { } close) return false;
        close();
        return true;
    }

    private bool RunDialogKey(List<DialogKeySlot> handlers)
    {
        if (_disposed || Error != null) return false;
        for (int i = handlers.Count - 1; i >= 0; i--)
        {
            var s = handlers[i];
            if (s.Handler is not { } run || s.Fiber.Unmounted || IsDisabled(s.Fiber)) continue;
            try
            {
                run();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            return true;
        }
        return false;
    }

    // .Disabled() on a component ends up on the first host node it renders, so check that one
    private static bool IsDisabled(Fiber f)
    {
        for (var c = f; c != null; c = c.Children.Count > 0 ? c.Children[0] : null)
            if (!c.IsTransparent) return c.DisabledTree;
        for (var p = f; p != null; p = p.Parent)
            if (p.Node.Disabled) return true;
        return false;
    }

    private static double Ms(long start) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private void SyncExternals()
    {
        if (Externals.Count == 0) return;
        // Sync can add or remove subscriptions, so loop over a copy
        _externalScratch.Clear();
        _externalScratch.AddRange(Externals);
        foreach (var x in _externalScratch)
        {
            try
            {
                x.Sync();
            }
            catch (Exception ex) when (x is HookSlot slot && CatchOutsideRender(slot.Fiber, ex))
            {
            }
        }
        _externalScratch.Clear();
    }

    private void RunIntervals()
    {
        if (Intervals.Count == 0) return;
        float t = Backend.RealTime;
        _intervalScratch.Clear();
        _intervalScratch.AddRange(Intervals);
        foreach (var s in _intervalScratch)
        {
            if (s.Fiber.Unmounted || s.Seconds <= 0) continue;
            if (float.IsNaN(s.Next)) s.Next = t + s.Seconds;
            else if (t >= s.Next)
            {
                s.Next = t + s.Seconds;
                try
                {
                    s.Callback();
                }
                catch (Exception ex) when (CatchOutsideRender(s.Fiber, ex))
                {
                }
            }
        }
    }

    // For animation hooks
    internal void RequestFrame(Fiber f) => _frameRequests.Add(f);

    private void RunFrameRequests()
    {
        if (_frameRequests.Count == 0) return;
        foreach (var f in _frameRequests) MarkDirty(f);
        _frameRequests.Clear();
    }

    private void CheckLayoutWatchers()
    {
        foreach (var s in LayoutWatchers)
        {
            var info = new LayoutInfo(s.Fiber.Rect, s.Fiber.ContentSize);
            if (info == s.Last) continue;
            s.Last = info;
            MarkDirty(s.Fiber);
        }
    }

    private bool NativePass(InputEvent e)
    {
        if (Natives.Count == 0) return false;
        bool overOverlay = e.IsMouse && Dispatcher.HitOverlay(e.Position);
        bool used = false;
        foreach (var f in Natives)
        {
            bool enabled = !f.DisabledTree && !(overOverlay && !f.InOverlay);
            // Call every leaf even after one uses the event, or IMGUI control IDs shift
            if (Backend.Native(f.Rect, f.ClipRect, ((NativeNode)f.Node).Draw, enabled, OwnerName(f))) used = true;
        }
        return used;
    }

    internal static string? OwnerName(Fiber f)
    {
        if (!RimactDiagnostics.CheckGuiState) return null;
        for (var p = f.Parent; p != null; p = p.Parent)
            if (p.IsComponent) return Rimact.Inspector.Name(p);
        return null;
    }

    #endregion

    #region Errors

    private void Fail(Exception ex)
    {
        Error = ex;
        if (_loggedErrors.Add(ex.GetType().FullName + ": " + ex.Message))
            Backend.LogError("[Rimact] UI error, showing an error panel instead.\n" + ex);
        Hooks.Current = null;
        var root = Root;
        Root = null;
        _layoutEffects.Clear();
        _effects.Clear();
        Natives.Clear();
        Overlays.Clear();
        Dispatcher.Reset();
        if (root != null)
        {
            try { Unmount(root); }
            catch (Exception cleanupEx) { Backend.LogError("[Rimact] Exception during cleanup after an error: " + cleanupEx); }
        }
    }

    private Rect ErrorRetryRect(Rect rect, out Rect textRect, out string message)
    {
        message = $"{Strings.Default.ErrorTitle}: {Error!.GetType().Name}: {Error.Message}";
        float w = Mathf.Max(0, rect.width - 16);
        float h = Backend.MeasureText(message, FontSize.Small, w).y;
        textRect = new Rect(rect.x + 8, rect.y + 8, w, h);
        return new Rect(rect.x + 8, textRect.yMax + 8, 120, 30);
    }

    private bool ProcessError(Rect rect, InputEvent e)
    {
        var retry = ErrorRetryRect(rect, out var textRect, out var message);
        if (e.Type == InputType.Repaint)
        {
            Backend.PushClip(rect);
            Backend.FillRect(rect, new Color(0.35f, 0.05f, 0.05f, 0.6f));
            Backend.DrawText(textRect, message, new TextStyle(FontSize.Small, TextAlign.UpperLeft, true, false), new Color(1f, 0.6f, 0.6f));
            var state = retry.Contains(e.Position) ? InteractionState.Hovered : InteractionState.None;
            Backend.DrawVanilla(retry, VanillaPart.Button, state);
            Backend.DrawText(retry, Strings.Default.Retry, new TextStyle(FontSize.Small, TextAlign.MiddleCenter, false, false), Color.white);
            Backend.PopClip();
            return false;
        }
        if (e.Type == InputType.MouseDown && e.Button == 0 && retry.Contains(e.Position))
        {
            Error = null;
            _needsRender = true;
            _layoutValid = false;
            return true;
        }
        return false;
    }

    #endregion

    #region Reconciliation

    private int _reloadGeneration = HotReload.Generation;

    // After a hot reload. Re-renders everything, ignoring memo and same-node checks
    private void ReloadAll()
    {
        _reloadGeneration = HotReload.Generation;
        void Walk(Fiber f)
        {
            if (f.IsComponent) MarkDirty(f);
            foreach (var c in f.Children) Walk(c);
        }
        if (Root != null) Walk(Root);
        _layoutValid = false;
    }

    internal void MarkDirty(Fiber f)
    {
        if (f.Unmounted) return;
        f.Dirty = true;
        for (var p = f.Parent; p != null && !p.ChildDirty; p = p.Parent) p.ChildDirty = true;
        _needsRender = true;
    }

    internal void RenderPending()
    {
        int passes = 0;
        while (_needsRender)
        {
            if (++passes > MaxRenderPasses)
                throw new InvalidOperationException(
                    $"Rimact: rendering didn't settle after {MaxRenderPasses} passes. Check for state set unconditionally during render or in an effect.");
            _needsRender = false;
            RenderPassCount++;
            _mountLog.Clear();
            try
            {
                if (Root is null || _rootChanged)
                {
                    _rootChanged = false;
                    Root = Reconcile(Root, _rootNode, null);
                }
                else
                {
                    Visit(Root);
                }
            }
            finally
            {
                Hooks.Current = null;
            }
            if (_layoutAffected)
            {
                _layoutAffected = false;
                _layoutValid = false;
            }
            else if (_layoutValid)
            {
                Stats.SkippedLayouts++;
                ApplyTranslations();
            }
            _translations.Clear();
            FlushEffects(_layoutEffects);
        }
    }

    internal enum LayoutChange : byte { None, Translate, Full }

    // Offset and scroll values only move boxes, so they can skip the layout pass.
    // Colors, handlers and paint callbacks don't affect layout at all
    internal static LayoutChange LayoutChangeOf(Node prev, Node next)
    {
        if (prev.GetType() != next.GetType() || prev.Disabled != next.Disabled) return LayoutChange.Full;
        var change = LayoutChange.None;
        if (prev.Layout != next.Layout)
        {
            var a = prev.Layout;
            var b = next.Layout;
            if (a.ScrollY.HasValue != b.ScrollY.HasValue || a.ScrollX.HasValue != b.ScrollX.HasValue ||
                (a with { Offset = b.Offset, ScrollY = b.ScrollY, ScrollX = b.ScrollX }) != b) return LayoutChange.Full;
            change = LayoutChange.Translate;
        }
        bool full = next switch
        {
            TextNode t => prev is TextNode p && (t.Text != p.Text || t.Font != p.Font || t.Size != p.Size || t.Bold != p.Bold || t.Wrap != p.Wrap || t.Truncate != p.Truncate),
            BoxNode b => prev is BoxNode pb && b.Axis != pb.Axis,
            OverlayNode o => prev is OverlayNode po && (o.Placement != po.Placement || o.SideOffset != po.SideOffset || o.FillAlign != po.FillAlign),
            _ => false,
        };
        return full ? LayoutChange.Full : change;
    }

    private void TrackLayoutChange(Fiber f, Node prev, Node next)
    {
        switch (LayoutChangeOf(prev, next))
        {
            case LayoutChange.Full:
                _layoutAffected = true;
                return;
            case LayoutChange.Translate:
                var offset = next.Layout.Offset - prev.Layout.Offset;
                // ScrollY only applies to columns and ScrollX only to rows
                var scroll = next switch
                {
                    BoxNode { Axis: Axis.Column } when next.Layout.ScrollY is { } y => new Vector2(0f, y - prev.Layout.ScrollY!.Value),
                    BoxNode { Axis: Axis.Row } when next.Layout.ScrollX is { } x => new Vector2(x - prev.Layout.ScrollX!.Value, 0f),
                    _ => Vector2.zero,
                };
                if (offset != Vector2.zero || scroll != Vector2.zero) _translations.Add((f, offset, scroll));
                return;
        }
    }

    // Falls back to a full layout if a subtree can't be moved
    private void ApplyTranslations()
    {
        foreach (var (f, offset, scroll) in _translations)
        {
            if (f.Unmounted) continue;
            bool ok = (offset == Vector2.zero || LayoutEngine.Translate(f, offset)) &&
                      (scroll == Vector2.zero || LayoutEngine.TranslateChildren(f, -scroll));
            if (!ok)
            {
                _layoutValid = false;
                return;
            }
            _translated = true;
        }
    }

    private void Visit(Fiber f)
    {
        if (f.Unmounted) return;
        if (f.Dirty)
        {
            Update(f, f.Node, mount: false);
            return;
        }
        if (!f.ChildDirty) return;
        f.ChildDirty = false;
        Descend(f, null);
    }

    private static object IdOf(Node node) => node switch
    {
        ProviderNode p => p.Context,
        _ => node.GetType(),
    };

    private Fiber Reconcile(Fiber? old, Node node, Fiber? parent)
    {
        var id = IdOf(node);
        if (old != null && old.Id.Equals(id) && old.Key == node.Key)
        {
            Update(old, node, mount: false);
            return old;
        }
        if (old != null) Unmount(old);
        _layoutAffected = true;
        var f = new Fiber(this, id, node.Key, node, parent);
        if (_boundaryDepth > 0) _mountLog.Add(f);
        Update(f, node, mount: true);
        return f;
    }

    private void Update(Fiber f, Node node, bool mount)
    {
        var prev = f.Node;
        // Same node instance (e.g. from UseMemo) means the same subtree. Only visit dirty fibers below
        if (!mount && !f.Dirty && ReferenceEquals(prev, node))
        {
            if (f.ChildDirty)
            {
                f.ChildDirty = false;
                Descend(f, null);
            }
            return;
        }
        f.Node = node;
        if (!mount && !_layoutAffected && node is not UIComponent) TrackLayoutChange(f, prev, node);
        if (mount && node is not UIComponent && node.Events?.AutoFocus == true) _autoFocus = f;
        switch (node)
        {
            case UIComponent c:
                if (!mount && !f.Dirty && c.IsMemo && PropEquality.Equal(prev, node))
                {
                    if (f.ChildDirty)
                    {
                        f.ChildDirty = false;
                        Descend(f, null);
                    }
                    return;
                }
                RenderComponent(f, c);
                return;
            case ProviderNode p:
                if (!mount && prev is ProviderNode pp && !Context<object>.ValueEquals(pp.Value, p.Value) && f.Consumers != null)
                    foreach (var consumer in f.Consumers.ToArray()) MarkConsumer(f, consumer);
                Clear(f);
                ReconcileChildren(f, new[] { p.Child });
                return;
            case BoxNode b:
                Clear(f);
                ReconcileChildren(f, b.Children);
                return;
            case FragmentNode fr:
                Clear(f);
                ReconcileChildren(f, fr.Children);
                return;
            case OverlayNode o:
                Clear(f);
                ReconcileChildren(f, new[] { o.Child });
                return;
            default:
                Clear(f);
                ReconcileChildren(f, Array.Empty<Node>());
                return;
        }

        static void Clear(Fiber f)
        {
            f.Dirty = false;
            f.ChildDirty = false;
        }
    }

    // Only marks up to the provider, which is being reconciled right now
    private void MarkConsumer(Fiber provider, Fiber consumer)
    {
        if (consumer.Unmounted) return;
        if (consumer.Host != this)
        {
            consumer.Host.MarkDirty(consumer);
            return;
        }
        consumer.Dirty = true;
        for (var p = consumer.Parent; p != null && p != provider && !p.ChildDirty; p = p.Parent) p.ChildDirty = true;
    }

    private void RenderComponent(Fiber f, UIComponent c)
    {
        f.RenderCount++;
        f.LastRender = Backend.RealTime;
        Stats.ComponentRenders++;
        f.Dirty = false;
        f.ChildDirty = false;
        var prevCurrent = Hooks.Current;
        var prevIndex = Hooks.Index;
        Hooks.Current = f;
        Hooks.Index = 0;
        Node? output;
        try
        {
            output = c.Build();
            int count = f.Hooks?.Count ?? 0;
            if (f.HooksSealed && Hooks.Index < count)
                throw new HookOrderException(
                    $"Rimact: hook order violation in {f}: called {Hooks.Index} hooks, last render called {count}. Check for hooks after an early return.");
            f.HooksSealed = true;
        }
        finally
        {
            Hooks.Current = prevCurrent;
            Hooks.Index = prevIndex;
        }
        Node child = output ?? FragmentNode.Empty;
        if (c.Decorators != null)
            foreach (var d in c.Decorators) child = child.Modify(d);
        Descend(f, new[] { child });
    }

    // nodes == null visits only the dirty children. Error boundaries catch here
    private void Descend(Fiber f, Node[]? nodes)
    {
        if (f.Node is not UIComponent { CatchesErrors: true } || f.CaughtError != null)
        {
            DescendUnguarded(f, nodes);
            return;
        }
        int start = _mountLog.Count;
        Exception? caught = null;
        _boundaryDepth++;
        try
        {
            DescendUnguarded(f, nodes);
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        finally
        {
            _boundaryDepth--;
        }
        if (caught != null)
        {
            // Includes fibers the failed pass created but never attached
            for (int i = start; i < _mountLog.Count; i++) Unmount(_mountLog[i]);
            _mountLog.RemoveRange(start, _mountLog.Count - start);
            ShowFallback(f, caught);
            RenderComponent(f, (UIComponent)f.Node); // fallback errors go to the next boundary up
        }
        if (_boundaryDepth == 0) _mountLog.Clear();
    }

    private void DescendUnguarded(Fiber f, Node[]? nodes)
    {
        if (nodes != null) ReconcileChildren(f, nodes);
        else foreach (var ch in f.Children.ToArray()) Visit(ch);
    }

    private void ShowFallback(Fiber boundary, Exception error)
    {
        if (_loggedErrors.Add(error.GetType().FullName + ": " + error.Message))
            Backend.LogError("[Rimact] UI error caught by an ErrorBoundary; its subtree is replaced by the fallback.\n" + error);
        foreach (var c in boundary.Children) Unmount(c);
        boundary.Children = new List<Fiber>();
        boundary.CaughtError = error;
        _layoutAffected = true;
        _layoutValid = false;
    }

    private static Fiber? FindBoundary(Fiber f)
    {
        for (var p = f.Parent; p != null; p = p.Parent)
            if (p.Node is UIComponent { CatchesErrors: true } && p.CaughtError == null && !p.Unmounted) return p;
        return null;
    }

    // For effects, external readers and intervals. False means no boundary took it
    private bool CatchOutsideRender(Fiber source, Exception error)
    {
        if (FindBoundary(source) is not { } boundary) return false;
        ShowFallback(boundary, error);
        MarkDirty(boundary);
        return true;
    }

    private void ReconcileChildren(Fiber f, Node[] nodes)
    {
        var old = f.Children;
        if (old.Count == 0 && nodes.Length == 0) return;

        Dictionary<string, Fiber>? keyed = null;
        List<Fiber>? unkeyed = null;
        foreach (var o in old)
        {
            if (o.Key != null) (keyed ??= new Dictionary<string, Fiber>())[o.Key] = o;
            else (unkeyed ??= new List<Fiber>()).Add(o);
        }

        var result = new List<Fiber>(nodes.Length);
        HashSet<Fiber>? reused = null;
        HashSet<string>? seen = null;
        int ui = 0;
        foreach (var node in nodes)
        {
            Fiber? match = null;
            if (node.Key != null)
            {
                if (!(seen ??= new HashSet<string>()).Add(node.Key)) WarnDuplicateKey(f, node.Key);
                if (keyed != null && keyed.Remove(node.Key, out var k)) match = k;
            }
            else if (unkeyed != null && ui < unkeyed.Count)
            {
                match = unkeyed[ui++];
            }
            var nf = Reconcile(match, node, f);
            if (match != null && ReferenceEquals(nf, match)) (reused ??= new HashSet<Fiber>()).Add(match);
            result.Add(nf);
        }
        foreach (var o in old)
            if (reused is null || !reused.Contains(o))
            {
                Unmount(o);
                _layoutAffected = true;
            }
        if (!_layoutAffected)
        {
            if (result.Count != old.Count) _layoutAffected = true;
            else
                for (int i = 0; i < result.Count; i++)
                    if (!ReferenceEquals(result[i], old[i]))
                    {
                        _layoutAffected = true; // reordered
                        break;
                    }
        }
        f.Children = result;
    }

    private void WarnDuplicateKey(Fiber parent, string key)
    {
        string owner = parent.Node.GetType().Name;
        for (var p = parent; p != null; p = p.Parent)
            if (p.IsComponent)
            {
                owner = Rimact.Inspector.Name(p);
                break;
            }
        if (_loggedErrors.Add("duplicate key " + owner + "/" + key))
            Backend.LogError($"[Rimact] Duplicate key \"{key}\" among the children of {owner}: the later sibling loses its state on every render. Give each sibling a unique key.");
    }

    internal void Unmount(Fiber f)
    {
        if (f.Unmounted) return;
        foreach (var c in f.Children) Unmount(c);
        f.Unmounted = true;
        if (Focused == f)
        {
            Focused = null;
            FocusVisible = false;
        }
        if (f.Hooks != null)
            for (int i = f.Hooks.Count - 1; i >= 0; i--)
                f.Hooks[i].Unmount();
        if (f.SubscribedProviders != null)
            foreach (var p in f.SubscribedProviders) p.Consumers?.Remove(f);
        Dispatcher.OnUnmount(f);
    }

    #endregion

    #region Keyboard focus

    // Space and Enter click it. Keys bubble up from it to OnKeyDown
    internal Fiber? Focused { get; private set; }
    // Focus came from the keyboard, so the ring is drawn
    internal bool FocusVisible { get; private set; }
    // Focused after its first layout
    private Fiber? _autoFocus;
    public Color FocusRingColor { get; set; } = Theme.Default.Ring;

    // f must be a host fiber. null clears focus
    internal void Focus(Fiber? f, bool visible)
    {
        if (f is { Unmounted: true }) f = null;
        var old = Focused;
        FocusVisible = visible && f != null;
        if (old == f) return;
        Focused = f;
        if (old is { Unmounted: false }) old.Node.Events?.OnFocus?.Invoke(false);
        f?.Node.Events?.OnFocus?.Invoke(true);
        if (f != null && visible)
            for (var p = f.Parent; p != null; p = p.Parent)
                if (!p.IsTransparent) p.Node.Events?.OnReveal?.Invoke(f.Rect);
    }

    // Tab order, wrapping around. from defaults to the focused node and can be a component fiber.
    // scope keeps the move inside one focus group (arrow keys)
    internal bool MoveFocus(bool backward, Fiber? from = null, Fiber? scope = null)
    {
        var list = new List<Fiber>();
        if (scope != null) CollectFocusable(scope, list, scope);
        else
        {
            if (Root != null) CollectFocusable(Root, list, null);
            foreach (var ov in Overlays) CollectFocusable(ov, list, null);
        }
        if (list.Count == 0) return false;
        from ??= Focused;
        int i = from is null ? -1 : list.IndexOf(from);
        if (i < 0 && from != null) i = list.FindIndex(x => IsWithin(x, from));
        int next = i < 0 ? (backward ? list.Count - 1 : 0) : (i + (backward ? list.Count - 1 : 1)) % list.Count;
        Focus(list[next], true);
        return true;
    }

    // A focus group counts as one Tab stop unless it is expand
    private void CollectFocusable(Fiber f, List<Fiber> list, Fiber? expand)
    {
        if (f.DisabledTree && !f.IsTransparent) return;
        if (f != expand && !f.IsTransparent && f.Node.Events?.FocusGroup != null)
        {
            var inner = new List<Fiber>();
            CollectFocusable(f, inner, f);
            if (inner.Count > 0)
                list.Add(inner.Find(x => x == Focused) ?? inner.Find(x => x.Node.Events?.FocusEntry == true) ?? inner[0]);
            return;
        }
        if (!f.IsTransparent && f.Node is not OverlayNode && f.Node.Events?.IsFocusable == true) list.Add(f);
        foreach (var c in f.Children)
            if (c.Node is not OverlayNode) CollectFocusable(c, list, expand);
    }

    private static bool IsWithin(Fiber f, Fiber ancestor)
    {
        for (var p = f; p != null; p = p.Parent)
            if (p == ancestor) return true;
        return false;
    }

    internal Fiber? FindProvider(Fiber f, IContext ctx)
    {
        for (var p = f.Parent; p != null; p = p.Parent)
            if (p.Node is ProviderNode pn && pn.Context == ctx) return p;
        for (var p = ContextParent; p != null; p = p.Parent)
            if (p.Node is ProviderNode pn && pn.Context == ctx) return p;
        // Then the hosts further up
        var outer = ContextParent?.Host;
        while (outer?.ContextParent != null)
        {
            for (var p = outer.ContextParent; p != null; p = p.Parent)
                if (p.Node is ProviderNode pn && pn.Context == ctx) return p;
            outer = outer.ContextParent.Host;
        }
        return null;
    }

    #endregion

    #region Effects

    internal void QueueEffect(EffectSlot s) => (s.IsLayout ? _layoutEffects : _effects).Add(s);

    private void FlushEffects(List<EffectSlot> queue)
    {
        if (queue.Count == 0) return;
        // Children first. Same depth keeps render order
        var batch = queue.OrderByDescending(s => s.Fiber.Depth).ToArray();
        queue.Clear();
        foreach (var s in batch)
        {
            try
            {
                s.Run();
            }
            catch (Exception ex) when (CatchOutsideRender(s.Fiber, ex))
            {
            }
        }
    }

    #endregion

    #region Lifetime

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var root = Root;
        Root = null;
        if (root != null)
        {
            try { Unmount(root); }
            catch (Exception ex) { Backend.LogError("[Rimact] Exception during unmount: " + ex); }
        }
    }

    #endregion
}
