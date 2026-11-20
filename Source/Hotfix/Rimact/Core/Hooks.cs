using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Rimact;

internal enum AsyncStatus : byte { Pending, Done, Failed }

/// <summary>Result of UseAsync. Value keeps the last good result while a new run is pending or after a failure.</summary>
internal readonly record struct AsyncState<T>(AsyncStatus Status, T? Value, Exception? Error, Action Reload)
{
    public bool Loading => Status == AsyncStatus.Pending;
}

internal sealed class HookOrderException(string message) : InvalidOperationException(message);

// One hook's storage. Fiber.Hooks holds them in call order
internal abstract class HookSlot
{
    public Fiber Fiber = null!;
    public abstract string Kind { get; }
    public virtual void Unmount() { }
}

internal sealed class StateSlot<T> : HookSlot
{
    public T Value = default!;
    public override string Kind => $"UseState<{typeof(T).Name}>";

    public void Set(T next)
    {
        if (Fiber.Unmounted) return;
        if (EqualityComparer<T>.Default.Equals(Value, next)) return;
        Value = next;
        Fiber.Host.MarkDirty(Fiber);
    }

    public void Set(Func<T, T> update) => Set(update(Value)); // latest value, so no update is lost
}

/// <summary>Result of UseState. Value is this render's snapshot. Set(x =&gt; ...) works from the latest value.</summary>
internal readonly struct State<T>(StateSlot<T> slot, T value)
{
    public T Value { get; } = value;
    public void Set(T value) => slot.Set(value);
    public void Set(Func<T, T> update) => slot.Set(update);

    public void Deconstruct(out T value, out Action<T> set)
    {
        value = Value;
        set = slot.Set;
    }
}

internal sealed class Ref<T>(T initial)
{
    public T Current = initial;
}

internal sealed class RefSlot<T> : HookSlot
{
    public Ref<T> Ref = null!;
    public override string Kind => $"UseRef<{typeof(T).Name}>";
}

internal sealed class EffectSlot : HookSlot
{
    public bool IsLayout;
    public object?[]? Deps;
    public bool HasRun;
    public Func<Action?>? Pending;
    public Action? Cleanup;
    public override string Kind => IsLayout ? "UseLayoutEffect" : "UseEffect";

    public void Run()
    {
        var effect = Pending;
        Pending = null;
        if (effect is null || Fiber.Unmounted) return;
        var cleanup = Cleanup;
        Cleanup = null;
        cleanup?.Invoke();
        Cleanup = effect();
    }

    public override void Unmount()
    {
        Pending = null;
        var c = Cleanup;
        Cleanup = null;
        c?.Invoke();
    }
}

internal sealed class MemoSlot<T> : HookSlot
{
    public object?[]? Deps;
    public T Value = default!;
    public override string Kind => $"UseMemo<{typeof(T).Name}>";
}

internal sealed class ExternalSlot<T> : HookSlot, IExternalSync
{
    public Func<T> Read = null!;
    public T Last = default!;
    public IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
    public override string Kind => $"UseExternal<{typeof(T).Name}>";

    public void Sync()
    {
        if (Fiber.Unmounted) return;
        var v = Read();
        if (Comparer.Equals(v, Last)) return;
        Last = v;
        Fiber.Host.MarkDirty(Fiber);
    }

    public override void Unmount() => Fiber.Host.Externals.Remove(this);
}

internal interface IExternalSync
{
    void Sync();
}

internal sealed class LayoutSlot : HookSlot
{
    public LayoutInfo Last;
    public bool Registered;
    public override string Kind => "UseLayout";
    public override void Unmount() => Fiber.Host.LayoutWatchers.Remove(this);
}

/// <summary>Rect in absolute coordinates and content size, from the last layout pass.</summary>
internal readonly record struct LayoutInfo(Rect Rect, Vector2 ContentSize);

internal sealed class IntervalSlot : HookSlot
{
    public Action Callback = null!;
    public float Seconds;
    public float Next = float.NaN;
    public override string Kind => "UseInterval";
    public override void Unmount() => Fiber.Host.Intervals.Remove(this);
}

