using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 모달 창의 내용. 서브 모드가 무엇으로 이루어져 있는지 보여 주기만 한다
internal sealed record SubModDetail(SubMod Sub, Action OnClose) : UIComponent
{
    private const float MaxHeight = 520f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var sub = Sub;
        var close = OnClose;
        var folder = SubModManager.Folder(sub.Id);
        // 열려 있는 동안에도 채팅이 다시 빌드할 수 있다
        var version = UseExternal(() => SubModManager.Version);
        var patched = UseMemo(() => SubModCode.Patched(sub.Id), version);
        var files = UseMemo(() => SubModManager.ContentFiles(sub.Id).Select(file => Path.GetRelativePath(folder, file)).ToList(), version);
        var sources = UseMemo(() => Sources(Path.Combine(SubModManager.VersionFolder(sub.Id), "Source")), version);

        return Column(
            new ScrollArea(Column(
                sub.Description.Length > 0 ? Text(Markdown.Literal(sub.Description)).Wrap() : null,
                Muted("Hotfix_SubModInfo".Translate(sub.Created.ToString("yyyy-MM-dd HH:mm"), sub.Revision, string.Join(", ", sub.SupportedVersions))),
                sub.Requires.Count > 0 ? Muted("Hotfix_SubModRequires".Translate(string.Join(", ", sub.Requires))) : null,
                sub.DisabledReason is { } reason ? Text(Markdown.Literal(reason)).Color(t.Destructive).Wrap() : null,
                patched.Count > 0 ? Section(t, "Hotfix_SubModPatched".Translate(), patched) : null,
                files.Count > 0 ? Section(t, "Hotfix_SubModFiles".Translate(), files) : null,
                sources.Count > 0 ? Separator("Hotfix_SubModSource".Translate()) : null,
                Column(sources.Select(source => new Collapsible(source.Name, new CodeBlock("csharp", source.Code)).Key(source.Name)))
            ).Gap(t.Space(2)))
            { MaxHeight = MaxHeight },
            DialogFooter(
                Button("Hotfix_OpenFolder".Translate(), () => Application.OpenURL(folder)),
                sub.Chat is { } chat
                    ? Button("Hotfix_OpenChat".Translate(), () =>
                    {
                        close();
                        ChatRuntime.Sessions.Open(chat);
                        ChatOverlay.Show();
                    })
                    : null,
                Button("CloseButton".Translate(), close) with { IsCancel = true })
        ).Gap(t.Space(2));
    }

    private static Node Section(Theme t, string title, IEnumerable<string> lines) => Column(
        Separator(title),
        Text(Markdown.Literal(string.Join("\n", lines))).TextSize(t.TextCaption + 1f).Color(t.MutedForeground).Wrap().Anchor(TextAlign.UpperLeft));

    private static List<(string Name, string Code)> Sources(string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => (Path.GetRelativePath(folder, file), File.ReadAllText(file))).ToList()
        : [];
}
