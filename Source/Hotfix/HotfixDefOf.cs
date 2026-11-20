using RimWorld;
using Verse;

namespace Hotfix;

[DefOf]
public static class HotfixDefOf
{
    public static KeyBindingDef Hotfix_ToggleChat = null!;

    static HotfixDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HotfixDefOf));
}
