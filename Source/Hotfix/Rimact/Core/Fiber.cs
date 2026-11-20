using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

// One position in the tree. Nodes are rebuilt every render, the fiber and its hooks stay while Id and Key match
internal sealed class Fiber
{
    public Fiber(Host host, object id, string? key, Node node, Fiber? parent)
    {
        Host = host;
        Id = id;
        Key = key;
        Node = node;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public readonly Host Host;
    public readonly object Id;
    public readonly string? Key;
    public readonly Fiber? Parent;
    public readonly int Depth;
    public Node Node;
    public List<Fiber> Children = new();
    public List<HookSlot>? Hooks;
    // Set after the first render. The hook count can't change after that
    public bool HooksSealed;

    public bool Dirty;
    // A descendant is dirty, so a render pass has to walk through here
    public bool ChildDirty;
    public bool Unmounted;

    // Error boundaries only. The fallback shows it until reset
    public System.Exception? CaughtError;

    public HashSet<Fiber>? Consumers;          // on a provider
    public List<Fiber>? SubscribedProviders;   // on a consumer

    // Layout results, absolute coordinates
    public Rect Rect;
    public Vector2 ContentSize;
    public Rect ClipRect;                       // ancestor clips intersected, for hit testing
    public bool InOverlay;
    public bool DisabledTree;                   // this or an ancestor is disabled
    public Rect AnchorRect;                     // on an overlay

    // Measure cache, reset every layout pass
    public int MeasureGen = -1;
    public int MeasureNext;
    public readonly Vector4[] MeasureKeys = new Vector4[4];   // (availW, availH, resultW, resultH)

    public int RenderCount;
    public float LastRender = -1f;              // real time

    public float HoverT;                        // 0..1, eased by the painter
    public float HoverStamp = -1f;              // real time. Negative before the first paint

    public string? MeasuredText;
    public TextStyle MeasuredStyle;
    public float MeasuredWidth = float.NaN;
    public Vector2 MeasuredSize;

    public bool IsComponent => Node is UIComponent;

    // Its children join the parent container's layout directly
    public bool IsTransparent => Node is FragmentNode or ProviderNode or UIComponent;

    public override string ToString() => $"{Id}{(Key is null ? "" : "#" + Key)}";
}
