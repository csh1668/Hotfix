using Verse;

namespace Hotfix;

internal static class ScriptGame
{
    public static void SetMainThread(int id) => UnityData.mainThreadId = id;
}
