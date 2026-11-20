using UnityEngine;

namespace Rimact;

internal enum InputType : byte { Layout, Repaint, MouseDown, MouseUp, MouseDrag, MouseMove, Wheel, KeyDown, KeyUp, Other }

// An IMGUI event for the core. Positions are in the host rect's space
internal readonly record struct InputEvent(InputType Type, Vector2 Position)
{
    public Vector2 Delta { get; init; }
    public int Button { get; init; }
    public int ClickCount { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public KeyCode Key { get; init; }
    public char Character { get; init; }
    // An IMGUI text field has focus, so the key skips the regular UseKeyDown handlers
    public bool TextFieldFocused { get; init; }

    public bool IsMouse => Type is InputType.MouseDown or InputType.MouseUp or InputType.MouseDrag or InputType.MouseMove or InputType.Wheel;

    public static InputEvent Repaint(Vector2 pointer) => new(InputType.Repaint, pointer);
    public static InputEvent Down(Vector2 p, int button = 0) => new(InputType.MouseDown, p) { Button = button, ClickCount = 1 };
    public static InputEvent Up(Vector2 p, int button = 0) => new(InputType.MouseUp, p) { Button = button };
    public static InputEvent Drag(Vector2 p, Vector2 delta) => new(InputType.MouseDrag, p) { Delta = delta };
    public static InputEvent Scroll(Vector2 p, float dy) => new(InputType.Wheel, p) { Delta = new Vector2(0, dy) };
    public static InputEvent KeyDown(KeyCode key, char ch = '\0') => new(InputType.KeyDown, default) { Key = key, Character = ch };
}
