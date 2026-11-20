using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityStubs;
using Verse;

namespace Hotfix;

internal static class SubModStartup
{
    private static readonly string Marker = Path.Combine(SubModManager.Root, "starting");

    private static bool _retried;
    private static bool _downgraded;
    private static bool _finished;

    public static Dictionary<string, string> Content { get; } = [];

    public static bool Skipped { get; private set; }

    public static void Run(ModContentPack pack)
    {
        try
        {
            // 서브 모드의 컨텐츠가 다른 일반 모드들보다 뒤에 오도록 이 모드를 실행 목록의 맨 뒤로 옮긴다
            LoadedModManager.runningMods.Remove(pack);
            LoadedModManager.runningMods.Add(pack);
            pack.loadOrder = int.MaxValue >> 1;
            
            // Enable되어있고, 모든 요구 모드가 다 켜져있고, 지금 게임 버전을 지원하는 모드들에 대해서..
            var mods = SubModManager.List()
                .Where(sub => sub.Enabled && SubModManager.MissingRequires(sub).Count == 0 && SubModManager.Supported(sub)).ToList();
            if (File.Exists(Marker) && File.ReadAllText(Marker) != Environment.ProcessId.ToString())
            {
                Skipped = mods.Count > 0;
                File.Delete(Marker);
                return;
            }
            if (mods.Count == 0)
                return;
            
            File.WriteAllText(Marker, Environment.ProcessId.ToString());
            foreach (var sub in mods)
                Load(pack, sub);
            ModContentLoader<Texture2D>.PrefetchPaths(pack);
            ModContentLoader<AudioClip>.PrefetchPaths(pack);
            var harmony = HotfixMod.Harmony;
            harmony.Patch(AccessTools.Method(typeof(ModsConfig), nameof(ModsConfig.Reset)),
                prefix: new HarmonyMethod(typeof(SubModStartup), nameof(ModsConfig_Reset_Prefix)));
            harmony.Patch(AccessTools.Method(typeof(PlayDataLoader), nameof(PlayDataLoader.LoadAllPlayData)),
                prefix: new HarmonyMethod(typeof(SubModStartup), nameof(PlayDataLoader_LoadAllPlayData_Prefix)));
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load the sub-mods: {e}");
        }
    }

    public static void Finished()
    {
        _finished = true;
        if (File.Exists(Marker))
            File.Delete(Marker);
        
        var backup = Path.Combine(GenFilePaths.SaveDataFolderPath, "Hotfix", "ModsConfig.backup.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(GenFilePaths.ModsConfigFilePath, backup, true);
        if (_retried)
        {
            var reason = "Hotfix_SubModLoadFailed".Translate(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).ToString();
            foreach (var sub in SubModManager.List().Where(sub => Content.ContainsKey(sub.Id)))
            {
                sub.Enabled = false;
                sub.DisabledReason = reason;
                SubModManager.Save(sub);
            }
            Content.Clear();
            DelayedErrorWindowRequest.Add("Hotfix_SubModsFailed".Translate(), "Hotfix");
        }
        else if (Skipped)
        {
            DelayedErrorWindowRequest.Add("Hotfix_SubModsSkipped".Translate(), "Hotfix");
        }
    }

    private static void Load(ModContentPack pack, SubMod sub)
    {
        try
        {
            // 코드를 적용하지 못한 서브 모드는 XML도 읽지 않음
            var dll = SubModManager.Dll(sub.Id);
            if (File.Exists(dll))
                SubModCode.Apply(sub.Id, File.ReadAllBytes(dll));
            else
                SubModCode.LoadLibraries(sub.Id);
            foreach (var folder in SubModManager.LoadFolders(sub.Id))
            {
                pack.foldersToLoadDescendingOrder.Add(folder);
                ContentPacks.MountLoadFolder(folder);
            }
            Content[sub.Id] = SubModManager.Stamp(sub.Id);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load the sub-mod {sub.Id}: {e}");
        }
    }

    private static bool ModsConfig_Reset_Prefix()
    {
        if (_finished || _retried || Content.Count == 0)
            return true;
        _retried = true;
        try
        {
            foreach (var id in Content.Keys)
                SubModCode.Remove(id);
            return false;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to unload the sub-mods: {e}");
            return true;
        }
    }

    private static void PlayDataLoader_LoadAllPlayData_Prefix(ref bool recovering)
    {
        if (!recovering || !_retried || _downgraded)
            return;
        _downgraded = true;
        recovering = false;
    }
}

[StaticConstructorOnStartup]
internal static class SubModStartupEnd
{
    static SubModStartupEnd() => LongEventHandler.ExecuteWhenFinished(SubModStartup.Finished);
}
