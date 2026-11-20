using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Hotfix;

internal static class GameView
{
    private const int MaxListed = 5;

    public static string Describe()
    {
        var lines = new List<string>();
        if (LongEventHandler.ShouldWaitForEvent)
        {
            lines.Add("Screen: loading");
        }
        else if (Current.ProgramState != ProgramState.Playing)
        {
            lines.Add("Screen: main menu");
        }
        else
        {
            var speed = Find.TickManager.Paused ? "paused" : "speed " + Find.TickManager.CurTimeSpeed;
            IEnumerable<object> selected = Find.Selector.SelectedObjectsListForReading;
            if (WorldRendererUtility.WorldSelected || Find.CurrentMap is not { } map)
            {
                lines.Add($"Screen: world map, {speed}");
                selected = Find.WorldSelector.SelectedObjects;
            }
            else
            {
                lines.Add($"Screen: map of {map.Parent?.LabelCap ?? "unknown"} ({map.Size.x}x{map.Size.z}), {speed}");
                var view = Find.CameraDriver.CurrentViewRect;
                lines.Add($"Camera: cells x {view.minX}..{view.maxX}, z {view.minZ}..{view.maxZ}");
                var cell = UI.MouseCell();
                if (cell.InBounds(map) && !Find.WindowStack.MouseObscuredNow)
                    lines.Add($"Under the mouse: cell ({cell.x}, {cell.z}): {List(cell.GetThingList(map).Select(Name))}");
            }
            if (selected.Any())
                lines.Add("Selected: " + List(selected.Select(Name)));
        }

        var windows = Find.WindowStack?.Windows.Where(window => window is not ImmediateWindow && !ChatOverlay.Owns(window))
            .Select(window => string.IsNullOrEmpty(window.optionalTitle) ? window.GetType().Name : $"{window.GetType().Name} \"{window.optionalTitle}\"")
            .ToList();
        if (windows is { Count: > 0 })
            lines.Add("Open windows: " + List(windows));
        return string.Join("\n", lines);
    }

    private static string Name(object selected) => selected switch
    {
        Thing thing => $"{thing.LabelCap.StripTags()} ({thing.def.defName}, id {thing.ThingID}, at {thing.Position.x}, {thing.Position.z})",
        WorldObject worldObject => $"{worldObject.LabelCap.StripTags()} ({worldObject.def.defName}, tile {worldObject.Tile})",
        _ => selected.ToString() ?? "",
    };

    private static string List(IEnumerable<string> names)
    {
        var all = names.ToList();
        if (all.Count == 0)
            return "nothing";
        return string.Join("; ", all.Take(MaxListed)) + (all.Count > MaxListed ? $"; and {all.Count - MaxListed} more" : "");
    }
}
