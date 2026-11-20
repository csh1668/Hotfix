using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rimact;

/// <summary>Node factories, for using static Rimact.Elements. A null child keeps its slot, so siblings keep state.</summary>
internal static partial class Elements
{
    public static BoxNode Row(params Node?[] children) => new(Axis.Row, NodeArrays.Normalize(children));
    public static BoxNode Row(IEnumerable<Node?> children) => Row(children.ToArray());
    public static BoxNode Column(params Node?[] children) => new(Axis.Column, NodeArrays.Normalize(children));
    public static BoxNode Column(IEnumerable<Node?> children) => Column(children.ToArray());
    /// <summary>Layers children on top of each other. Later ones paint above.</summary>
    public static BoxNode Stack(params Node?[] children) => new(Axis.Stack, NodeArrays.Normalize(children));
    public static BoxNode Stack(IEnumerable<Node?> children) => Stack(children.ToArray());

    public static FragmentNode Fragment(params Node?[] children) => new(NodeArrays.Normalize(children));
    public static FragmentNode Fragment(IEnumerable<Node?> children) => Fragment(children.ToArray());

    public static TextNode Text(string text) => new(text ?? "");

    /// <summary>Text from a translation, keeping its color tags.</summary>
    /// <remarks>Passing a TaggedString to a string parameter strips its tags. Pass .Resolve() there instead.</remarks>
    public static TextNode Text(Verse.TaggedString text) => new(Components.GameText.Resolve(text));

    public static ImageNode Image(Icon icon) => new(icon);

    /// <summary>Empty box that takes up the remaining space.</summary>
    public static BoxNode Spacer(float grow = 1f) => new(Axis.Row, Array.Empty<Node>()) { Layout = LayoutProps.Default with { Grow = grow } };

    /// <summary>Empty box, size x size.</summary>
    public static BoxNode Gap(float size) => new(Axis.Row, Array.Empty<Node>()) { Layout = LayoutProps.Default with { Width = size, Height = size } };

    /// <summary>Raw IMGUI escape hatch. draw gets the leaf's rect on every IMGUI event, not just repaint.</summary>
    /// <remarks>Layout can't measure it. Set its size with Width/Height.</remarks>
    public static NativeNode Native(Action<Rect> draw) => new(draw);

    /// <summary>Custom-painted leaf. paint runs on repaint only.</summary>
    public static CanvasNode Canvas(Action<IPainter, Rect> paint) => new(paint);

    public static ProviderNode Provide<T>(Context<T> context, T value, Node child) => new(context, value, child);

    public static ProviderNode ThemeProvider(Theme theme, Node child) => Provide(Theme.Context, theme, child);

    /// <summary>Overlay placed beside its anchor, the nearest enclosing box.</summary>
    /// <example><code>Stack(trigger, open ? Overlay(menu, onPointerDownOutside: close) : null)</code></example>
    public static OverlayNode Overlay(Node child, Placement placement = Placement.Below, Action? onPointerDownOutside = null, Action? onEscape = null) =>
        new(child, placement) { OnPointerDownOutside = onPointerDownOutside, OnEscape = onEscape };
}
