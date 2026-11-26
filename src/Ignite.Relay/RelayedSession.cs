using System.Net;
using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.Relay;

/// <summary>One relayed stream connection, watchable once its client offered the console key.</summary>
internal sealed class RelayedSession : IRelayedSession, IUdpTap
{
    public const string LinkClosedReason = "The player's stream ended.";
    public const string ClientGoneReason = "The player went away.";

    private readonly StreamObserver _observer = new();
    private readonly Action<RelayedSession> _closed;
    private volatile SpectatorConnection? _connection;

    public RelayedSession(IPAddress client, Action<RelayedSession> closed)
    {
        Client = client;
        _closed = closed;
        _observer.KeyFrameNeeded += () => _connection?.Send(SpectatorSignal.KeyFrame);
    }

    public IPAddress Client { get; }

    public VideoCodec Codec { get; private set; }

    public Task<string> Ended => _observer.Ended;

    public bool CanWatch => _connection is not null && _observer.CanWatch;

    /// <summary>Takes the key of a client's offer when it is this stream's client and no key came yet.</summary>
    public bool TryAccept(SpectatorConnection connection)
    {
        if (!connection.Client.Equals(Client) || _observer.HasKey || _observer.Ended.IsCompleted)
            return false;
        Codec = connection.Codec;
        _observer.SetConsoleKey(connection.ConsoleKey);
        _connection = connection;
        _ = connection.Closed.ContinueWith(_ => _observer.End(LinkClosedReason), TaskScheduler.Default);
        return true;
    }

    public void OnConsoleDatagram(Span<byte> datagram) => _observer.OnConsoleDatagram(datagram);

    public IDisposable Watch(IStreamSink sink)
    {
        _observer.Attach(sink);
        _connection?.Send(SpectatorSignal.Watching);
        return new Watcher(this);
    }

    public void Dispose()
    {
        _observer.End(ClientGoneReason);
        _observer.Dispose();
        _closed(this);
    }

    private sealed class Watcher(RelayedSession session) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            session._observer.Detach();
            session._connection?.Send(SpectatorSignal.NotWatching);
        }
    }
}
