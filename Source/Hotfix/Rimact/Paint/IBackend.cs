using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

/// <summary>The drawing surface a Canvas gets.</summary>
internal interface IPainter
{
    void FillRect(Rect r, Color c);
    void DrawBorder(Rect r, Color c, float thickness);
    void DrawText(Rect r, string text, TextStyle style, Color color);
    void DrawIcon(Rect r, Icon icon, Color tint);
    void DrawVanilla(Rect r, VanillaPart part, InteractionState state);
    Vector2 MeasureText(string text, FontSize font, float wrapWidth);
    Vector2 MeasureText(string text, TextStyle style, float wrapWidth);

    void FillRounded(Rect r, Color c, float radius);
    /// <summary>Strokes along the inside of r.</summary>
    void StrokeRounded(Rect r, Color c, float thickness, float radius);
    void FillGradient(Rect r, Color from, Color to, GradientDirection direction);
    void DrawShadow(Rect r, float radius, Shadow shadow);
    void DrawLine(Vector2 a, Vector2 b, Color c, float thickness);
    /// <summary>Light on the top and right, dark on the left and bottom.</summary>
    void DrawBevel(Rect r, Color light, Color dark, float width);
    /// <summary>Diagonal stripes clipped to r. phase, in pixels, scrolls them.</summary>
    void DrawStripes(Rect r, Color c, float spacing, float width, float phase);
    /// <summary>Every three vertices make a triangle. Skipped where the backend can't draw shapes.</summary>
    void FillTriangles(IReadOnlyList<Vector2> vertices, Color c);
    /// <summary>Drawn just outside r.</summary>
    void DrawFocusRing(Rect r, Color c);
}

// Box drawn in place of the vanilla text field skin. FocusBorder is used while the field has focus
internal sealed record FieldChrome(Color Bg, Color Border, Color FocusBorder, float Radius, float PadX, Bevel? Bevel = null);

/// <summary>An option in a vanilla FloatMenu. Glyph icons and non-thing defs show no icon.</summary>
internal readonly record struct MenuItem(string Label, Action? OnSelect, bool Disabled = false)
{
    public Icon? Icon { get; init; }
    public string? Tooltip { get; init; }
}

internal sealed record WindowOptions
{
    /// <summary>Top bar title. null means no bar.</summary>
    public string? Title { get; init; }
    /// <summary>With AutoHeight, only the width is used.</summary>
    public Vector2 Size { get; init; } = new(500, 400);
    /// <summary>Fits the height to the content as it changes, up to the screen height.</summary>
    /// <remarks>A scroll area measures 0 tall without a MaxHeight, so give scroll areas inside one.</remarks>
    public bool AutoHeight { get; init; }
    public bool Modal { get; init; } = true;
    public bool Draggable { get; init; } = true;
    public bool CloseOnClickedOutside { get; init; }
    public bool CloseButton { get; init; } = true;
    public bool Pause { get; init; }
}

internal sealed record WindowRequest(Host Content, WindowOptions Options)
{
    // Runs once, also when the user closes the window with X or Esc
    public Action? OnClosed { get; init; }
}

internal interface IWindowHandle
{
    void Close();
    // A size change keeps the top edge and horizontal center
    void Update(WindowOptions options);
}

// The core's only way into IMGUI. Coordinates are absolute, in the host rect's space. Tests use a FakeBackend
internal interface IBackend : IPainter
{
    void PushClip(Rect r);
    void PopClip();
    void PushAlpha(float a);
    void PopAlpha();
    void Tooltip(Rect r, string text);
    void PlaySound(UiSound sound);
    // Runs a native leaf's IMGUI code inside clip. True if it used the event. owner is null unless diagnostics are on
    bool Native(Rect r, Rect clip, Action<Rect> draw, bool enabled, string? owner = null);
    // Call only inside a native leaf's callback, with the rect it got, unchanged.
    // controlName goes to GUI.SetNextControlName. chrome replaces the vanilla skin
    string TextField(Rect r, string text, bool multiline, string? controlName, FieldChrome? chrome = null);
    void FocusControl(string controlName);
    string? FocusedControl { get; }
    void ClearFocus();
    void OpenMenu(IReadOnlyList<MenuItem> items);
    IWindowHandle OpenWindow(WindowRequest request);
    void LogError(string message);
    // Whether the button is down now, wherever the pointer is
    bool IsMouseHeld(int button);
    float RealTime { get; }
    int FrameId { get; }
}
