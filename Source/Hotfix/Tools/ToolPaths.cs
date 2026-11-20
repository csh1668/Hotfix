using System.Collections.Generic;

namespace Hotfix;

public sealed class ToolPaths
{
    public required IReadOnlyList<string> ReadRoots { get; init; }
    public required string ModRoot { get; init; }
    public required string SubModRoot { get; init; }
    public required string ScratchRoot { get; init; }
}
