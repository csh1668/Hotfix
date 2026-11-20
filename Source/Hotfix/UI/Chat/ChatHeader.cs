using System;
using System.Collections.Generic;
using System.Linq;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record ChatHeader(ChatSessions Sessions, Action OnClose) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var sessions = Sessions;
        var opacity = UseExternal(() => HotfixMod.Settings.ChatOpacity);
        var (busy, running) = UseExternal(() => (sessions.Current.Busy, sessions.Running));
        // 열려 있는 동안의 지난 대화 목록, 닫혀 있으면 null
        var chats = UseState((List<ChatInfo>?)null);

        return Row(
            Slider(opacity, value => HotfixMod.Settings.ChatOpacity = value, 0.2f).Width(120f)
                .Tooltip("Hotfix_ChatOpacity".Translate()),
            Spacer(),
            // 다른 대화가 뒤에서 진행 중
            running > (busy ? 1 : 0) ? Spinner(14f) : null,
            new Popover(IconButton(Glyph.Clock, tooltip: "Hotfix_ChatHistory".Translate()),
                new ChatHistory(sessions, chats.Value ?? [],
                    id =>
                    {
                        sessions.Open(id);
                        CloseHistory();
                    },
                    id =>
                    {
                        sessions.Delete(id);
                        chats.Set(sessions.History());
                    }))
            {
                Open = chats.Value is not null,
                OnOpenChange = open =>
                {
                    if (open)
                        chats.Set(sessions.History());
                    else
                        CloseHistory();
                },
                Width = 300f,
            },
            IconButton(Glyph.Plus, sessions.New, "Hotfix_ChatNew".Translate()),
            IconButton(Glyph.X, OnClose, "Hotfix_ChatClose".Translate())
        ).Height(ChatUI.HeaderHeight).Padding(new Edges(t.Space(3), 0f, t.Space(1), 0f)).Align(Align.Center);

        void CloseHistory()
        {
            chats.Set((List<ChatInfo>?)null);
            // 검색란이 가져간 포커스를 입력란에 돌려준다
            backend.FocusControl(Composer.InputName);
        }
    }
}

// 지난 대화 목록과 검색란
internal sealed record ChatHistory(ChatSessions Sessions, List<ChatInfo> Chats, Action<string> OnOpen, Action<string> OnDelete)
    : UIComponent
{
    public const string SearchName = "hotfix.chat.search";

    protected internal override Node Build()
    {
        var t = UseTheme();
        var sessions = Sessions;
        var open = OnOpen;
        var delete = OnDelete;
        var current = UseExternal(() => sessions.Current.Id);
        var query = UseState("");
        var word = query.Value.Trim();
        // 검색어가 있으면 사용자의 글이나 응답에 그 말이 나오는 대화만 남긴다
        var found = Chats.Select(chat => (Chat: chat, At: chat.Text.IndexOf(word, StringComparison.OrdinalIgnoreCase)))
            .Where(hit => hit.At >= 0).ToList();
        // 검색란에서 Enter를 누르면 첫 결과를 연다
        UseAcceptKey(() =>
        {
            if (found.Count > 0)
                open(found[0].Chat.Id);
        });

        if (Chats.Count == 0)
            return Text("Hotfix_ChatHistoryEmpty".Translate()).Color(t.MutedForeground);
        return Column(
            SearchInput(query.Value, query.Set, "Hotfix_ChatSearch".Translate()) with { ControlName = SearchName, AutoFocus = true },
            found.Count == 0
                ? Text("Hotfix_ChatSearchEmpty".Translate()).Color(t.MutedForeground)
                : new ScrollArea(Column(found.Select(hit => (Node?)new Item(Markdown.Literal(hit.Chat.Title))
                {
                    // 검색 중에는 찾은 곳의 앞뒤를 보여 준다
                    Description = word.Length == 0
                        ? hit.Chat.Time.ToString("yyyy-MM-dd HH:mm")
                        : Markdown.Literal(Around(hit.Chat.Text, hit.At, word.Length)),
                    Selected = hit.Chat.Id == current,
                    OnClick = () => open(hit.Chat.Id),
                    Actions = Row(
                        sessions.IsBusy(hit.Chat.Id) ? Spinner(14f) : null,
                        IconButton(Glyph.Trash, () => delete(hit.Chat.Id), "Hotfix_Delete".Translate())
                    ).Gap(t.Space(2)).Align(Align.Center),
                }.Key(hit.Chat.Id))))
                { MaxHeight = 360f }
        ).Gap(t.Space(2));
    }

    private static string Around(string text, int at, int length)
    {
        var start = Math.Max(0, at - 16);
        var end = Math.Min(text.Length, at + length + 48);
        return (start > 0 ? "..." : "") + text[start..end].ReplaceLineEndings(" ") + (end < text.Length ? "..." : "");
    }
}
