using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Rimact;

/// <summary>Scoped IMGUI global changes. Every scope restores all the globals on dispose, even ones set directly.</summary>
/// <example><code>using (Temporary.Color(Color.gray).Anchor(TextAnchor.MiddleCenter)) Widgets.Label(rect, "Disabled");</code></example>
internal static class Temporary
{
    /// <summary>Only saves. With using var, undoes whatever the rest of the method changes.</summary>
    public static GuiScope Gui() => new(GuiSnapshot.Capture());

    public static GuiScope Color(Color color) => Gui().Color(color);
    /// <summary>Multiplies the alpha of GUI.color.</summary>
    public static GuiScope Alpha(float alpha) => Gui().Alpha(alpha);
    public static GuiScope Background(Color color) => Gui().Background(color);
    public static GuiScope Content(Color color) => Gui().Content(color);
    public static GuiScope Enabled(bool enabled) => Gui().Enabled(enabled);
    public static GuiScope Font(GameFont font) => Gui().Font(font);
    public static GuiScope Anchor(TextAnchor anchor) => Gui().Anchor(anchor);
    public static GuiScope Wrap(bool wrap) => Gui().Wrap(wrap);
    public static GuiScope Matrix(Matrix4x4 matrix) => Gui().Matrix(matrix);

    /// <summary>Widgets.BeginGroup, ended on dispose.</summary>
    public static GroupScope Group(Rect rect)
    {
        Widgets.BeginGroup(rect);
        return new GroupScope(true);
    }

    /// <summary>Widgets.BeginScrollView, ended on dispose.</summary>
    public static ScrollViewScope ScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true)
    {
        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, showScrollbars);
        return new ScrollViewScope(true);
    }
}

/// <summary>Restores the IMGUI globals on dispose. Each method sets one global now and returns the scope.</summary>
internal readonly ref struct GuiScope
{
    private readonly GuiSnapshot _saved;

    internal GuiScope(GuiSnapshot saved) => _saved = saved;

    public GuiScope Color(Color color)
    {
        GUI.color = color;
        return this;
    }

    public GuiScope Alpha(float alpha)
    {
        var c = GUI.color;
        GUI.color = new Color(c.r, c.g, c.b, c.a * alpha);
        return this;
    }

    public GuiScope Background(Color color)
    {
        GUI.backgroundColor = color;
        return this;
    }

    public GuiScope Content(Color color)
    {
        GUI.contentColor = color;
        return this;
    }

    public GuiScope Enabled(bool enabled)
    {
        GUI.enabled = enabled;
        return this;
    }

    public GuiScope Font(GameFont font)
    {
        Text.Font = font;
        return this;
    }

    public GuiScope Anchor(TextAnchor anchor)
    {
        Text.Anchor = anchor;
        return this;
    }

    public GuiScope Wrap(bool wrap)
    {
        Text.WordWrap = wrap;
        return this;
    }

    public GuiScope Matrix(Matrix4x4 matrix)
    {
        GUI.matrix = matrix;
        return this;
    }

    /// <summary>A default scope does nothing.</summary>
    public void Dispose() => _saved.Restore();
}

internal readonly ref struct GroupScope
{
    private readonly bool _open;
    internal GroupScope(bool open) => _open = open;
    public void Dispose()
    {
        if (_open) Widgets.EndGroup();
    }
}

internal readonly ref struct ScrollViewScope
{
    private readonly bool _open;
    internal ScrollViewScope(bool open) => _open = open;
    public void Dispose()
    {
        if (_open) Widgets.EndScrollView();
    }
}

// The IMGUI globals that drawing code commonly changes
internal readonly record struct GuiSnapshot(
    Color Color, Color Background, Color Content, bool Enabled, GameFont Font, TextAnchor Anchor, bool Wrap, Matrix4x4 Matrix)
{
    // false for default, which Restore skips
    public bool Captured { get; private init; }

    public static GuiSnapshot Capture() =>
        new(GUI.color, GUI.backgroundColor, GUI.contentColor, GUI.enabled, Text.Font, Text.Anchor, Text.WordWrap, GUI.matrix) { Captured = true };

    // Writes only what differs. Setting GUI.matrix always reaches the renderer
    public void Restore()
    {
        if (!Captured) return;
        if (GUI.color != Color) GUI.color = Color;
        if (GUI.backgroundColor != Background) GUI.backgroundColor = Background;
        if (GUI.contentColor != Content) GUI.contentColor = Content;
        if (GUI.enabled != Enabled) GUI.enabled = Enabled;
        if (Text.Font != Font) Text.Font = Font;
        if (Text.Anchor != Anchor) Text.Anchor = Anchor;
        if (Text.WordWrap != Wrap) Text.WordWrap = Wrap;
        if (GUI.matrix != Matrix) GUI.matrix = Matrix;
    }

    public static List<string> Changes(GuiSnapshot before, GuiSnapshot after)
    {
        var changed = new List<string>();
        if (before.Color != after.Color) changed.Add("GUI.color");
        if (before.Background != after.Background) changed.Add("GUI.backgroundColor");
        if (before.Content != after.Content) changed.Add("GUI.contentColor");
        if (before.Enabled != after.Enabled) changed.Add("GUI.enabled");
        if (before.Font != after.Font) changed.Add("Text.Font");
        if (before.Anchor != after.Anchor) changed.Add("Text.Anchor");
        if (before.Wrap != after.Wrap) changed.Add("Text.WordWrap");
        if (before.Matrix != after.Matrix) changed.Add("GUI.matrix");
        return changed;
    }
}
