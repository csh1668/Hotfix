using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 기본 제공 프리셋과 사용자가 만든 프리셋. 행을 누르면 그 프리셋을 쓴다
internal sealed record PresetList(HotfixSettings Settings) : UIComponent
{
    private static readonly WindowOptions EditorWindow = new() { Size = new Vector2(560f, 0f), AutoHeight = true };

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var settings = Settings;
        // 모달 창에서 고치는 프리셋. 목록에 없는 것이면 확인할 때 추가된다
        var editing = UseState((PromptPreset?)null);
        var selected = UseExternal(() => (settings.Preset.Id, settings.Presets.Count)).Id;

        return Column(
            Row(
                Text("Hotfix_Presets".Translate()).Grow(),
                IconButton(Glyph.Plus, Add, "Hotfix_AddPreset".Translate())
            ).Align(Align.Center),
            Column(new ScrollArea(Column(PromptPreset.BuiltIn.Concat(settings.Presets).Select(PresetRow))).Grow())
                .Bg(t.Card).Border(t.CardBorder).Grow(),
            editing.Value is { } target
                ? Dialog(true, _ => Close(), new PresetEditor(settings, target, Close), EditorWindow with
                {
                    Title = (target.IsBuiltIn ? "Hotfix_ViewPreset"
                        : settings.Presets.Contains(target) ? "Hotfix_EditPreset" : "Hotfix_AddPreset").Translate(),
                })
                : null
        ).Gap(t.Space(2));

        Node? PresetRow(PromptPreset preset)
        {
            var builtIn = preset.IsBuiltIn;
            return new Item(Markdown.Literal(preset.Label))
            {
                // 사용자가 만든 것은 글의 첫 줄
                Description = builtIn
                    ? ("Hotfix_PresetDesc_" + preset.Id).Translate().ToString()
                    : Markdown.Literal(preset.Text.TrimStart().Split('\n', 2)[0]),
                Selected = preset.Id == selected,
                OnClick = () =>
                {
                    if (preset.Id == selected)
                        return;
                    backend.PlaySound(UiSound.Tick);
                    settings.PresetId = preset.Id;
                },
                Actions = Row(
                    // 기본 제공 프리셋은 고칠 수 없으므로 내용만 보여 준다
                    builtIn
                        ? IconButton(Glyph.Eye, () => editing.Set(preset), "Hotfix_ViewPreset".Translate())
                        : IconButton(Glyph.Pencil, () => editing.Set(preset), "Hotfix_EditPreset".Translate()),
                    IconButton(Glyph.Copy, () => Duplicate(preset), "Hotfix_Duplicate".Translate()),
                    builtIn ? null : IconButton(Glyph.Trash, () => settings.Presets.Remove(preset), "Hotfix_Delete".Translate())
                ).Gap(t.Space(1)).Align(Align.Center),
            }.Key(preset.Id);
        }

        void Close() => editing.Set((PromptPreset?)null);

        void Add()
        {
            var number = 1;
            while (settings.Presets.Any(p => p.Name == "Hotfix_PresetName".Translate(number)))
                number++;
            editing.Set(new PromptPreset { Name = "Hotfix_PresetName".Translate(number) });
        }

        void Duplicate(PromptPreset preset)
        {
            var copy = new PromptPreset { Name = preset.Label + " (2)", Text = preset.Text };
            settings.Presets.Add(copy);
            editing.Set(copy);
        }
    }
}
