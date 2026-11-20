using System.Linq;
using Rimact;
using Rimact.Components;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record MemoryList(HotfixSettings Settings, MemoryStore Store) : UIComponent
{
    private static readonly WindowOptions EditorWindow = new() { Size = new Vector2(560f, 0f), AutoHeight = true };

    protected internal override Node Build()
    {
        var t = UseTheme();
        var settings = Settings;
        var store = Store;
        var enabled = UseExternal(() => settings.MemoryEnabled);
        var editing = UseState((MemoryEntry?)null);
        var memories = UseMemo(store.List, UseExternal(() => store.Version));

        Node list = memories.Count == 0
            ? Empty("Hotfix_NoMemories".Translate()) with { Icon = null }
            : new ScrollArea(Column(memories.Select(MemoryRow)));

        return Column(
            Row(
                Text("Hotfix_Memory".Translate()).Grow(),
                Switch(enabled, value => settings.MemoryEnabled = value, "Hotfix_MemoryEnabled".Translate())
            ).Align(Align.Center),
            Column(list.Grow()).Bg(t.Card).Border(t.CardBorder).Grow(),
            editing.Value is { } target
                ? Dialog(true, _ => Close(), new MemoryEditor(store, target, Close), EditorWindow with { Title = target.Name })
                : null
        ).Gap(t.Space(2));

        Node? MemoryRow(MemoryEntry memory) => new Item(Markdown.Literal(memory.Name))
        {
            Description = Markdown.Literal(memory.Description),
            Actions = Row(
                memory.Type.Length > 0 ? Badge(memory.Type, BadgeVariant.Secondary) : null,
                IconButton(Glyph.Pencil, () => editing.Set(memory), "Hotfix_EditMemory".Translate()),
                IconButton(Glyph.Trash, () => store.Delete(memory.Name), "Hotfix_Delete".Translate())
            ).Gap(t.Space(1)).Align(Align.Center),
        }.Key(memory.Name);

        void Close() => editing.Set((MemoryEntry?)null);
    }
}
