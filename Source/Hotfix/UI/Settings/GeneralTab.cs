using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record GeneralTab(HotfixSettings Settings) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        return Column(
            Row(Text("Hotfix_ResponseLanguage".Translate()).Grow(), new LanguagePicker(Settings)).Align(Align.Center)
        ).Gap(t.Space(2));
    }
}

// Select와 같은 모양. 목록은 게임에 있는 언어들이고, 맨 아래 칸에 직접 적을 수도 있다
internal sealed record LanguagePicker(HotfixSettings Settings) : UIComponent
{
    private const string CustomName = "hotfix.settings.language";
    private const float Width = 220f;
    private const float RowHeight = 26f;
    private const int VisibleRows = 9;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var settings = Settings;
        var open = UseState(false);
        var value = UseExternal(() => settings.ResponseLanguage);
        // 값은 영어 이름
        var languages = UseMemo(() => LanguageDatabase.AllLoadedLanguages.Select(loaded =>
        {
            var name = loaded.FriendlyNameEnglish;
            var native = loaded.FriendlyNameNative;
            return (Name: name, Label: native == name ? name : $"{name} ({native})");
        }).ToList());
        var listed = languages.Any(language => language.Name == value);
        UseKeyDown(key =>
        {
            if (key.Key is not (KeyCode.Return or KeyCode.KeypadEnter) || backend.FocusedControl != CustomName)
                return false;
            open.Set(false);
            return true;
        }, beforeTextFields: true);

        var (normal, hover, pressed) = Rimact.Components.Button.Styles(t, ButtonVariant.Default);
        var shown = value.Length == 0
            ? "Hotfix_LanguageOfUser".Translate().ToString()
            : languages.FirstOrDefault(language => language.Name == value).Label ?? value;
        var trigger = Row(
                Text(shown)
                    .Truncate().Grow().Shrink().Color(value.Length == 0 ? t.MutedForeground : t.Foreground),
                new Chevron(true) { Size = 12f })
            .Height(t.ControlHeight).Padding(t.Space(3), 0).Gap(t.Space(1.5f)).Align(Align.Center)
            .Visual(normal, hover, pressed);

        var panel = Column(
            new ScrollArea(Column(languages.Select(language => LanguageRow(language.Name, language.Label))))
                .Height(Mathf.Min(languages.Count, VisibleRows) * RowHeight),
            Input(listed ? "" : value, text => settings.ResponseLanguage = text, "Hotfix_CustomLanguage".Translate()) with { ControlName = CustomName }
        ).Gap(t.Space(2));

        return new Popover(trigger, panel)
        {
            Open = open.Value,
            OnOpenChange = on =>
            {
                if (on)
                    backend.PlaySound(UiSound.MenuOpen);
                open.Set(on);
            },
            Width = Width,
        }.Width(Width);

        Node? LanguageRow(string name, string label)
        {
            var on = name == value;
            return Row(Text(label).Truncate().Grow().Shrink())
                .Height(RowHeight).Padding(t.Space(2), 0).Align(Align.Center)
                .Visual(new Visual { Fg = on ? t.Indicator : t.Foreground, Overlay = on ? t.SelectedOverlay : null },
                    new Visual { Overlay = t.HoverOverlay })
                .OnClick(() =>
                {
                    backend.PlaySound(UiSound.Click);
                    settings.ResponseLanguage = name;
                    open.Set(false);
                }).Key(name);
        }
    }
}
