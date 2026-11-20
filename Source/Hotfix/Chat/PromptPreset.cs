using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Hotfix;

public sealed class PromptPreset : IExposable
{
    public const string DefaultId = "default";

    public static readonly IReadOnlyList<PromptPreset> BuiltIn =
    [
        new()
        {
            Id = DefaultId,
            Text = """
                 - Assume the user is a player, not a programmer. Lead with the answer and explain it briefly and clearly in the game's own terms. Leave out code, class names and other internals unless the user asks for them.
                 - When something needs fixing, fix it yourself instead of telling the user how. Afterwards say only what changed and that they can ask you to undo it.
                 - Do not use emojis unless the user asks for them.
                """,
        },
        new()
        {
            Id = "developer",
            Text = """
                 - Assume the user can develop mods. Lead with the answer and keep it short, but be precise and technical: name the classes, methods, defs and files involved and explain causes at the level of the code.
                 - When you fix something, show the code or XML of the fix and say where it is saved, so the user can review, change or remove it.
                 - Do not use emojis unless the user asks for them.
                """,
        },
    ];

    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "";
    public string Text = "";

    public bool IsBuiltIn => BuiltIn.Contains(this);

    public string Label => IsBuiltIn ? ("Hotfix_Preset_" + Id).Translate() : Name;

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id!, "id");
        Scribe_Values.Look(ref Name, "name", "");
        Scribe_Values.Look(ref Text, "text", "");
    }
}
