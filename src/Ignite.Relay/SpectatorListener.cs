using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Ignite.Relay;

/// <summary>Accepts spectator links from the clients the relay serves and hands every valid offer on.</summary>
internal sealed class SpectatorListener : IDisposable
{
    private static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(5);

    private readonly Socket _listener;
    private readonly Func<IPAddress, bool> _accepts;
    private readonly Action<SpectatorConnection> _offered;
    private readonly ConcurrentDictionary<SpectatorConnection, byte> _connections = new();
    private readonly CancellationTokenSource _stop = new();

    public SpectatorListener(IPEndPoint listen, Func<IPAddress, bool> accepts, Action<SpectatorConnection> offered)
    {
        _accepts = accepts;
        _offered = offered;
        _listener = new Socket(listen.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _listener.Bind(listen);
            _listener.Listen();
        }
        catch
        {
            _listener.Dispose();
            throw;
        }

        _ = Task.Run(() => AcceptAsync(_stop.Token));
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_listener.LocalEndPoint!;

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        foreach (var connection in _connections.Keys)
            connection.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(cancellationToken);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue;
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            if (!_accepts(((IPEndPoint)client.RemoteEndPoint!).Address))
            {
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => ServeAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ServeAsync(Socket client, CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        var stream = new NetworkStream(client, ownsSocket: true);
        var offer = new byte[SpectatorOffer.Size];
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OfferTimeout);
            await stream.ReadExactlyAsync(offer, timeout.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or EndOfStreamException or IOException or ObjectDisposedException)
        {
            await stream.DisposeAsync();
            return;
        }

        if (!SpectatorOffer.TryDecode(offer, out var codec, out var consoleKey))
        {
            await stream.DisposeAsync();
            return;
        }

        var connection = new SpectatorConnection(stream, ((IPEndPoint)client.RemoteEndPoint!).Address, codec, consoleKey);
        _connections[connection] = 0;
        _offered(connection);
        await connection.RunAsync(cancellationToken);
        _connections.TryRemove(connection, out _);
        connection.Dispose();
    }
}
