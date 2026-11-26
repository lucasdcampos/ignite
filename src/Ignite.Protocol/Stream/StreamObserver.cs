using Google.Protobuf;
using Ignite.Protocol.Av;
using Ignite.Protocol.Crypto;
using Ignite.Protocol.Stream.Messages;
using Ignite.Protocol.Takion;
using static Ignite.Protocol.Stream.Messages.TakionMessage.Types;

namespace Ignite.Protocol.Stream;

/// <summary>
/// Follows a stream connection from the path between a client and the console without taking
/// part in it. STREAMINFO and DISCONNECT are read from the console's control messages, which
/// are not encrypted. Once given the console's key, video and audio are verified, decrypted and
/// reassembled for an attached sink (docs/architecture.md#watching-a-shared-console).
/// </summary>
public sealed class StreamObserver : IVideoReceiverSink, IAudioReceiverSink, IDisposable
{
    private const int AuthenticatedHeaderSize = 0x12;

    private readonly Lock _lock = new();
    private readonly KeyPosTracker _keyPos = new();
    private readonly TaskCompletionSource<string> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GkCrypt? _crypt;
    private IReadOnlyList<VideoStreamProfile>? _profiles;
    private AudioFormat? _audioFormat;
    private IStreamSink? _sink;
    private VideoReceiver? _video;
    private AudioReceiver? _audio;
    private bool _keyConfirmed;
    private bool _keyFrameRequested;

    /// <summary>The attached sink needs a key frame: it joined a running stream or lost frames.</summary>
    public event Action? KeyFrameNeeded;

    /// <summary>Completes with a reason when the console ends the stream or <see cref="End"/> is called.</summary>
    public Task<string> Ended => _ended.Task;

    public bool HasKey
    {
        get
        {
            lock (_lock)
                return _crypt is not null;
        }
    }

    /// <summary>True when STREAMINFO was seen and the key verified a packet of this stream, so a sink would get a picture.</summary>
    public bool CanWatch
    {
        get
        {
            lock (_lock)
                return _keyConfirmed && _profiles is not null && !_ended.Task.IsCompleted;
        }
    }

    /// <summary>Sets the console's key (<see cref="GkCrypt.ExportKeyMaterial"/>). Only the first key is kept.</summary>
    public void SetConsoleKey(ReadOnlySpan<byte> keyMaterial)
    {
        var crypt = GkCrypt.FromKeyMaterial(keyMaterial);
        bool needsKeyFrame;
        lock (_lock)
        {
            if (_crypt is not null)
            {
                crypt.Dispose();
                return;
            }

            _crypt = crypt;
            needsKeyFrame = TakeKeyFrameRequest();
        }

        if (needsKeyFrame)
            KeyFrameNeeded?.Invoke();
    }

    /// <summary>Starts delivering stream info, video and audio to <paramref name="sink"/>, replacing any earlier sink.</summary>
    public void Attach(IStreamSink sink)
    {
        bool needsKeyFrame;
        lock (_lock)
        {
            _sink = sink;
            _video = null;
            _audio = null;
            _keyFrameRequested = false;
            if (_profiles is not null)
                StartReceivers();
            needsKeyFrame = TakeKeyFrameRequest();
        }

        if (needsKeyFrame)
            KeyFrameNeeded?.Invoke();
    }

    public void Detach()
    {
        lock (_lock)
        {
            _sink = null;
            _video = null;
            _audio = null;
        }
    }

    /// <summary>Marks the stream as ended, for example because the client went away.</summary>
    public void End(string reason) => _ended.TrySetResult(reason);

