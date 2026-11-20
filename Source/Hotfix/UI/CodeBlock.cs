using Rimact;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record CodeBlock(string Language, string Code) : UIComponent
{
    // 긴 코드는 앞쪽 몇 줄만 보여 준다
    public bool Clip { get; init; }

    protected internal override Node Build()
    {
        var t = UseTheme();
        var open = UseState(false);
        var cut = UseMemo(() => Clip ? Clipped.Cut(Code) : null, Clip, Code);
        var shown = cut is null || open.Value ? Code : cut.Value.Shown;
        var painted = UseMemo(() => CodeHighlight.Paint(Language, shown.Replace("\t", "    ")), Language, shown);
        return Column(
            Row(
                Text(Language).TextSize(t.TextCaption).Color(t.MutedForeground).Grow(),
                Markdown.CopyButton(Code)
            ).Align(Align.Center),
            Text(painted).TextSize(t.TextCaption + 1f).Wrap()
                .Anchor(TextAlign.UpperLeft),
            cut is null ? null : Clipped.Toggle(open, cut.Value.Hidden)
        ).Padding(new Edges(t.Space(2), 0f, t.Space(1), t.Space(2))).Bg(t.Sunken).Border(t.Border);
    }
}