internal sealed class AnimationSlot : HookSlot
{
    public float Value;
    public float Stamp = float.NaN;
    public bool Settled = true;
    public override string Kind => "UseAnimated";
}

internal sealed class PulseSlot : HookSlot
{
    public override string Kind => "UsePulse";
}

internal sealed class KeyDownSlot : HookSlot
{
    public Func<KeyEvent, bool> Handler = null!;
    public bool BeforeTextFields;
    public override string Kind => "UseKeyDown";
    public override void Unmount() => Fiber.Host.KeyHandlers.Remove(this);
}

internal sealed class DialogKeySlot : HookSlot
{
    public Action? Handler;
    public bool IsCancel;
    public bool Registered;
    public override string Kind => IsCancel ? "UseCancelKey" : "UseAcceptKey";
    public override void Unmount() => (IsCancel ? Fiber.Host.CancelHandlers : Fiber.Host.AcceptHandlers).Remove(this);
}

internal readonly struct HostHandle
{
    private readonly Host _host;
    public HostHandle(Host host) => _host = host;
    /// <summary>Asks the window holding this host (RimactWindow, Dialog) to close.</summary>
    public void Close() => _host.CloseRequested?.Invoke();
    public IBackend Backend => _host.Backend;
    /// <summary>Paint-time animations should hold still while this is set.</summary>
    public bool ReduceMotion => _host.ReduceMotion;
}

/// <summary>Component hooks. Call them only while rendering.</summary>
/// <remarks>Same order every render: no hooks in conditions, loops or after an early return.</remarks>
internal static partial class Hooks
{
    [ThreadStatic] internal static Fiber? Current;
    [ThreadStatic] internal static int Index;

    private static Fiber CurrentFiber =>
        Current ?? throw new InvalidOperationException("Rimact hooks can only be called while a component is rendering.");

    private static TSlot Slot<TSlot>(Func<TSlot> create) where TSlot : HookSlot
    {
        var f = CurrentFiber;
        int i = Index++;
        f.Hooks ??= [];
        if (i < f.Hooks.Count)
        {
            if (f.Hooks[i] is TSlot existing) return existing;
            throw new HookOrderException(
                $"Rimact: hook order violation in {f}: hook #{i} was {f.Hooks[i].Kind} last render but is {KindOf(typeof(TSlot))} now. " +
                "Don't call hooks inside conditions, loops, or after an early return.");
        }
        if (f.HooksSealed)
            throw new HookOrderException($"Rimact: hook order violation in {f}: called more hooks than last render (hook #{i}).");
        var slot = create();
        slot.Fiber = f;
        f.Hooks.Add(slot);
        return slot;
    }

    // Hook name from the slot type. Doesn't create a slot, since that can run user code
    private static string KindOf(Type slot)
    {
        var name = slot.Name.Split('`')[0];
        if (name.EndsWith("Slot")) name = name[..^4];
        return name switch
        {
            "Animation" => "UseAnimated",
            "Pulse" => "UsePulse",
            _ => "Use" + name,
        };
    }

    /// <summary>Component state. Setting a different value re-renders, setting an equal one does nothing.</summary>
    /// <example><code>var (count, setCount) = UseState(0);</code></example>
    public static State<T> UseState<T>(T initial)
    {
        var s = Slot(() => new StateSlot<T> { Value = initial });
        return new State<T>(s, s.Value);
    }

    /// <summary>State whose initial value is computed once, on mount.</summary>
    public static State<T> UseState<T>(Func<T> initial)
    {
        var s = Slot(() => new StateSlot<T> { Value = initial() });
        return new State<T>(s, s.Value);
    }

    public static (TState state, Action<TAction> dispatch) UseReducer<TState, TAction>(Func<TState, TAction, TState> reducer, TState initial)
    {
        var s = Slot(() => new StateSlot<TState> { Value = initial });
        return (s.Value, a => s.Set(cur => reducer(cur, a)));
    }

