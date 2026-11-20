using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Verse;

namespace Rimact;

internal readonly record struct CallSite(string File, string Member, int Line);

// Hosts behind RimactUI.Draw: a Scope per owner, and in it a host per call site (file, line, key).
// A site drawn twice in one pass gets a host per draw by order, and logs an error once
internal sealed class HostRegistry
{
    // Undrawn frames before a host in a Forgets scope is disposed
    public const int ForgetAfterFrames = 300;

    internal sealed class Scope
    {
        internal readonly Dictionary<Site, Entry> Entries = new();
        internal object? Era;
        internal int LastSweep;

        // For owners that never go away
        public bool Forgets { get; init; }

        // What UseHost().Close() does in this scope's hosts
        public Action? Close { get; set; }

        public Window? Window { get; set; }

        public int Count => Entries.Count;
    }

    internal readonly record struct Site(string File, int Line, string? Key, int Occurrence);

    internal sealed class Entry
    {
        public RimactHost Host = null!;
        public Node Root = null!;
        public string Member = null!;
        public long Pass;
        public int Frame;
        public int Generation;
    }

    private readonly ConditionalWeakTable<object, Scope> _scopes = new();
    private readonly Func<Node, RimactHost> _create;
    private readonly Action<string> _error;
    private readonly Func<int> _generation;
    private readonly HashSet<(string file, int line, string? key)> _reported = new();

    public HostRegistry(Func<Node, RimactHost> create, Action<string> error, Func<int>? generation = null)
    {
        _create = create;
        _error = error;
        _generation = generation ?? (static () => HotReload.Generation);
    }

    // Lives as long as owner. close builds Scope.Close once, when the scope is created
    public Scope ScopeFor(object owner, Func<object, Scope, Action?>? close = null, bool forgets = false)
    {
        if (_scopes.TryGetValue(owner, out var scope)) return scope;
        scope = new Scope { Forgets = forgets };
        scope.Close = close?.Invoke(owner, scope);
        _scopes.Add(owner, scope);
        return scope;
    }

    // A new root is applied only when it differs by value.
    // pass numbers the current event pass. The same site twice in one pass is a duplicate.
    // era is the loaded game. When it changes, the scope's hosts are disposed
    public RimactHost Get(Scope scope, Node root, string? key, CallSite site, long pass, object? era, int frame)
    {
        if (!ReferenceEquals(scope.Era, era))
        {
            Clear(scope);
            scope.Era = era;
        }

        var at = new Site(site.File, site.Line, key, 0);
        Entry? entry;
        while (scope.Entries.TryGetValue(at, out entry) && entry.Pass == pass)
        {
            if (at.Occurrence == 0)
                Report(site.File, site.Line, key, key is null
                    ? "drew more than once in one pass; give each tree its own key, or they keep their state by draw order"
                    : $"drew key \"{key}\" more than once in one pass; a key must name one tree in its window");
            at = at with { Occurrence = at.Occurrence + 1 };
        }

        int generation = _generation();
        entry ??= Reclaim(scope, at, site.Member, root.GetType(), generation);
        if (entry is null)
        {
            var host = _create(root);
            host.CloseRequested = scope.Close;
            entry = new Entry { Host = host, Root = root, Member = site.Member };
            scope.Entries[at] = entry;
        }
        else if (!PropEquality.Equal(entry.Root, root))
        {
            entry.Host.SetRoot(root);
            entry.Root = root;
        }
        entry.Pass = pass;
        entry.Frame = frame;
        entry.Generation = generation;

        if (scope.Forgets && frame - scope.LastSweep > ForgetAfterFrames) Sweep(scope, frame);
        return entry.Host;
    }

    // Once per site and key
    public void Report(string file, int line, string? key, string message)
    {
        if (_reported.Add((file, line, key))) _error($"[Rimact] RimactUI.Draw at {Path.GetFileName(file)}:{line} {message}.");
    }

    // Hot reload can move lines. Take over a host not drawn since the reload with the same file, member, key and root type
    private static Entry? Reclaim(Scope scope, Site at, string member, Type rootType, int generation)
    {
        Site? best = null;
        foreach (var (site, e) in scope.Entries)
        {
            if (e.Generation == generation || site.File != at.File || site.Key != at.Key || e.Member != member
                || e.Root.GetType() != rootType) continue;
            if (best is not { } b || site.Line < b.Line || site.Line == b.Line && site.Occurrence < b.Occurrence) best = site;
        }
        if (best is not { } from) return null;
        var entry = scope.Entries[from];
        scope.Entries.Remove(from);
        scope.Entries[at] = entry;
        return entry;
    }

    private static void Sweep(Scope scope, int frame)
    {
        scope.LastSweep = frame;
        List<Site>? stale = null;
        foreach (var (site, e) in scope.Entries)
            if (frame - e.Frame > ForgetAfterFrames) (stale ??= new()).Add(site);
        if (stale is null) return;
        foreach (var site in stale)
        {
            scope.Entries[site].Host.Dispose();
            scope.Entries.Remove(site);
        }
    }

    private static void Clear(Scope scope)
    {
        foreach (var e in scope.Entries.Values) e.Host.Dispose();
        scope.Entries.Clear();
    }
}
