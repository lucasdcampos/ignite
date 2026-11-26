using Google.Protobuf;
using Ignite.Protocol.Av;
using Ignite.Protocol.Crypto;
using Ignite.Protocol.Stream;
using Ignite.Protocol.Stream.Messages;
using Ignite.Protocol.Takion;
using Ignite.Protocol.Tests.Av;
using static Ignite.Protocol.Stream.Messages.TakionMessage.Types;
using static Ignite.Protocol.Tests.Support.TestBytes;

namespace Ignite.Protocol.Tests.Stream;

public sealed class StreamObserverTests : IDisposable
{
    private static readonly byte[] Header = [0, 0, 0, 1, 0x40, 0x01];
    private static readonly byte[] AudioHeader = [2, 16, 0, 0, 0xBB, 0x80, 0, 0, 0x01, 0xE0, 0, 0, 0, 1];

    private readonly GkCrypt _console = new(GkCrypt.RemoteIndex, Sequence(16, i => 0xA0 + i), Sequence(32, i => 0x40 + i));
    private readonly StreamObserver _observer = new();
    private readonly RecordingSink _sink = new();
    private ulong _keyPos = 0x1000;
    private uint _sequence = 100;
    private int _keyFrameRequests;

    public StreamObserverTests() => _observer.KeyFrameNeeded += () => _keyFrameRequests++;

    public void Dispose()
    {
        _observer.Dispose();
        _console.Dispose();
    }

    [Fact]
    public void DeliversDecryptedVideoAndAudioOnceTheKeyIsKnown()
    {
        var frame = Sequence(500, i => i * 7);
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());

        ObserveFrame(1, frame);
        Observe(Encrypted(AvTestPackets.Audio(1, 1, 0, [Sequence(20, i => i + 1)]), 0x14));

