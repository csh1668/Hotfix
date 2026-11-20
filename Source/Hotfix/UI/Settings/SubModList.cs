using System;
using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 모델이 만든 서브 모드. 여기서는 켜고 끄고 지우고 내용을 본다. 만들고 고치는 것은 채팅에서 한다
internal sealed record SubModList : UIComponent
{
    private static readonly WindowOptions DetailWindow = new() { Size = new Vector2(640f, 0f), AutoHeight = true };

    protected internal override Node Build()
    {
        var t = UseTheme();
        // 모달 창으로 보는 서브 모드의 id
        var viewing = UseState((string?)null);
        // 채팅에서 만들거나 여기서 바꾸면 폴더를 다시 읽는다
        var mods = UseMemo(() => SubModManager.List().Select(sub => (Sub: sub, Status: SubModManager.Status(sub))).ToList(),
            UseExternal(() => SubModManager.Version));
        var viewed = mods.FirstOrDefault(mod => mod.Sub.Id == viewing.Value).Sub;

        Node list = mods.Count == 0
            ? Empty("Hotfix_NoSubMods".Translate()) with { Icon = null }
            : new ScrollArea(Column(mods.Select(mod => SubModRow(mod.Sub, mod.Status))));

        return Column(
            Text("Hotfix_SubMods".Translate()),
            Column(list.Grow()).Bg(t.Card).Border(t.CardBorder).Grow(),
            viewed is not null
                ? Dialog(true, _ => Close(), new SubModDetail(viewed, Close), DetailWindow with { Title = Title(viewed) })
                : null
        ).Gap(t.Space(2));

        Node? SubModRow(SubMod sub, SubModStatus status) => new Item(Markdown.Literal(Title(sub)))
        {
            Description = Markdown.Literal(sub.Description),
            Actions = Row(
                Badge(("Hotfix_SubModStatus_" + status).Translate(), status switch
                {
                    SubModStatus.On => BadgeVariant.Success,
                    SubModStatus.Restart => BadgeVariant.Warning,
                    SubModStatus.Failed => BadgeVariant.Destructive,
                    SubModStatus.Missing or SubModStatus.Incompatible => BadgeVariant.Outline,
                    _ => BadgeVariant.Secondary,
                }),
                Switch(sub.Enabled, value => Try(() => SubModManager.SetEnabled(sub, value), sub)),
                IconButton(Glyph.Eye, () => viewing.Set(sub.Id), "Hotfix_ViewSubMod".Translate()),
                IconButton(Glyph.Trash, () => Try(() => SubModManager.Delete(sub), sub), "Hotfix_Delete".Translate())
            ).Gap(t.Space(1)).Align(Align.Center),
        }.Key(sub.Id);

        void Close() => viewing.Set((string?)null);
    }

    private static string Title(SubMod sub) => sub.Name.Length > 0 ? sub.Name : sub.Id;

    // 패치가 걸리지 않거나 파일이 잠겨 있으면 실패할 수 있다
    private static void Try(Action action, SubMod sub)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to change the sub-mod {sub.Id}: {e}");
        }
    }
}
