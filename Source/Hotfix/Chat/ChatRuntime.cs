using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace Hotfix;

internal static class ChatRuntime
{
    private static readonly ChatStore Store = new(Path.Combine(GenFilePaths.SaveDataFolderPath, "Hotfix", "Chats"));
    public static readonly MemoryStore Memory = new(Path.Combine(GenFilePaths.SaveDataFolderPath, "Hotfix", "Memory"));
    private static readonly ToolPaths Paths = FindPaths();
    private static readonly Tool_Base[] Tools =
    [
        new Tool_Read(), new Tool_Glob(), new Tool_Grep(), new Tool_Edit(), new Tool_Write(), new Tool_Shell(),
        new Tool_CSharp(() => ScriptHost.Engine.Value), new Tool_SubMod(), new Tool_Build(() => ScriptHost.Engine.Value),
        new Tool_Snapshot(), new Tool_Screenshot(), new Tool_Memory(Memory), new Tool_Ask(),
    ];
    public static readonly ChatSessions Sessions = new(Store,
        () => new ChatSession(Store) { Tools = Tools, Paths = Paths, Memory = Memory });
    private static bool _warmed;

    public static void WarmUp()
    {
        if (_warmed)
            return;
        _warmed = true;
        Task.Run(() =>
        {
            try
            {
                ScriptHost.Engine.Value.CreateState().Prepare("1");
            }
            catch (Exception e)
            {
                Logger.Warning($"Failed to prepare the script engine: {e.Message}");
            }
        });
    }

    private static ToolPaths FindPaths()
    {
        var mod = LoadedModManager.GetMod<HotfixMod>().Content;
        var roots = new List<string> { Path.GetDirectoryName(Path.GetFullPath(GenFilePaths.ModsFolderPath))!, GenFilePaths.SaveDataFolderPath };
        if (ModLister.AllInstalledMods.FirstOrDefault(m => m.OnSteamWorkshop) is { } subscribed)
            roots.Add(subscribed.RootDir.Parent!.FullName);
        return new ToolPaths
        {
            ReadRoots = roots,
            ModRoot = mod.RootDir,
            SubModRoot = SubModManager.Root,
            ScratchRoot = Path.Combine(GenFilePaths.SaveDataFolderPath, "Hotfix", "Scratch"),
        };
    }
}
