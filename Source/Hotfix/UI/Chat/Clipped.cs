using System;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 긴 글은 몇 줄만 보여 주고 아래 버튼으로 편다. Render는 보여 줄 글로 본문을 만든다
internal sealed record Clipped(string Text, Func<string, Node> Render) : UIComponent
{
    private const int Lines = 12;
    private const int Chars = 1500;

    // 끝쪽을 남긴다. 오류는 원인이 끝에 있다
    public bool Tail { get; init; }

    protected internal override Node Build()
    {
        var open = UseState(false);
        var cut = UseMemo(() => Cut(Text, Tail), Text, Tail);
        return cut is null ? Render(Text) : Column(Render(open.Value ? Text : cut.Value.Shown), Toggle(open, cut.Value.Hidden));
    }

    // 자를 필요가 없으면 null. Hidden은 가려진 줄 수
    public static (string Shown, int Hidden)? Cut(string text, bool tail = false)
    {
        var lines = text.TrimEnd().Split('\n');
        // 조금 넘는 정도면 그냥 다 보여 준다
        if (lines.Length <= Lines + 3 && text.Length <= Chars + 500)
            return null;
        var kept = lines.Length <= Lines ? lines : tail ? lines[^Lines..] : lines[..Lines];
        var shown = string.Join("\n", kept);
        if (shown.Length > Chars)
            shown = tail ? shown[^Chars..] : shown[..Chars];
        return (shown, lines.Length - kept.Length);
    }

    public static Node Toggle(State<bool> open, int hidden) => Button(
        open.Value ? "Hotfix_ChatLess".Translate()
        : hidden > 0 ? "Hotfix_ChatMoreLines".Translate(hidden.ToString("N0"))
        : "Hotfix_ChatMore".Translate(),
        () => open.Set(value => !value), ButtonVariant.Ghost, ButtonSize.Sm).AlignSelf(Align.Start);
}
