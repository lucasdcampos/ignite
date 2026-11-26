using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Ignite.App.Audio;
using Ignite.App.Video;
using Ignite.Core.Consoles;
using Ignite.Core.Settings;
using Ignite.Media;
using Ignite.Protocol;
using Ignite.Protocol.Av;
using Ignite.Protocol.Crypto;
using Ignite.Protocol.Input;
using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.App.Streaming;

internal enum StreamState
{
    Idle,
    Connecting,
    Streaming,
    Ended,
}

internal sealed record StreamStatistics(double Fps, double VideoMbps, long FecRecoveries, string? Decoder, int Width, int Height, int RttMs);

/// <summary>
/// Runs one Remote Play session: connects, decodes video on its own thread, plays audio and
/// forwards controller input. Console feedback for the gamepad is queued for the UI thread.
/// </summary>
internal sealed class StreamController : IStreamSink, IAsyncDisposable
{
    private readonly VideoPresenter _presenter;
    private readonly HapticsOutput _haptics;
    private readonly Channel<(byte[] Frame, byte[]? Header)> _frames =
        Channel.CreateBounded<(byte[], byte[]?)>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _stopLock = new();
    private Task? _stopping;
    private readonly Stopwatch _statsClock = Stopwatch.StartNew();
    private RemotePlaySession? _session;
    private VideoDecoder? _decoder;
    private OpusAudioDecoder? _audioDecoder;
    private AudioOutput? _audio;
    private Task? _decodeLoop;
    private TaskCompletionSource<string?>? _passcodeRequest;
    private VideoCodecKind _codec;
    private bool _hardwareDecoding;
    private volatile bool _decodeOnRenderThread;
    private long _videoBytes;
    private long _fecRecoveries;
    private long _framesAtLastStats;
    private long _bytesAtLastStats;
    private StreamStatistics? _statistics;

    public StreamController(VideoPresenter presenter, HapticsOutput haptics)
    {
        _presenter = presenter;
        _haptics = haptics;
    }

    public StreamState State { get; private set; }

    public string? Message { get; private set; }

    public bool IsStopping => _stop.IsCancellationRequested;

    public bool PasscodeRequested => _passcodeRequest is { Task.IsCompleted: false };

    public bool PasscodeRetry { get; private set; }

    /// <summary>Gamepad feedback from the console, applied by the UI thread.</summary>
    public ConcurrentQueue<Action<Input.GamepadInput>> GamepadFeedback { get; } = new();

    public void Start(RpCryptTables tables, IPAddress address, StoredConsole console, AppSettings settings, bool dualSense)
    {
        State = StreamState.Connecting;
        Message = $"Connecting to {console.Nickname}...";
        _codec = settings.Quality.Codec == VideoCodec.H264 ? VideoCodecKind.H264 : VideoCodecKind.Hevc;
        _hardwareDecoding = settings.HardwareDecoding && VideoDecoder.SupportsHardware(_codec);
        var options = settings.Quality.ToStreamOptions(dualSense, settings.MaxReportedPacketLoss);
        _ = Task.Run(() => ConnectAsync(tables, address, console, options));
    }

    public void AnswerPasscode(string? passcode) => _passcodeRequest?.TrySetResult(passcode);

    public void UpdateInput(in ControllerState state) => _session?.Stream.UpdateController(state);

    public StreamStatistics? Statistics()
    {
        var elapsed = _statsClock.Elapsed.TotalSeconds;
        if (elapsed >= 1 && _session is not null)
        {
            var frames = _presenter.FramesPresented;
            var bytes = Interlocked.Read(ref _videoBytes);
            var (width, height) = _presenter.VideoSize;
            _statistics = new StreamStatistics(
                (frames - _framesAtLastStats) / elapsed,
                (bytes - _bytesAtLastStats) * 8 / elapsed / 1_000_000,
                Interlocked.Read(ref _fecRecoveries),
                DescribeDecoder(),
                width,
                height,
                _session.Network.RttMilliseconds);
            _framesAtLastStats = frames;
            _bytesAtLastStats = bytes;
            _statsClock.Restart();
        }

        return _statistics;
    }