    /// <summary>
    /// Handles a datagram the console sent to the client. It must already have been passed on:
    /// AV payloads are decrypted in place.
    /// </summary>
    public void OnConsoleDatagram(Span<byte> datagram)
    {
        if (datagram.IsEmpty)
            return;

        var needsKeyFrame = false;
        lock (_lock)
        {
            switch (TakionPackets.TypeOf(datagram))
            {
                case TakionPacketType.Control:
                    needsKeyFrame = HandleControl(datagram);
                    break;
                case TakionPacketType.Video or TakionPacketType.Audio:
                    HandleAv(datagram);
                    break;
            }
        }

        if (needsKeyFrame)
            KeyFrameNeeded?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _crypt?.Dispose();
            _crypt = null;
            _sink = null;
        }
    }

    void IVideoReceiverSink.OnFrame(ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte>? streamHeader, bool recoveredWithFec) =>
        _sink?.OnVideoFrame(frame, streamHeader, recoveredWithFec);

    // Called with the lock held, from HandleAv.
    void IVideoReceiverSink.OnCorruptFrames(ushort first, ushort last) => KeyFrameNeeded?.Invoke();

    void IAudioReceiverSink.OnAudioFrame(ushort frameIndex, ReadOnlySpan<byte> opusFrame, bool haptics) =>
        _sink?.OnAudioFrame(opusFrame, haptics);

    private bool HandleControl(ReadOnlySpan<byte> datagram)
    {
        // The console does not fragment its messages, so every DATA chunk is a whole message.
        if (!TakionPackets.TryParseControl(datagram, out var chunk)
            || chunk.Type != TakionChunkType.Data
            || chunk.Payload.Length < TakionPackets.DataHeaderSize
            || (TakionDataType)chunk.Payload[8] != TakionDataType.Protobuf)
        {
            return false;
        }

        TakionMessage message;
        try
        {
            message = TakionMessage.Parser.ParseFrom(chunk.Payload[TakionPackets.DataHeaderSize..]);
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }

        switch (message.Type)
        {
            case PayloadType.Streaminfo when message.StreamInfoPayload is { } info && _profiles is null:
                return OnStreamInfo(info);
            case PayloadType.Disconnect:
                var reason = message.DisconnectPayload?.Reason ?? "Disconnected";
                _ended.TrySetResult(reason);
                _sink?.OnDisconnected(reason);
                return false;
            default:
                return false;
        }
    }

    private bool OnStreamInfo(StreamInfoPayload info)
    {
        AudioFormat audio;
        try
        {
            audio = AudioFormat.Parse(info.AudioHeader.Span);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        _profiles = info.Resolution.Select(r => new VideoStreamProfile((int)r.Width, (int)r.Height, r.VideoHeader.ToByteArray())).ToList();
        _audioFormat = audio;
        if (_sink is null)
            return false;
        StartReceivers();
        return TakeKeyFrameRequest();
    }

    private void HandleAv(Span<byte> datagram)
    {
        if (_crypt is null || datagram.Length < AuthenticatedHeaderSize)
            return;

        var keyPos = _keyPos.Resolve(TakionPackets.ReadKeyPosLow(datagram));
        if (_sink is null && _keyConfirmed)
        {
            // Nobody watches: only keep the key position current for when someone does.
            _keyPos.Commit(keyPos);
            return;
        }

        if (!TakionPackets.Verify(_crypt, datagram, keyPos))
            return;
        _keyPos.Commit(keyPos);
        _keyConfirmed = true;

        if (_video is null || _audio is null || !AvPacket.TryParse(datagram, out var packet))
            return;
        var payload = datagram[packet.PayloadOffset..];
        _crypt.Transform(keyPos + GkCrypt.BlockSize, payload);
        if (packet.IsVideo)
            _video.OnPacket(packet, payload);
        else
            _audio.OnPacket(packet, payload);
    }

    private void StartReceivers()
    {
        _sink!.OnStreamInfo(_profiles!, _audioFormat!);
        _video = new VideoReceiver(_profiles!, this);
        _audio = new AudioReceiver(this);
    }

    /// <summary>A sink joining a running stream needs one key frame request once it can decode.</summary>
    private bool TakeKeyFrameRequest()
    {
        if (_keyFrameRequested || _sink is null || _crypt is null || _profiles is null)
            return false;
        _keyFrameRequested = true;
        return true;
    }
}
