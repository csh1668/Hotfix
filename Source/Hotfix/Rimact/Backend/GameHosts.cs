using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Rimact;

/// <summary>Draws Rimact trees from IMGUI methods a mod already overrides. No base class, no host to keep.</summary>
/// <remarks>State lives as long as the owner: the mod settings page, the inspect tab or the window. Outside windows, the key.</remarks>
internal static class RimactUI
{
    // Inspect tabs draw in this immediate window (InspectTabBase.DoTabGUI). Negated, as the window stack stores it
    private const int TabWindowId = -235086;

    private static readonly HostRegistry s_hosts = new(static root => new RimactHost(root), static message => Log.Error(message));
    private static readonly object s_screen = new();
    private static readonly Dictionary<int, object> s_immediate = new();
    private static int s_passes;
    private static bool s_counting;

    /// <summary>Host is kept per call site (file and line). A new root is applied only when it differs by value.</summary>
    /// <param name="key">Needed when one line draws several trees, e.g. in a loop, and outside windows (gizmos).</param>
    /// <example><code>public override void DoSettingsWindowContents(Rect inRect) =&gt; RimactUI.Draw(inRect, new SettingsPage(settings));</code></example>
    public static void Draw(Rect rect, Node root, string? key = null,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        var e = Event.current;
        if (e is null) return;
        var window = Find.WindowStack?.currentlyDrawnWindow;
        object? era = Current.Game;
        HostRegistry.Scope scope;
        switch (window)
        {
            case null:
                if (key is null) s_hosts.Report(file, line, null, "draws outside any window; pass a key that names what it shows, such as the thing's ID");
                scope = s_hosts.ScopeFor(s_screen, forgets: true);
                break;
            case Dialog_ModSettings:
                // New dialog on every open, so scope by type. Each mod has its own Rimact copy, so it's this mod's page
                scope = s_hosts.ScopeFor(typeof(Dialog_ModSettings), CloseWindow);
                era = null;
                break;
            case ImmediateWindow { ID: TabWindowId } when OpenTab(Pane()) is { } tab:
                scope = s_hosts.ScopeFor(tab, static (owner, _) => () => CloseTab((InspectTabBase)owner));
                break;
            case ImmediateWindow immediate:
                // Recreated when a frame skips it, but keeps its ID
                if (!s_immediate.TryGetValue(immediate.ID, out var owner)) s_immediate[immediate.ID] = owner = new object();
                scope = s_hosts.ScopeFor(owner);
                break;
            default:
                scope = s_hosts.ScopeFor(window, CloseWindow);
                break;
        }
        scope.Window = window;
        s_hosts.Get(scope, root, key, new CallSite(file, member, line), Pass(e), era, Time.frameCount).Draw(rect);
    }

    private static long Pass(Event e)
    {
        if (!s_counting)
        {
            s_counting = true;
            var counter = new GameObject("Rimact");
            UnityEngine.Object.DontDestroyOnLoad(counter);
            counter.AddComponent<PassCounter>();
        }

        int type = e.rawType switch { EventType.Repaint => 1, EventType.Layout => 2, _ => 0 };
        return ((long)s_passes << 2) | (long)type;
    }

    private sealed class PassCounter : MonoBehaviour
    {
        private void OnGUI() => s_passes++;
    }

    /// <summary>Content area of an inspect tab of this size: 10px in, clear of the close button.</summary>
    public static Rect TabRect(Vector2 size) => new(10f, 26f, size.x - 20f, size.y - 36f);

    private static readonly Func<object, HostRegistry.Scope, Action?> CloseWindow = static (_, scope) => () => scope.Window?.Close();

    private static IInspectPane? Pane() =>
        WorldRendererUtility.WorldSelected ? Find.World?.UI.inspectPane : MainButtonDefOf.Inspect?.TabWindow as IInspectPane;

    private static InspectTabBase? OpenTab(IInspectPane? pane)
    {
        if (pane?.OpenTabType is not { } type || pane.CurTabs is not { } tabs) return null;
        foreach (var tab in tabs)
            if (tab.GetType() == type) return tab;
        return null;
    }

    // Like the tab's close button, if it is still open
    private static void CloseTab(InspectTabBase tab)
    {
        var pane = Pane();
        if (OpenTab(pane) != tab) return;
        pane!.CloseOpenTab();
        SoundDefOf.TabClose.PlayOneShotOnCamera();
    }
}

/// <summary>Gizmo whose whole face is a Rimact tree. Return it from GetGizmos.</summary>
/// <remarks>Gizmos are new every frame, so state is kept by Key. Hosts undrawn for a few seconds are disposed.</remarks>
/// <example><code>yield return new RimactGizmo("power:" + parent.ThingID, new PowerDial(this), width: 160f);</code></example>
internal sealed class RimactGizmo : Gizmo
{
    public RimactGizmo(string key, Node root, float width = 140f)
    {
        Key = key;
        Root = root;
        Width = width;
    }

    public string Key { get; }
    public Node Root { get; }
    public float Width { get; }

    public override float GetWidth(float maxWidth) => Mathf.Min(Width, maxWidth);

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), Height);
        Widgets.DrawWindowBackground(rect);
        RimactUI.Draw(rect.ContractedBy(6f), Root, Key);
        return new GizmoResult(Mouse.IsOver(rect) ? GizmoState.Mouseover : GizmoState.Clear);
    }
}