    /// <summary>Ends the session; safe to call several times and from any thread.</summary>
    public Task StopAsync()
    {
        lock (_stopLock)
            return _stopping ??= StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        _passcodeRequest?.TrySetResult(null);
        await _stop.CancelAsync();
        _frames.Writer.TryComplete();
        if (_session is not null)
            await _session.DisposeAsync();
        _session = null;
        if (_decodeLoop is not null)
            await _decodeLoop;
        if (State != StreamState.Ended)
        {
            // The user ended the stream: nothing to report.
            State = StreamState.Ended;
            Message = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _decoder?.Dispose();
        _audioDecoder?.Dispose();
        _audio?.Dispose();
        _stop.Dispose();
    }

    void IStreamSink.OnStreamInfo(IReadOnlyList<VideoStreamProfile> profiles, AudioFormat audio)
    {
        // Sharing the renderer's single-threaded device means decoding on the render thread.
        _decodeOnRenderThread = _hardwareDecoding && _presenter.SharedDevice != 0;
        if (!_decodeOnRenderThread)
            _decoder = new VideoDecoder(_codec, new VideoDecoderOptions(_hardwareDecoding ? HardwareAcceleration.Auto : HardwareAcceleration.None));
        var channels = audio.Channels is >= 1 and <= 8 ? audio.Channels : 2;
        var sampleRate = audio.SampleRate > 0 ? audio.SampleRate : 48000;
        _audioDecoder = new OpusAudioDecoder(sampleRate, channels);
        _audio = new AudioOutput(sampleRate, channels);
        if (!_decodeOnRenderThread)
            _decodeLoop = Task.Factory.StartNew(DecodeLoop, TaskCreationOptions.LongRunning);
    }

    /// <summary>Called by the render loop: decodes queued frames when they stay on the GPU.</summary>
    public void DecodePending()
    {
        if (!_decodeOnRenderThread || _stop.IsCancellationRequested)
            return;
        _decoder ??= new VideoDecoder(_codec, new VideoDecoderOptions(HardwareAcceleration.Auto, _presenter.SharedDevice));
        while (_frames.Reader.TryRead(out var item))
            Decode(_decoder, item);
    }

    void IStreamSink.OnVideoFrame(ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte>? streamHeader, bool recoveredWithFec)
    {
        Interlocked.Add(ref _videoBytes, frame.Length);
        if (recoveredWithFec)
            Interlocked.Increment(ref _fecRecoveries);
        _frames.Writer.TryWrite((frame.ToArray(), streamHeader?.ToArray()));
    }

    void IStreamSink.OnAudioFrame(ReadOnlySpan<byte> frame, bool haptics)
    {
        if (haptics)
        {
            _haptics.Play(frame);
            return;
        }
        if (_audioDecoder is null || _audio is null)
            return;
        _audio.Play(_audioDecoder.Decode(frame));
    }

    void IStreamSink.OnRumble(byte left, byte right) => GamepadFeedback.Enqueue(g =>
    {
        if (_haptics.Available)
            g.Rumble(0, 0);
        else
            g.Rumble(left, right);
    });

    void IStreamSink.OnTriggerEffects(TriggerEffect left, TriggerEffect right) =>
        GamepadFeedback.Enqueue(g => g.SetTriggerEffects(left, right));

    void IStreamSink.OnPadInfo(PadInfo info) => GamepadFeedback.Enqueue(g => g.SetLightbar(info.Red, info.Green, info.Blue));

    void IStreamSink.OnDisconnected(string reason)
    {
        if (!IsStopping)
            Message = StreamEndMessages.ForDisconnect(reason);
    }

    private async Task ConnectAsync(RpCryptTables tables, IPAddress address, StoredConsole console, StreamOptions options)
    {
        try
        {
            _session = await RemotePlaySession.ConnectAsync(
                tables, address, console.ToCredentials(), options, this, RequestPasscodeAsync, _stop.Token);
            State = StreamState.Streaming;
            Message = null;
            var reason = await _session.WaitForEndAsync();
            Message ??= StreamEndMessages.ForEnd(reason);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is RemotePlayException or IOException or System.Net.Sockets.SocketException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            Message = StreamEndMessages.ForFailure(e);
        }

        if (IsStopping)
            Message = null;
        State = StreamState.Ended;
        // Release the session right away so the console stops streaming and can accept the next one.
        await StopAsync();
    }

    private string DescribeDecoder() => _decoder switch
    {
        null => "starting",
        { KeepsFramesOnGpu: true } => $"{_decoder.HardwareDevice} (zero-copy)",
        { HardwareDevice: { } device } => $"{device} (copy-back)",
        _ => "software",
    };

    private Task<string?> RequestPasscodeAsync(bool previousAttemptFailed, CancellationToken cancellationToken)
    {
        PasscodeRetry = previousAttemptFailed;
        _passcodeRequest = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => _passcodeRequest.TrySetResult(null));
        return _passcodeRequest.Task;
    }

    private void DecodeLoop()
    {
        var reader = _frames.Reader;
        while (!_stop.IsCancellationRequested)
        {
            if (!reader.TryRead(out var item))
            {
                try
                {
                    if (!reader.WaitToReadAsync(_stop.Token).AsTask().GetAwaiter().GetResult())
                        return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            Decode(_decoder!, item);
        }
    }

    private void Decode(VideoDecoder decoder, (byte[] Frame, byte[]? Header) item)
    {
        if (item.Header is { } header)
            decoder.Decode([.. header, .. item.Frame], _presenter);
        else
            decoder.Decode(item.Frame, _presenter);
    }
}
