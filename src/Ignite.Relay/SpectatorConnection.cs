using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Ignite.Protocol.Session;

namespace Ignite.Relay;

/// <summary>The relay's end of one playing client's spectator link.</summary>
internal sealed class SpectatorConnection : IDisposable
{
    private readonly NetworkStream _stream;
    private readonly Channel<SpectatorSignal> _signals = Channel.CreateUnbounded<SpectatorSignal>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SpectatorConnection(NetworkStream stream, IPAddress client, VideoCodec codec, byte[] consoleKey)
    {
        _stream = stream;
        Client = client;
        Codec = codec;
        ConsoleKey = consoleKey;
    }

    public IPAddress Client { get; }

    public VideoCodec Codec { get; }

    public byte[] ConsoleKey { get; }

    /// <summary>Completes when the client closed the link, which it does when its stream ends.</summary>
    public Task Closed => _closed.Task;

    public void Send(SpectatorSignal signal) => _signals.Writer.TryWrite(signal);

    public void Dispose()
    {
        _signals.Writer.TryComplete();
        _stream.Dispose();
        _closed.TrySetResult();
    }

    /// <summary>Sends queued signals until the client closes the link.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var send = SendAsync(connection.Token);
        await WaitForCloseAsync(connection.Token);
        await connection.CancelAsync();
        await send;
        _closed.TrySetResult();
    }

    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var signal = new byte[1];
        try
        {
            await foreach (var next in _signals.Reader.ReadAllAsync(cancellationToken))
            {
                signal[0] = (byte)next;
                await _stream.WriteAsync(signal, cancellationToken);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    // The client sends nothing after its offer; the read ends when it closes the link.
    private async Task WaitForCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[16];
            while (await _stream.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }
}
