using System;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 모달 창의 내용. 확인을 누르면 프리셋에 적용한다. 기본 제공 프리셋은 보기만 한다
internal sealed record PresetEditor(HotfixSettings Settings, PromptPreset Preset, Action OnClose) : UIComponent
{
    private const float LabelWidth = 110f;
    private const float TextHeight = 280f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var settings = Settings;
        var preset = Preset;
        var close = OnClose;
        var readOnly = preset.IsBuiltIn;
        var name = UseState(preset.Label);
        var text = UseState(preset.Text);

        return Column(
            FieldRow("Hotfix_Name".Translate(), Input(name.Value, readOnly ? null : name.Set)) with { LabelWidth = LabelWidth },
            FieldRow("Hotfix_Content".Translate(), Textarea(text.Value, readOnly ? null : text.Set, TextHeight)) with { LabelWidth = LabelWidth },
            readOnly
                ? DialogFooter(Button("CloseButton".Translate(), close) with { IsCancel = true })
                : DialogFooter(
                    Button("CancelButton".Translate(), close) with { IsCancel = true },
                    Button("Confirm".Translate(), () =>
                    {
                        preset.Name = name.Value.Trim();
                        preset.Text = text.Value;
                        if (!settings.Presets.Contains(preset))
                            settings.Presets.Add(preset);
                        close();
                    }, ButtonVariant.Primary) with
                    { IsDefault = true })
        ).Gap(t.Space(2));
    }
}
