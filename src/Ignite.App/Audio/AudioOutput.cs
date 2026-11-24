using Hexa.NET.SDL3;

namespace Ignite.App.Audio;

/// <summary>
/// Plays interleaved 16-bit PCM through an SDL audio stream, dropping queued audio when the
/// queue grows beyond the latency budget.
/// </summary>
internal sealed unsafe class AudioOutput : IDisposable
{
    private const uint DefaultPlaybackDevice = 0xFFFFFFFF;
    private static readonly TimeSpan MaxQueuedLatency = TimeSpan.FromMilliseconds(80);

    private readonly SDLAudioStreamPtr _stream;
    private readonly int _maxQueuedBytes;

    public AudioOutput(int sampleRate, int channels)
    {
        var spec = new SDLAudioSpec { Format = SDLAudioFormat.S16Le, Channels = channels, Freq = sampleRate };
        _stream = SDL.OpenAudioDeviceStream(DefaultPlaybackDevice, &spec, (SDLAudioStreamCallback?)null, null);
        if (_stream.IsNull)
            throw new InvalidOperationException("Could not open the audio device.");
        _maxQueuedBytes = (int)(sampleRate * channels * sizeof(short) * MaxQueuedLatency.TotalSeconds);
        SDL.ResumeAudioStreamDevice(_stream);
    }

    /// <summary>Thread safe; called from the network thread.</summary>
    public void Play(ReadOnlySpan<short> samples)
    {
        if (SDL.GetAudioStreamQueued(_stream) > _maxQueuedBytes)
            SDL.ClearAudioStream(_stream);
        fixed (short* data = samples)
            SDL.PutAudioStreamData(_stream, data, samples.Length * sizeof(short));
    }

    public void Dispose() => SDL.DestroyAudioStream(_stream);
}
