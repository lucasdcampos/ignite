using System.Net;
using System.Net.Sockets;
using Ignite.Protocol.Session;

namespace Ignite.Relay;

/// <summary>
/// The playing client's end of the spectator channel: offers the console key of a running stream
/// to the relay it plays through, follows whether someone watches and passes key frame requests
/// on to the stream. A real console refuses the connection, and the link then does nothing
/// (docs/architecture.md#watching-a-shared-console).
/// </summary>
public sealed class SpectatorLink : IAsyncDisposable
{
    /// <summary>TCP port of the spectator channel on the relay. It is Ignite's own, not a Remote Play port.</summary>
    public const int Port = 9320;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Key frames cost the player bandwidth, so the watcher gets at most one per interval.</summary>
    private static readonly TimeSpan KeyFrameInterval = TimeSpan.FromSeconds(1);

    private readonly Func<CancellationToken, Task> _requestKeyFrame;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private long? _lastKeyFrame;
    private volatile bool _watched;

    private SpectatorLink(IPEndPoint relay, byte[] offer, Func<CancellationToken, Task> requestKeyFrame)
    {
        _requestKeyFrame = requestKeyFrame;
        _run = Task.Run(() => RunAsync(relay, offer, _stop.Token));
    }

    /// <summary>True while someone on the sharing computer watches the stream.</summary>
    public bool Watched => _watched;

    /// <param name="consoleKey">The console's key material from the stream connection.</param>
    /// <param name="requestKeyFrame">Asks the console for a key frame.</param>
    public static SpectatorLink Start(IPEndPoint relay, VideoCodec codec, ReadOnlySpan<byte> consoleKey, Func<CancellationToken, Task> requestKeyFrame) =>
        new(relay, SpectatorOffer.Encode(codec, consoleKey), requestKeyFrame);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run;
        _stop.Dispose();
    }

    private async Task RunAsync(IPEndPoint relay, byte[] offer, CancellationToken cancellationToken)
    {
        using var socket = new Socket(relay.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(relay, timeout.Token);
            }

            await socket.SendAsync(offer, SocketFlags.None, cancellationToken);
            var signal = new byte[1];
            while (await socket.ReceiveAsync(signal, SocketFlags.None, cancellationToken) == 1)
                await HandleAsync((SpectatorSignal)signal[0], cancellationToken);
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            _watched = false;
        }
    }

    private async Task HandleAsync(SpectatorSignal signal, CancellationToken cancellationToken)
    {
        switch (signal)
        {
            case SpectatorSignal.Watching:
                _watched = true;
                break;
            case SpectatorSignal.NotWatching:
                _watched = false;
                break;
            case SpectatorSignal.KeyFrame:
                var now = Environment.TickCount64;
                if (_lastKeyFrame is { } last && now - last < KeyFrameInterval.TotalMilliseconds)
                    break;
                _lastKeyFrame = now;
                await _requestKeyFrame(cancellationToken);
                break;
        }
    }
}
