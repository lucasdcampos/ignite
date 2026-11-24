using Ignite.Protocol.Av;

namespace Ignite.Protocol.Stream;

public readonly record struct TriggerEffect(byte Type, byte[] Parameters);

public readonly record struct PadInfo(byte PlayerIndex, byte Red, byte Green, byte Blue, bool MotionReset, byte HapticIntensity, byte TriggerIntensity);

/// <summary>Receives decoded stream events. Called from the network receive thread; keep handlers short.</summary>
public interface IStreamSink
{
    void OnStreamInfo(IReadOnlyList<VideoStreamProfile> profiles, AudioFormat audio);

    void OnVideoFrame(ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte>? streamHeader, bool recoveredWithFec);

    void OnAudioFrame(ReadOnlySpan<byte> opusFrame, bool haptics);

    void OnRumble(byte left, byte right);

    void OnTriggerEffects(TriggerEffect left, TriggerEffect right);

    void OnPadInfo(PadInfo info);

    void OnDisconnected(string reason);
}
