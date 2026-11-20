using HarmonyLib;
using Rimact;
using UnityEngine;
using Verse;

namespace Hotfix;

public class HotfixMod : Mod
{
    public static HotfixSettings Settings { get; private set; } = null!;
    public static string RootDir { get; private set; } = null!;
    public static Harmony Harmony { get; } = new("seohyeon.hotfix");

    public HotfixMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<HotfixSettings>();
        RootDir = content.RootDir;
        SubModStartup.Run(content);
    }

    public override string SettingsCategory() => "Hotfix";

    public override void DoSettingsWindowContents(Rect inRect) => RimactUI.Draw(inRect, new SettingsUI(Settings));
}
