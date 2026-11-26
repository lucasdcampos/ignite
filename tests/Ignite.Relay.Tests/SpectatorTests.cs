using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Ignite.Protocol.Av;
using Ignite.Protocol.Crypto;
using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.Relay.Tests;

public sealed class SpectatorTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 0);

    private readonly Channel<SpectatorConnection> _offers = Channel.CreateUnbounded<SpectatorConnection>();
    private readonly SpectatorListener _listener;
    private readonly ConsolePackets _console = new();
    private int _keyFrames;

    public SpectatorTests() => _listener = new SpectatorListener(Loopback, _ => true, c => _offers.Writer.TryWrite(c));

    public void Dispose()
    {
        _listener.Dispose();
        _console.Dispose();
    }

    [Fact]
    public void OfferRoundTrips()
    {
        var key = Enumerable.Range(0, GkCrypt.KeyMaterialSize).Select(i => (byte)i).ToArray();

        Assert.True(SpectatorOffer.TryDecode(SpectatorOffer.Encode(VideoCodec.HevcHdr, key), out var codec, out var decoded));
        Assert.Equal(VideoCodec.HevcHdr, codec);
        Assert.Equal(key, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsOffersWithAWrongMagicVersionOrCodec(int index)
    {
        var offer = SpectatorOffer.Encode(VideoCodec.H264, new byte[GkCrypt.KeyMaterialSize]);
        offer[index] = 0x7F;

        Assert.False(SpectatorOffer.TryDecode(offer, out _, out _));
    }

    [Fact]
    public async Task LinkOffersTheKeyAndFollowsTheWatcher()
    {
        var key = _console.Crypt.ExportKeyMaterial();
        await using var link = Link(key);

        var connection = await NextOfferAsync();
        Assert.Equal(IPAddress.Loopback, connection.Client);
        Assert.Equal(VideoCodec.Hevc, connection.Codec);
        Assert.Equal(key, connection.ConsoleKey);

        connection.Send(SpectatorSignal.Watching);
        await UntilAsync(() => link.Watched);
        connection.Send(SpectatorSignal.KeyFrame);
        connection.Send(SpectatorSignal.KeyFrame);
        connection.Send(SpectatorSignal.NotWatching);
        await UntilAsync(() => !link.Watched);

        Assert.Equal(1, Volatile.Read(ref _keyFrames));
    }

    [Fact]
    public async Task ClosingTheLinkClosesTheConnection()
    {
        var link = Link(_console.Crypt.ExportKeyMaterial());
        var connection = await NextOfferAsync();

        await link.DisposeAsync();

        await connection.Closed.WaitAsync(Timeout);
    }

    [Fact]
    public async Task LinkGivesUpQuietlyWhenNothingListens()
    {
        int port;
        using (var probe = new TcpListener(Loopback))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var link = SpectatorLink.Start(new IPEndPoint(IPAddress.Loopback, port), VideoCodec.H264, new byte[GkCrypt.KeyMaterialSize], _ => Task.CompletedTask);

        await link.DisposeAsync();
        Assert.False(link.Watched);
    }

    [Fact]
    public async Task ListenerIgnoresInvalidOffers()
    {
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(_listener.LocalEndPoint);

        await client.SendAsync(new byte[SpectatorOffer.Size]);

        using var timeout = new CancellationTokenSource(Timeout);
        Assert.Equal(0, await client.ReceiveAsync(new byte[1], SocketFlags.None, timeout.Token));
        Assert.False(_offers.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ListenerIgnoresClientsTheFilterRejects()
    {
        var offered = false;
        using var listener = new SpectatorListener(Loopback, _ => false, _ => offered = true);
        await using var link = SpectatorLink.Start(listener.LocalEndPoint, VideoCodec.H264, new byte[GkCrypt.KeyMaterialSize], _ => Task.CompletedTask);

        await Task.Delay(300);

        Assert.False(offered);
    }

    [Fact]
    public async Task WatchingAnOfferedSessionShowsTheConsolesPicture()
    {
        var sessions = new RelayedSessions();
        var tap = sessions.Tap(new IPEndPoint(IPAddress.Loopback, 50000));
        tap.OnConsoleDatagram(_console.StreamInfo());
        await using var link = Link(_console.Crypt.ExportKeyMaterial());
        sessions.Offer(await NextOfferAsync());
        Assert.Null(sessions.Watchable);
        tap.OnConsoleDatagram(_console.Video(1, [1, 2, 3]));

        var session = Assert.IsType<RelayedSession>(sessions.Watchable);
        var sink = new FrameSink();
        using (session.Watch(sink))
        {
            await UntilAsync(() => link.Watched);
            await UntilAsync(() => Volatile.Read(ref _keyFrames) == 1);
            tap.OnConsoleDatagram(_console.Video(2, [4, 5, 6]));
        }

        await UntilAsync(() => !link.Watched);
        Assert.Equal(VideoCodec.Hevc, session.Codec);
        Assert.Equal(1280, sink.Width);
        Assert.Equal([4, 5, 6], Assert.Single(sink.Frames));
    }

    [Fact]
    public async Task SessionEndsWhenTheLinkCloses()
    {
        var sessions = new RelayedSessions();
        var tap = sessions.Tap(new IPEndPoint(IPAddress.Loopback, 50000));
        tap.OnConsoleDatagram(_console.StreamInfo());
        var link = Link(_console.Crypt.ExportKeyMaterial());
        sessions.Offer(await NextOfferAsync());
        tap.OnConsoleDatagram(_console.Video(1, [1, 2, 3]));
        var session = sessions.Watchable!;

        await link.DisposeAsync();

        Assert.Equal(RelayedSession.LinkClosedReason, await session.Ended.WaitAsync(Timeout));
        Assert.Null(sessions.Watchable);
    }

    [Fact]
    public async Task OfferFromAnotherClientIsNotAccepted()
    {
        var sessions = new RelayedSessions();
        var tap = sessions.Tap(new IPEndPoint(IPAddress.Parse("26.1.2.3"), 50000));
        tap.OnConsoleDatagram(_console.StreamInfo());
        await using var link = Link(_console.Crypt.ExportKeyMaterial());
        var connection = await NextOfferAsync();

        sessions.Offer(connection);
        tap.OnConsoleDatagram(_console.Video(1, [1, 2, 3]));

        Assert.Null(sessions.Watchable);
        await connection.Closed.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ForgettingTheClientEndsItsSession()
    {
        var sessions = new RelayedSessions();
        var tap = sessions.Tap(new IPEndPoint(IPAddress.Loopback, 50000));
        tap.OnConsoleDatagram(_console.StreamInfo());
        await using var link = Link(_console.Crypt.ExportKeyMaterial());
        sessions.Offer(await NextOfferAsync());
        tap.OnConsoleDatagram(_console.Video(1, [1, 2, 3]));
        var session = sessions.Watchable!;

        tap.Dispose();

        Assert.Equal(RelayedSession.ClientGoneReason, await session.Ended.WaitAsync(Timeout));
        Assert.Null(sessions.Watchable);
    }

    private SpectatorLink Link(byte[] key) => SpectatorLink.Start(_listener.LocalEndPoint, VideoCodec.Hevc, key, _ =>
    {
        Interlocked.Increment(ref _keyFrames);
        return Task.CompletedTask;
    });

    private async Task<SpectatorConnection> NextOfferAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        return await _offers.Reader.ReadAsync(timeout.Token);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10);
        }
    }

    private sealed class FrameSink : IStreamSink
    {
        public ConcurrentQueue<byte[]> Frames { get; } = new();

        public int Width { get; private set; }

        public void OnStreamInfo(IReadOnlyList<VideoStreamProfile> profiles, AudioFormat audio) => Width = profiles[0].Width;

        public void OnVideoFrame(ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte>? streamHeader, bool recoveredWithFec) => Frames.Enqueue(frame.ToArray());

        public void OnAudioFrame(ReadOnlySpan<byte> frame, bool haptics)
        {
        }

        public void OnRumble(byte left, byte right)
        {
        }

        public void OnTriggerEffects(TriggerEffect left, TriggerEffect right)
        {
        }

        public void OnPadInfo(PadInfo info)
        {
        }

        public void OnDisconnected(string reason)
        {
        }
    }
}
