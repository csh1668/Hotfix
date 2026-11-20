using Rimact;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record SettingsUI(HotfixSettings Settings) : UIComponent
{
    protected internal override Node Build() =>
        Tabs(
            Tab("general", "Hotfix_General".Translate(), new GeneralTab(Settings)) with { Icon = Glyph.Settings },
            Tab("models", "Hotfix_Models".Translate(), new ModelsTab(Settings)) with { Icon = Glyph.Cube },
            Tab("prompts", "Hotfix_Prompts".Translate(), new PromptsTab(Settings)) with { Icon = Glyph.Cpu },
            Tab("submods", "Hotfix_SubMods".Translate(), new SubModList()) with { Icon = Glyph.Puzzle }
        ) with
        { Vertical = true };
}

// 위는 프로필 목록, 아래는 작업마다 쓸 프로필. 높이를 반씩 나눈다
internal sealed record ModelsTab(HotfixSettings Settings) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        return Column(
            new ProfileList(Settings).FillHeight(),
            new TaskModelList(Settings).FillHeight()
        ).Gap(t.Space(3));
    }
}

// 위는 응답 방식 프리셋, 아래는 메모리
internal sealed record PromptsTab(HotfixSettings Settings) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        return Column(
            new PresetList(Settings).FillHeight(),
            new MemoryList(Settings, ChatRuntime.Memory).FillHeight()
        ).Gap(t.Space(3));
    }
}