    public static Ref<T> UseRef<T>(T initial)
    {
        var s = Slot(() => new RefSlot<T> { Ref = new Ref<T>(initial) });
        return s.Ref;
    }

    /// <summary>Runs effect after render, at the end of the host event. Re-runs when deps change.</summary>
    /// <remarks>Empty deps = once on mount. The returned action is the cleanup. Child effects run first.</remarks>
    /// <example><code>UseEffect(() =&gt; { src.Changed += OnChanged; return () =&gt; src.Changed -= OnChanged; }, src);</code></example>
    public static void UseEffect(Func<Action?> effect, params object?[]? deps) => Effect(effect, deps, layout: false);

    /// <summary>Runs effect after every render.</summary>
    public static void UseEffect(Func<Action?> effect) => Effect(effect, null, layout: false);

    public static void UseEffect(Action effect, params object?[]? deps) => Effect(() => { effect(); return null; }, deps, layout: false);

    /// <summary>Runs effect after every render.</summary>
    public static void UseEffect(Action effect) => Effect(() => { effect(); return null; }, null, layout: false);

    /// <summary>Runs effect once after mount. Its cleanup runs on unmount.</summary>
    public static void UseMountEffect(Func<Action?> effect) => Effect(effect, Array.Empty<object>(), layout: false);

    /// <summary>Like UseEffect, but runs right after rendering, before layout and paint.</summary>
    public static void UseLayoutEffect(Func<Action?> effect, params object?[]? deps) => Effect(effect, deps, layout: true);

    /// <summary>Runs effect after every render, before layout.</summary>
    public static void UseLayoutEffect(Func<Action?> effect) => Effect(effect, null, layout: true);

    private static void Effect(Func<Action?> effect, object?[]? deps, bool layout)
    {
        var s = Slot(() => new EffectSlot { IsLayout = layout });
        if (!s.HasRun || deps is null || !DepsEqual(s.Deps, deps))
        {
            s.HasRun = true;
            s.Deps = deps;
            s.Pending = effect;
            s.Fiber.Host.QueueEffect(s);
        }
    }

    /// <summary>Caches factory's result until deps change. No deps = computed once.</summary>
    /// <remarks>A cached node skips its whole subtree on re-render. Components inside still update on their own.</remarks>
    /// <example><code>var visible = UseMemo(() =&gt; items.Where(i =&gt; i.Name.Contains(filter)).ToList(), items, filter);</code></example>
    public static T UseMemo<T>(Func<T> factory, params object?[] deps)
    {
        var s = Slot(() => new MemoSlot<T>());
        if (s.Deps is null || !DepsEqual(s.Deps, deps))
        {
            s.Value = factory();
            s.Deps = deps;
        }
        return s.Value;
    }

    /// <summary>Keeps the first callback until deps change. [] keeps it for good, so it sees only first-render values.</summary>
    public static T UseCallback<T>(T callback, object?[] deps) where T : Delegate => UseMemo(() => callback, deps);

    /// <summary>Nearest provided value, or the context's Default. Re-renders when the value changes.</summary>
    /// <remarks>Looks past the host root into parent hosts, e.g. from a dialog into the tree that opened it.</remarks>
    public static T UseContext<T>(Context<T> context)
    {
        var f = CurrentFiber;
        var provider = f.Host.FindProvider(f, context);
        if (provider is null) return context.Default;
        provider.Consumers ??= new HashSet<Fiber>();
        if (provider.Consumers.Add(f))
            (f.SubscribedProviders ??= new List<Fiber>()).Add(provider);
        return (T)((ProviderNode)provider.Node).Value!;
    }

    /// <summary>Reads state from outside the UI, like ModSettings or game objects. Re-renders when it changes.</summary>
    /// <remarks>read runs on every host event, so keep it cheap.</remarks>
    /// <example><code>var enabled = UseExternal(() =&gt; MyMod.Settings.enabled);</code></example>
    public static T UseExternal<T>(Func<T> read, IEqualityComparer<T>? comparer = null)
    {
        var s = Slot(() => new ExternalSlot<T>());
        bool first = s.Read is null;
        s.Read = read;
        if (comparer != null) s.Comparer = comparer;
        s.Last = read();
        if (first) s.Fiber.Host.Externals.Add(s);
        return s.Last;
    }

