using System;
using System.Collections.Generic;
using System.Linq;

namespace Hotfix;

internal sealed class ChatSessions
{
    private readonly ChatStore _store;
    private readonly Func<ChatSession> _create;
    private readonly Dictionary<string, ChatSession> _open = [];

    public ChatSession Current { get; private set; }
    public int Running => _open.Values.Count(session => session.Busy);

    public ChatSessions(ChatStore store, Func<ChatSession> create)
    {
        _store = store;
        _create = create;
        Current = Add(create());
        if (store.Latest() is { } latest)
            Open(latest);
    }

    public bool IsBusy(string id) => _open.TryGetValue(id, out var session) && session.Busy;

    public List<ChatInfo> History() => _store.History();

    public void New()
    {
        if (!Current.Empty)
            Show(Blank());
    }

    public void Open(string id)
    {
        if (!_open.TryGetValue(id, out var session))
        {
            session = _create();
            if (!session.Load(id))
                return;
            Add(session);
        }
        Show(session);
    }

    public void Delete(string id)
    {
        if (_open.Remove(id, out var session))
            session.Discard();
        else
            _store.Delete(id);
        if (session == Current)
            Current = Blank();
    }

    private ChatSession Add(ChatSession session) => _open[session.Id] = session;

    private ChatSession Blank() => _open.Values.FirstOrDefault(session => session.Empty) ?? Add(_create());

    private void Show(ChatSession session)
    {
        if (Current != session && Current.Empty && Current.Draft.Length == 0)
            _open.Remove(Current.Id);
        Current = session;
    }
}
