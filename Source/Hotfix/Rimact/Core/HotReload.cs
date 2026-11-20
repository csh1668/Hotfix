using System;
using System.Threading;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Rimact.HotReload))]

namespace Rimact;

// Re-renders every host after a .NET hot reload edit, so a changed Build() shows without reopening the window.
// The runtime calls the handlers on its own thread, so they only bump Generation. Hosts re-render on their next event.
// Needs DOTNET_MODIFIABLE_ASSEMBLIES=debug and an unoptimized build, see docs/hot-reload.md
internal static class HotReload
{
    private static int s_generation;

    public static int Generation => Volatile.Read(ref s_generation);

    // Called by the runtime, ClearCache first
    internal static void ClearCache(Type[]? types) => PropEquality.ClearCache();

    internal static void UpdateApplication(Type[]? types) => Interlocked.Increment(ref s_generation);
}