    /// <summary>Runs an async operation on mount and when deps change. Re-renders when it finishes.</summary>
    /// <remarks>Polled on host events, so the task may finish on any thread. Changing deps or unmounting cancels the token.</remarks>
    /// <example><code>var models = UseAsync(token =&gt; engine.ListModelsAsync(token), engine);</code></example>
    public static AsyncState<T> UseAsync<T>(Func<CancellationToken, Task<T>> start, params object?[] deps)
    {
        var run = UseRef<Task<T>?>(null);
        var runDeps = UseRef<object?[]?>(null);
        var last = UseRef<T?>(default);
        var reload = UseState(0);
        var all = new object?[deps.Length + 1];
        Array.Copy(deps, all, deps.Length);
        all[deps.Length] = reload.Value;
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            Task<T> task;
            try
            {
                task = start(cts.Token);
            }
            catch (Exception ex)
            {
                task = Task.FromException<T>(ex);
            }
            run.Current = task;
            runDeps.Current = all;
            return () => cts.Cancel();
        }, all);
        UseExternal(() => (run.Current, run.Current?.Status));
        var again = UseCallback(new Action(() => reload.Set(n => n + 1)), []);

        var t = run.Current;
        // Until the effect starts a run for these deps, the current run is for older ones
        if (t is null || !t.IsCompleted || !DepsEqual(runDeps.Current, all)) return new(AsyncStatus.Pending, last.Current, null, again);
        if (t.IsCanceled) return new(AsyncStatus.Failed, last.Current, new TaskCanceledException(t), again);
        if (t.IsFaulted) return new(AsyncStatus.Failed, last.Current, t.Exception!.InnerException ?? t.Exception, again);
        last.Current = t.Result;
        return new(AsyncStatus.Done, t.Result, null, again);
    }

    /// <summary>This component's rect and content size from the last layout. Re-renders when they change.</summary>
    /// <remarks>Render and layout alternate at most 4 times per event. The rest waits for the next event.</remarks>
    public static LayoutInfo UseLayout()
    {
        var s = Slot(() => new LayoutSlot());
        if (!s.Registered)
        {
            s.Registered = true;
            s.Fiber.Host.LayoutWatchers.Add(s);
        }
        return s.Last;
    }

    /// <summary>Calls callback every seconds of real time, checked once per frame. 0 or less pauses it.</summary>
    /// <remarks>Its state changes re-render this component and its children, so keep it where the result is shown.</remarks>
    public static void UseInterval(Action callback, float seconds)
    {
        var s = Slot(() => new IntervalSlot());
        bool first = s.Callback is null;
        s.Callback = callback;
        if (first || !Mathf.Approximately(s.Seconds, seconds)) s.Next = s.Fiber.Host.Backend.RealTime + seconds;
        s.Seconds = seconds;
        if (first) s.Fiber.Host.Intervals.Add(s);
    }

    /// <summary>Eases toward target, settling in about seconds. Re-renders every frame until it settles.</summary>
    /// <remarks>Starts at from on mount, or at target if from is null. Returns target while ReduceMotion is set.</remarks>
    /// <example><code>float x = UseAnimated(on ? 16f : 0f);</code></example>
    public static float UseAnimated(float target, float seconds = 0.15f, float? from = null)
    {
        var s = Slot(() => new AnimationSlot());
        var host = s.Fiber.Host;
        float now = host.Backend.RealTime;
        if (float.IsNaN(s.Stamp) && from is { } start && !host.ReduceMotion && seconds > 0f && start != target)
        {
            s.Value = start;
            s.Stamp = now;
            s.Settled = false;
            host.RequestFrame(s.Fiber);
            return start;
        }
        if (float.IsNaN(s.Stamp) || host.ReduceMotion || seconds <= 0f)
        {
            s.Value = target;
            s.Stamp = now;
            s.Settled = true;
            return target;
        }
        if (s.Settled)
        {
            if (s.Value == target) return target;
            s.Settled = false; // idle time doesn't count
            s.Stamp = now;
            host.RequestFrame(s.Fiber);
            return s.Value;
        }
        float dt = Mathf.Clamp(now - s.Stamp, 0f, 0.1f);
        s.Stamp = now;
        s.Value = Mathf.Lerp(s.Value, target, 1f - Mathf.Exp(-dt * 4.6f / seconds));
        if (Mathf.Abs(s.Value - target) <= 0.002f * Mathf.Max(1f, Mathf.Abs(target)))
        {
            s.Value = target;
            s.Settled = true;
        }
        else host.RequestFrame(s.Fiber);
        return s.Value;
    }

    /// <summary>0..1 sine wave, period in seconds. Returns 1 while ReduceMotion is set.</summary>
    /// <remarks>Re-renders the component and all its children every frame. Call it from the smallest component that needs it.</remarks>
    public static float UsePulse(float period = 2f)
    {
        var s = Slot(() => new PulseSlot());
        var host = s.Fiber.Host;
        if (host.ReduceMotion || period <= 0f) return 1f;
        host.RequestFrame(s.Fiber);
        return 0.5f + 0.5f * Mathf.Sin(host.Backend.RealTime * (2f * Mathf.PI / period));
    }

    /// <summary>Key presses while no text field has focus. Return true to consume. Newest handler runs first.</summary>
    /// <remarks>beforeTextFields: sees every key before text fields and native leaves. Return false for keys the field needs.</remarks>
    /// <example><code>UseKeyDown(k =&gt; k.Key == KeyCode.DownArrow &amp;&amp; Next(), beforeTextFields: true);</code></example>
    public static void UseKeyDown(Func<KeyEvent, bool> handler, bool beforeTextFields = false)
    {
        var s = Slot(() => new KeyDownSlot());
        bool first = s.Handler is null;
        s.Handler = handler;
        s.BeforeTextFields = beforeTextFields;
        if (first) s.Fiber.Host.KeyHandlers.Add(s);
    }

    /// <summary>Enter in this window runs handler. The newest non-null one on an enabled component wins.</summary>
    /// <remarks>Enter in a multiline field types a newline instead.</remarks>
    /// <example><code>UseAcceptKey(canSave ? Save : null);</code></example>
    public static void UseAcceptKey(Action? handler) => UseDialogKey(handler, false);

    /// <summary>Esc in this window runs handler. An open popover closes first. With no handler, Esc closes the window.</summary>
    public static void UseCancelKey(Action? handler) => UseDialogKey(handler, true);

    private static void UseDialogKey(Action? handler, bool cancel)
    {
        var s = Slot(() => new DialogKeySlot { IsCancel = cancel });
        bool first = !s.Registered;
        s.Handler = handler;
        if (!first) return;
        s.Registered = true;
        (cancel ? s.Fiber.Host.CancelHandlers : s.Fiber.Host.AcceptHandlers).Add(s);
    }

    /// <summary>Id unique to this component and stable across renders, e.g. for an IMGUI control name.</summary>
    public static string UseId() => UseMemo(() => IdPrefix + ++s_ids);

    // IMGUI control names are global and every mod's copy counts from 1, so the prefix has the assembly name
    internal static readonly string IdPrefix = "rimact." + typeof(Hooks).Assembly.GetName().Name + ".";

    private static int s_ids;

    public static HostHandle UseHost() => new(CurrentFiber.Host);

    // For library components. Dialog uses it as the point its child host inherits context from
    internal static Fiber UseFiber() => CurrentFiber;

    public static Theme UseTheme() => UseContext(Theme.Context);

    public static Strings UseStrings() => UseContext(Strings.Context) ?? Strings.Default;

    internal static bool DepsEqual(object?[]? a, object?[]? b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(a[i], b[i])) return false;
        return true;
    }
}
