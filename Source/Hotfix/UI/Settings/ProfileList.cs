using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record ProfileList(HotfixSettings Settings) : UIComponent
{
    private static readonly WindowOptions EditorWindow = new() { Size = new Vector2(560f, 0f), AutoHeight = true };

    protected internal override Node Build()
    {
        var t = UseTheme();
        var settings = Settings;
        // 모달 창에서 고치는 프로필. 목록에 없는 것이면 확인할 때 추가된다
        var editing = UseState((LlmProfile?)null);
        UseExternal(() => settings.Profiles.Count);

        Node list = settings.Profiles.Count == 0
            ? Empty("Hotfix_NoProfiles".Translate()) with { Icon = null }
            : new ScrollArea(Column(settings.Profiles.Select(Node (profile) =>
                new ProfileRow(settings, profile, editing.Set).Key(profile.Id))));

        return Column(
            Row(
                Text("Hotfix_Profiles".Translate()).Grow(),
                IconButton(Glyph.Plus, Add, "Hotfix_AddProfile".Translate())
            ).Align(Align.Center),
            Column(list.Grow()).Bg(t.Card).Border(t.CardBorder).Grow(),
            editing.Value is { } target
                ? Dialog(true, _ => Close(), new ProfileEditor(settings, target, Close), EditorWindow with
                {
                    Title = (settings.Profiles.Contains(target) ? "Hotfix_EditProfile" : "Hotfix_AddProfile").Translate(),
                })
                : null
        ).Gap(t.Space(2));

        void Close() => editing.Set((LlmProfile?)null);

        void Add()
        {
            var number = 1;
            while (settings.Profiles.Any(p => p.Name == "Hotfix_ProfileName".Translate(number)))
                number++;
            editing.Set(new LlmProfile { Name = "Hotfix_ProfileName".Translate(number) });
        }
    }
}
