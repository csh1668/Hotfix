using System;
using System.Linq;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record TaskModelList(HotfixSettings Settings) : UIComponent
{
    private const float PickerWidth = 220f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var settings = Settings;
        // 프로필의 이름이나 지정이 바뀌면 다시 그린다
        UseExternal(() => string.Join('|', settings.Profiles.Select(p => $"{p.Id} {p.Name}")) + string.Join('|', settings.TaskModels));

        return Column(
            Text("Hotfix_TaskModels".Translate()),
            Column(new ScrollArea(Column(Enum.GetValues<LlmTask>().Select(TaskRow))).Grow())
                .Bg(t.Card).Border(t.CardBorder).Grow()
        ).Gap(t.Space(2));

        Node TaskRow(LlmTask task)
        {
            var options = settings.Profiles.Select(p => ((string?)p.Id, p.Name));
            // 메인이 아닌 작업은 지정하지 않으면 메인을 따른다
            if (task != LlmTask.Main)
                options = options.Prepend((null, "Hotfix_SameAsMain".Translate()));
            var selected = task == LlmTask.Main ? settings.MainProfile : settings.TaskProfile(task);

            return Item(("Hotfix_Task_" + task).Translate(), ("Hotfix_TaskDesc_" + task).Translate(),
                actions: Select(selected?.Id, options, id =>
                {
                    if (id is null)
                        settings.TaskModels.Remove(task);
                    else
                        settings.TaskModels[task] = id;
                }).Width(PickerWidth).Disabled(settings.Profiles.Count == 0)).Key(task);
        }
    }
}
