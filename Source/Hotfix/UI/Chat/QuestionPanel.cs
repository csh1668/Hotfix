using System;
using System.Collections.Generic;
using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 모델이 물은 질문이 있는 동안 입력란 위에 뜬다
internal sealed record QuestionPanel(ChatSession Session) : UIComponent
{
    protected internal override Node? Build()
    {
        var session = Session;
        var question = UseExternal(() => session.Question);
        // 질문이 바뀌면 고른 것도 새로 시작한다
        return question is null ? null : new QuestionForm(session, question.Questions).Key(question.CallId);
    }
}

// 한 번에 한 질문씩 보여 준다. 질문이 여럿이면 위의 번호로 오간다
internal sealed record QuestionForm(ChatSession Session, IReadOnlyList<AskQuestion> Questions) : UIComponent
{
    public const string OtherName = "hotfix.chat.other";
    private const float MaxHeight = 260f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var session = Session;
        var questions = Questions;
        var page = UseState(0);
        // 질문마다 고른 줄의 비트. 선택지들 다음 비트가 직접 입력 줄
        var picks = UseState(new int[questions.Count]);
        var typed = UseState(Enumerable.Repeat("", questions.Count).ToArray());
        // 입력란에 글이 있으면 Enter는 그 글을 보낸다
        var drafting = UseExternal(() => session.Draft.Trim().Length > 0);

        var current = questions[page.Value];
        var mask = picks.Value[page.Value];
        var other = 1 << current.Options.Count;
        var last = page.Value == questions.Count - 1;
        var answers = Answers(picks.Value);
        var answered = answers.Any(answer => answer.Count > 0);
        UseAcceptKey(drafting ? null : last ? Submit : Next);
        UseCancelKey(Skip);

        var options = current.Options.Select((option, i) => (Node?)Row(
            Mark((mask & 1 << i) != 0).AlignSelf(Align.Start).Margin(new Edges(0f, 3f, 0f, 0f)),
            Column(
                Text(Markdown.Literal(option.Label)).Wrap().Anchor(TextAlign.MiddleLeft),
                option.Description is { Length: > 0 } description
                    ? Text(Markdown.Literal(description)).TextSize(t.TextCaption).Color(t.MutedForeground).Wrap().Anchor(TextAlign.UpperLeft)
                    : null
            ).Gap(1f).Grow().Shrink()
        ).Gap(t.Space(2)).Padding(t.Space(1.5f), t.Space(1)).HoverOverlay(t.HoverOverlay).OnClick(() => Pick(i)).Key(i));
        // 선택지 끝에 늘 붙는 직접 입력 줄
        var custom = Row(
            Row(Mark((mask & other) != 0)).OnClick(PickOther),
            (Elements.Input(typed.Value[page.Value], Type, "Hotfix_AskOther".Translate()) with { ControlName = OtherName }).Grow()
        ).Gap(t.Space(2)).Padding(t.Space(1.5f), t.Space(1)).Align(Align.Center).Key("other");

        return Column(
            Row().Height(1f).Bg(t.Border),
            Column(
                questions.Count > 1 ? Row(questions.Select((_, i) => (Node?)Pill(i))).Gap(t.Space(1)) : null,
                new ScrollArea(Column(
                    Text(Markdown.Literal(current.Question)).Wrap().Anchor(TextAlign.UpperLeft),
                    Column([.. options, custom]).Gap(t.Space(0.5f))
                ).Gap(t.Space(2)))
                { MaxHeight = MaxHeight }.Key(page.Value),
                Row(
                    Spacer(),
                    Button("Hotfix_AskSkip".Translate(), Skip, size: ButtonSize.Sm),
                    last
                        ? Button("Hotfix_AskSend".Translate(), Submit, ButtonVariant.Primary, ButtonSize.Sm).Disabled(!answered)
                        : Button("Hotfix_AskNext".Translate(), Next, ButtonVariant.Primary, ButtonSize.Sm)
                ).Gap(t.Space(2))
            ).Gap(t.Space(2)).Padding(t.Space(3), t.Space(2))
        );

        // 지금 보는 질문, 답한 질문, 아직인 질문을 색으로 구분한다
        Node Pill(int index)
        {
            var on = index == page.Value;
            var done = answers[index].Count > 0;
            return Row(Text((index + 1).ToString()).TextSize(t.TextCaption).Color(on || done ? t.Foreground : t.MutedForeground))
                .Size(30f, 20f).Center().Radius(10f)
                .Bg(on ? t.Primary : done ? t.Secondary : Color.clear).Border(on ? t.PrimaryBorder : done ? t.SecondaryBorder : t.Border)
                .HoverOverlay(t.HoverOverlay).OnClick(() => page.Set(index)).Key(index);
        }

        // 하나만 고르는 질문은 둥근 표시, 여럿을 고르는 질문은 네모난 표시
        CanvasNode Mark(bool on) => current.MultiSelect ? ChoiceMark.Check(t, on) : ChoiceMark.Radio(t, on);

        void Pick(int index)
        {
            var bit = 1 << index;
            if (current.MultiSelect)
            {
                backend.PlaySound((mask & bit) == 0 ? UiSound.CheckboxOn : UiSound.CheckboxOff);
                picks.Set(now => With(now, page.Value, now[page.Value] ^ bit));
                return;
            }
            backend.PlaySound(UiSound.Tick);
            var next = With(picks.Value, page.Value, bit);
            picks.Set(next);
            // 하나만 고르는 질문은 고르면 다음으로 넘어가고, 질문이 그것뿐이면 바로 보낸다
            if (questions.Count == 1)
                Reply(Answers(next));
            else if (!last)
                page.Set(page.Value + 1);
        }

        // 써 둔 글이 없으면 쓸 수 있게 포커스만 준다
        void PickOther()
        {
            if (typed.Value[page.Value].Trim().Length == 0)
                backend.FocusControl(OtherName);
            else
                picks.Set(now => With(now, page.Value, current.MultiSelect ? now[page.Value] ^ other : other));
        }

        // 글을 쓰면 직접 입력 줄을 고른 것으로, 다 지우면 고르지 않은 것으로 본다
        void Type(string text)
        {
            typed.Set(now => With(now, page.Value, text));
            picks.Set(now => With(now, page.Value,
                text.Trim().Length == 0 ? now[page.Value] & ~other : current.MultiSelect ? now[page.Value] | other : other));
        }

        void Next() => page.Set(page.Value + 1);

        void Submit()
        {
            if (answered)
                Reply(answers);
        }

        void Skip() => Reply(null);

        void Reply(IReadOnlyList<IReadOnlyList<string>>? reply)
        {
            session.Reply(reply);
            backend.FocusControl(Composer.InputName);
        }

        List<IReadOnlyList<string>> Answers(int[] masks) => questions.Select((question, i) => (IReadOnlyList<string>)
        [
            .. question.Options.Where((_, option) => (masks[i] & 1 << option) != 0).Select(option => option.Label),
            .. (masks[i] & 1 << question.Options.Count) != 0 ? [typed.Value[i].Trim()] : Array.Empty<string>(),
        ]).ToList();
    }

    private static T[] With<T>(T[] values, int index, T value)
    {
        var copy = (T[])values.Clone();
        copy[index] = value;
        return copy;
    }
}
