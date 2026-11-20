using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Snapshot() : Tool_Base("Snapshot",
    """
    Returns what the game is drawing on the screen right now as a compact tree of text: every open window with its texts and controls, and the screen outside windows, such as labels over the map, alerts and the bottom bar. Use it to read a dialog, an info card, a mod's settings window or an error message exactly as the user sees it.
    Lines are in reading order, and texts on the same row are joined with " | ". Controls carry a ref like [ref=e12] that stays the same in later snapshots.
    After the first call in a chat, the result lists only the lines that changed since the previous snapshot. Pass full=true to get the whole screen again.
    Buttons that show only an icon are not listed. For anything that is not on the screen, use CSharp.
    """,
    """
    {
      "type": "object",
      "properties": {
        "full": { "type": "boolean", "description": "Return the whole screen instead of the changes since the last snapshot" }
      }
    }
    """)
{
    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var state = context.State(() => new UiSnapshotState());
        var snapshot = UiSnapshot.Build(await UiRecorder.Capture(), state);
        var previous = state.Last;
        state.Last = snapshot;
        return previous is null || arguments.GetBool("full") ? snapshot : UiSnapshot.Diff(previous, snapshot);
    }
}