        Assert.Equal(48000, _sink.Audio!.SampleRate);
        var (received, header) = Assert.Single(_sink.Frames);
        Assert.Equal(frame, received);
        Assert.Equal(Header, header);
        Assert.Equal(Sequence(20, i => i + 1), Assert.Single(_sink.AudioFrames));
        Assert.Equal(1, _keyFrameRequests);
        Assert.True(_observer.CanWatch);
    }

    [Fact]
    public void ReplaysStreamInfoToASinkThatAttachesLater()
    {
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());
        ObserveFrame(1, Sequence(100, i => i));
        Assert.True(_observer.CanWatch);
        Assert.Equal(0, _keyFrameRequests);

        _observer.Attach(_sink);
        ObserveFrame(2, Sequence(100, i => i + 3));

        Assert.Equal(48000, _sink.Audio!.SampleRate);
        Assert.Equal(Sequence(100, i => i + 3), Assert.Single(_sink.Frames).Frame);
        Assert.Equal(1, _keyFrameRequests);
    }

    [Fact]
    public void IgnoresPacketsThatTheKeyDoesNotAuthenticate()
    {
        using var other = new GkCrypt(GkCrypt.RemoteIndex, Sequence(16, i => i), Sequence(32, i => i));
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(other.ExportKeyMaterial());

        ObserveFrame(1, Sequence(100, i => i));

        Assert.Empty(_sink.Frames);
        Assert.False(_observer.CanWatch);
    }

    [Fact]
    public void KeepsTheKeyPositionWhileNobodyWatches()
    {
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());
        ObserveFrame(1, Sequence(100, i => i));

        // 4.5 GiB of key positions pass, more than the 32 bits on the wire can tell apart at once.
        for (ushort frame = 2; frame < 11; frame++)
        {
            _keyPos += 512UL * 1024 * 1024;
            ObserveFrame(frame, Sequence(100, i => i));
        }

        _observer.Attach(_sink);
        _keyPos += 1000;
        ObserveFrame(11, Sequence(100, i => i + 9));

        Assert.True(_keyPos > uint.MaxValue);
        Assert.Equal(Sequence(100, i => i + 9), Assert.Single(_sink.Frames).Frame);
    }

    [Fact]
    public void AsksForAKeyFrameWhenFramesAreLost()
    {
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());
        ObserveFrame(1, Sequence(100, i => i));

        ObserveFrame(4, Sequence(100, i => i));

        Assert.Equal(2, _sink.Frames.Count);
        Assert.Equal(2, _keyFrameRequests);
    }

    [Fact]
    public async Task EndsWhenTheConsoleDisconnects()
    {
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());

        Observe(Message(new TakionMessage { Type = PayloadType.Disconnect, DisconnectPayload = new DisconnectPayload { Reason = "Server shutting down" } }));

        Assert.True(_observer.Ended.IsCompleted);
        Assert.Equal("Server shutting down", await _observer.Ended);
        Assert.Equal(["Server shutting down"], _sink.Disconnects);
        Assert.False(_observer.CanWatch);
    }

    [Fact]
    public void DetachedSinkGetsNothingMore()
    {
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());

        _observer.Detach();
        ObserveFrame(1, Sequence(100, i => i));

        Assert.Empty(_sink.Frames);
    }

    [Fact]
    public void KeepsTheFirstKey()
    {
        using var other = new GkCrypt(GkCrypt.RemoteIndex, Sequence(16, i => i), Sequence(32, i => i));
        _observer.Attach(_sink);
        Observe(StreamInfo());
        _observer.SetConsoleKey(_console.ExportKeyMaterial());

        _observer.SetConsoleKey(other.ExportKeyMaterial());
        ObserveFrame(1, Sequence(100, i => i));

        Assert.Single(_sink.Frames);
    }

    private void Observe(byte[] datagram) => _observer.OnConsoleDatagram(datagram);

    private void ObserveFrame(ushort frameIndex, byte[] frame)
    {
        var units = AvTestPackets.EncodeFrame(frame, 2, 1);
        for (var u = 0; u < units.Length; u++)
            Observe(Encrypted(AvTestPackets.Video((ushort)(frameIndex * 4 + u), frameIndex, u, units.Length, 1, 0, units[u]), 0x15));
    }

    private byte[] Encrypted(byte[] datagram, int payloadOffset)
    {
        var keyPos = _keyPos;
        _keyPos += (ulong)(datagram.Length + (datagram.Length % 16));
        _console.Transform(keyPos + GkCrypt.BlockSize, datagram.AsSpan(payloadOffset));
        TakionPackets.Sign(_console, datagram, keyPos);
        return datagram;
    }

    private byte[] StreamInfo() => Message(new TakionMessage
    {
        Type = PayloadType.Streaminfo,
        StreamInfoPayload = new StreamInfoPayload
        {
            Resolution = { new ResolutionPayload { Width = 1920, Height = 1080, VideoHeader = ByteString.CopyFrom(Header) } },
            AudioHeader = ByteString.CopyFrom(AudioHeader),
        },
    });

    private byte[] Message(TakionMessage message) =>
        TakionPackets.BuildControl(0x55667788, TakionChunkType.Data, 1, TakionPackets.BuildData(_sequence++, 1, TakionDataType.Protobuf, message.ToByteArray()));

    private sealed class RecordingSink : IStreamSink
    {
        public List<(byte[] Frame, byte[]? Header)> Frames { get; } = [];

        public List<byte[]> AudioFrames { get; } = [];

        public List<string> Disconnects { get; } = [];

        public AudioFormat? Audio { get; private set; }

        public void OnStreamInfo(IReadOnlyList<VideoStreamProfile> profiles, AudioFormat audio) => Audio = audio;

        public void OnVideoFrame(ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte>? streamHeader, bool recoveredWithFec) =>
            Frames.Add((frame.ToArray(), streamHeader?.ToArray()));

        public void OnAudioFrame(ReadOnlySpan<byte> frame, bool haptics) => AudioFrames.Add(frame.ToArray());

        public void OnRumble(byte left, byte right)
        {
        }

        public void OnTriggerEffects(TriggerEffect left, TriggerEffect right)
        {
        }

        public void OnPadInfo(PadInfo info)
        {
        }

        public void OnDisconnected(string reason) => Disconnects.Add(reason);
    }
}
