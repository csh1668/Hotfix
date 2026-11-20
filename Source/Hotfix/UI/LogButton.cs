using System.Linq;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

namespace Hotfix;

// 로그 창 맨 위 줄의 오른쪽 끝에 두는 버튼. 고른 로그나 최근 로그를 새 대화의 입력란에 넣는다
[StaticConstructorOnStartup]
internal static class LogButton
{
    private const int RecentCount = 5;
    private const float Size = 24f;
    private static readonly Texture2D Icon = LoadedModManager.GetMod<HotfixMod>().Content.ModMetaData.Icon;

    static LogButton()
    {
        HotfixMod.Harmony.Patch(AccessTools.Method(typeof(EditWindow_Log), nameof(EditWindow_Log.DoWindowContents)),
            postfix: new HarmonyMethod(typeof(LogButton), nameof(EditWindow_Log_DoWindowContents_Postfix)));
    }

    private static void EditWindow_Log_DoWindowContents_Postfix(Rect inRect)
    {
        var rect = new Rect(inRect.xMax - Size, inRect.y, Size, Size);
        if (Widgets.ButtonImage(rect, Icon))
        {
            var entries = EditWindow_Log.selectedMessage is { } selected
                ? [selected]
                : Log.Messages.Where(Shown).TakeLast(RecentCount);
            ChatRuntime.Sessions.New();

            var logs = string.Join("\n\n", entries.Select(x =>
            {
                var times = x.repeats > 1 ? $" ({x.repeats} times) " : " ";
                var trace = x.stackTrace is null ? "" : $"\n{x.stackTrace.TrimEnd()}";
                return $"[{x.type}]{times}{x.text}{trace}";
            }));
            ChatRuntime.Sessions.Current.Draft = logs;
            ChatOverlay.Show();
        }

        TooltipHandler.TipRegion(rect, "Hotfix it!");
    }

    private static bool Shown(LogMessage entry) => entry.type switch
    {
        LogMessageType.Message => EditWindow_Log.showMessages,
        LogMessageType.Warning => EditWindow_Log.showWarnings,
        _ => EditWindow_Log.showErrors,
    };
}
