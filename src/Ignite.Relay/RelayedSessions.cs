using System.Net;

namespace Ignite.Relay;

/// <summary>The stream connections passing through the relay, matched with the keys their clients offer.</summary>
internal sealed class RelayedSessions
{
    private readonly Lock _lock = new();
    private readonly List<RelayedSession> _sessions = [];

    /// <summary>The newest session that can be watched.</summary>
    public IRelayedSession? Watchable
    {
        get
        {
            lock (_lock)
                return _sessions.LastOrDefault(s => s.CanWatch);
        }
    }

    public IUdpTap Tap(IPEndPoint client)
    {
        var session = new RelayedSession(client.Address, Remove);
        lock (_lock)
            _sessions.Add(session);
        return session;
    }

    /// <summary>
    /// Gives the key to every stream of the offering client that has none. A client has one
    /// stream at a time, and an older connection that did not end cleanly simply never verifies.
    /// </summary>
    public void Offer(SpectatorConnection connection)
    {
        bool accepted;
        lock (_lock)
            accepted = _sessions.Aggregate(false, (any, session) => session.TryAccept(connection) | any);
        if (!accepted)
            connection.Dispose();
    }

    private void Remove(RelayedSession session)
    {
        lock (_lock)
            _sessions.Remove(session);
    }
}
