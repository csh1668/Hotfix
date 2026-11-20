using System.Globalization;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 쓰는 프로필과 누적 토큰 사용량
internal sealed record StatusBar(ChatSession Session) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var session = Session;
        var (name, model, usage, cost) = UseExternal(() => (session.Profile?.Name, session.Profile?.Model, session.Usage, session.Cost));
        var mode = UseExternal(() => HotfixMod.Settings.PermissionMode);
        // 오른쪽 아래 모서리는 창 크기 조절 손잡이 자리
        return Row(
            // 누를 때마다 Read, Auto, Bypass 순으로 바뀐다
            Button(("Hotfix_Mode_" + mode).Translate(), () => HotfixMod.Settings.PermissionMode = (PermissionMode)(((int)mode + 1) % 3),
                ButtonVariant.Ghost, ButtonSize.Sm).Tooltip("Hotfix_ModeTip".Translate()),
            Caption(name is null ? "" : string.IsNullOrEmpty(model) ? name : $"{name} · {model}").Grow().Shrink(),
            usage is null ? null : Caption("Hotfix_ChatUsage".Translate(
                (usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens).ToString("N0"),
                usage.OutputTokens.ToString("N0"))
                // 가격을 적어 둔 프로필을 썼을 때만 비용을 덧붙인다
                + (cost > 0 ? string.Create(CultureInfo.InvariantCulture, $" (${cost:0.00##})") : ""))
        ).Height(24f).Gap(t.Space(2)).Padding(new Edges(t.Space(3), 0f, 28f, 0f)).Align(Align.Center);

        TextNode Caption(string text) => Text(text).TextSize(t.TextCaption).Color(t.MutedForeground).Truncate();
    }
}
