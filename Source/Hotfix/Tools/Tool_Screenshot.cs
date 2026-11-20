using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_Screenshot() : Tool_Base("Screenshot",
    """
    Takes a picture of the game screen as the user sees it and returns the image. The chat overlay is hidden for the capture.
    Use it for what text cannot tell: how something looks, where things are, broken textures or layouts. To read text and controls, Snapshot is cheaper and exact.
    """,
    """
    {
      "type": "object",
      "properties": {}
    }
    """)
{
    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        context.Attachments.Add(await GameScreen.Capture(Path.Combine(context.Scratch, "screenshots")));
        return "The screenshot follows.";
    }
}
