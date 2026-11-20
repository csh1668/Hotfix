using System;
using UnityEngine;
using Verse;

namespace Rimact;

// A Rimact tree in any IMGUI callback. Call Draw on every event and keep the instance so state survives.
// RimactUI.Draw keeps these per call site for you
internal sealed class RimactHost : IDisposable
{
    public RimactHost(Node root) => Core = new Host(root, ImguiBackend.Instance);

    internal RimactHost(Host core) => Core = core;

    public Host Core { get; }

    public Action? CloseRequested
    {
        get => Core.CloseRequested;
        set => Core.CloseRequested = value;
    }

    public void SetRoot(Node root) => Core.SetRoot(root);

    public void Draw(Rect rect)
    {
        var e = Event.current;
        if (e is null) return;
        var input = Convert(e);

        // Restore the IMGUI globals so nothing leaks into the caller
        var backend = ImguiBackend.Instance;
        bool used;
        using (Temporary.Gui())
        {
            backend.BeginFrame();
            try
            {
                used = Core.Process(rect, input);
            }
            finally
            {
                backend.EndFrame();
            }
        }
        if (used && e.type != EventType.Repaint && e.type != EventType.Layout && e.type != EventType.Used) e.Use();
    }

    private static InputEvent Convert(Event e)
    {
        var type = e.type switch
        {
            EventType.Repaint => InputType.Repaint,
            EventType.Layout => InputType.Layout,
            EventType.MouseDown => InputType.MouseDown,
            EventType.MouseUp => InputType.MouseUp,
            EventType.MouseDrag => InputType.MouseDrag,
            EventType.MouseMove => InputType.MouseMove,
            EventType.ScrollWheel => InputType.Wheel,
            EventType.KeyDown => InputType.KeyDown,
            EventType.KeyUp => InputType.KeyUp,
            _ => InputType.Other,
        };
        return new InputEvent(type, e.mousePosition)
        {
            Delta = e.delta,
            Button = e.button,
            ClickCount = e.clickCount,
            Shift = e.shift,
            Ctrl = e.control,
            Alt = e.alt,
            Key = e.keyCode,
            Character = e.character,
            TextFieldFocused = GUIUtility.keyboardControl != 0,
        };
    }

    public void Dispose() => Core.Dispose();
}
