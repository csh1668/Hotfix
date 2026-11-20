using UnityEngine;

namespace Rimact;

/// <summary>Debug switches. They only affect the mod that sets them, since each mod compiles its own Rimact.</summary>
/// <remarks>Not tied to dev mode, which many players leave on. Turn them on under #if DEBUG or a debug setting.</remarks>
internal static class RimactDiagnostics
{
    /// <summary>Warns when a native leaf leaves GUI.color, Text.Font and the like changed. They are restored either way.</summary>
    public static bool CheckGuiState { get; set; }

    /// <summary>Sets GUI.color and backgroundColor to magenta before each draw, so draws that use leftover state show up.</summary>
    public static bool PoisonGuiState { get; set; }

    /// <summary>Inspector overlay on every host that doesn't set Host.Inspector itself.</summary>
    public static bool Inspector { get; set; }

    /// <summary>Ctrl+Shift+this key toggles the inspector on a host. None turns the shortcut off.</summary>
    public static KeyCode InspectorKey { get; set; } = KeyCode.None;

    internal static readonly Color Poison = new(1f, 0f, 1f, 1f);
}

/// <summary>Per-host timings and counts for the inspector. Times are moving averages in ms.</summary>
internal sealed class HostStats
{
    public float RenderMs, LayoutMs, PaintMs;
    public int LayoutPasses, SkippedLayouts, ComponentRenders, Fibers;

    internal static void Average(ref float avg, double ms) => avg = avg <= 0f ? (float)ms : avg * 0.9f + (float)ms * 0.1f;
}
