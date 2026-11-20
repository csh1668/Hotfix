using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hotfix;

public sealed class Tool_CSharp(Func<IScriptEngine> engine) : Tool_Base("CSharp",
    """
    Runs C# code inside the running RimWorld process and returns what it printed and the value of its last expression. This is how you inspect and change the live game: definitions, maps, pawns, mods, Harmony patches, anything reachable from C#.

    How scripts work:
    - Write top-level statements, as in a C# script. End with an expression without a semicolon to return its value, for example: DefDatabase<ThingDef>.GetNamed("Steel").BaseMarketValue
    - Variables, functions and types you declare stay available to later CSharp calls in this chat, unless the script failed. They are lost when the game restarts.
    - Imported namespaces: System, System.Collections.Generic, System.Linq, System.Text, System.Reflection, UnityEngine, Verse, Verse.AI, RimWorld, RimWorld.Planet, HarmonyLib. Every loaded assembly, including mods, is referenced.
    - Private and internal members can be used directly.
    - Helpers: Print(value) writes a line. Dump(value, depth) prints the fields of an object. Decompile(typeof(X)) and Decompile("Namespace.Type:Method") return the C# source of game or mod code. Errors(count) returns the latest warnings and errors of the game log. Patches("Namespace.Type:Method") lists the Harmony patches on a method. Locate(def) or Locate("defName") tells which mod a def comes from, with that mod's folder and the XML file.
    - Any string has Grep(pattern, context, ignoreCase), which keeps only the lines that match a regular expression, numbered, with that many lines around each match: Decompile(typeof(Pawn)).Grep("Kill", 2). Use it instead of returning a large text such as a whole type.
    - Warnings and errors that the game logs while the script runs are added to the result.

    Limits:
    - The game is frozen while a script runs, and a script is stopped after 20 seconds. Keep scripts short and bound every loop.
    - Print only what you need. Filter and summarize in code instead of printing whole collections.
    - Read first. Change the game state only when the user asked for the change.
    """,
    """
    {
      "type": "object",
      "properties": {
        "code": { "type": "string", "description": "C# script to run" },
        "description": { "type": "string", "description": "A few words on what the script does, shown to the user" }
      },
      "required": ["code"]
    }
    """)
{
    // 시한 검사가 스크립트를 끝내거나, 깨운 스크립트가 끝나기를 기다려 주는 시간
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private const string Abandoned =
        "The script did not stop within the time limit. It was left running in the background and may still be changing the game. The script state of this chat was reset. Tell the user to save the game and restart it, and do not run more scripts until they have.";

    public TimeSpan Limit { get; init; } = TimeSpan.FromSeconds(20);

    public override bool NeedsApproval(JsonNode arguments, ToolContext context) => true;

    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var code = Required(arguments, "code");
        var state = context.State(() => engine().CreateState());
        // 컴파일은 메인 스레드 밖에서, 실행은 메인 스레드가 기다리는 동안 전용 스레드에서 한다
        var run = await Task.Run(() => state.Prepare(code), ct);
        return Execute(run, context);
    }

    // 스크립트가 어디에서 멈추든 메인 스레드는 제한 시간 뒤에 돌아온다
    private string Execute(IScriptRun run, ToolContext context)
    {
        string? output = null;
        Exception? failure = null;
        var limit = new ScriptLimit(Limit);
        var culture = Thread.CurrentThread.CurrentCulture;
        var thread = new Thread(() =>
        {
            Thread.CurrentThread.CurrentCulture = culture;
            try
            {
                output = run.Execute(limit);
            }
            catch (Exception e)
            {
                failure = e;
            }
        })
        { IsBackground = true, Name = "HotfixScript" };

        var main = Environment.CurrentManagedThreadId;
        var mark = GameLog.Mark();
        var console = Console.Out;
        bool finished;
        // 스크립트가 끝나지 않아도 되돌릴 수 있게, 바꿔 두는 것은 모두 기다리는 쪽에서 한다
        Console.SetOut(run.Output);
        ScriptGame.SetMainThread(thread.ManagedThreadId);
        try
        {
            thread.Start();
            finished = thread.Join(Limit + Grace);
            if (!finished)
            {
                // 스크립트 코드 밖에서 멈춘 경우. 대기 중이면 깨워 보고, 그래도 끝나지 않으면 남겨 둔다
                limit.Abandon();
                thread.Interrupt();
                finished = thread.Join(Grace);
            }
        }
        finally
        {
            ScriptGame.SetMainThread(main);
            Console.SetOut(console);
        }

        if (!finished)
        {
            context.Drop<IScriptState>();
            Logger.Warning("A script did not stop within its time limit and was left running. Save and restart the game.");
            throw new ScriptException(Abandoned);
        }

        var text = failure?.Message ?? output!;
        var log = GameLog.Since(mark);
        if (log.Length > 0)
            text += "\n\n[Game log during the script]\n" + log;
        if (failure is not null)
            throw new ScriptException(text);
        return text.Length > 0 ? text : "(no output)";
    }
}
