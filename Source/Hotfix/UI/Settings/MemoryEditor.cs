using System;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record MemoryEditor(MemoryStore Store, MemoryEntry Memory, Action OnClose) : UIComponent
{
    private const float LabelWidth = 110f;
    private const float TextHeight = 220f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var store = Store;
        var memory = Memory;
        var close = OnClose;
        var description = UseState(memory.Description);
        var content = UseState(memory.Content);

        return Column(
            FieldRow("Hotfix_Description".Translate(), Input(description.Value, description.Set)) with { LabelWidth = LabelWidth },
            FieldRow("Hotfix_Content".Translate(), Textarea(content.Value, content.Set, TextHeight)) with { LabelWidth = LabelWidth },
            DialogFooter(
                Button("CancelButton".Translate(), close) with { IsCancel = true },
                Button("Confirm".Translate(), () =>
                {
                    store.Save(memory with { Description = description.Value, Content = content.Value });
                    close();
                }, ButtonVariant.Primary) with
                { IsDefault = true })
        ).Gap(t.Space(2));
    }
}
