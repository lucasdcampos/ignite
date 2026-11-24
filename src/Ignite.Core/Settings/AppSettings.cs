using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.Core.Settings;

public enum Resolution
{
    P360,
    P540,
    P720,
    P1080,
    P1440,
    P2160,
}

public sealed record StreamQuality(
    Resolution Resolution = Resolution.P1080,
    int Fps = 60,
    VideoCodec Codec = VideoCodec.Hevc,
    int? BitrateKbps = null)
{
    public (int Width, int Height) Size => Resolution switch
    {
        Resolution.P360 => (640, 360),
        Resolution.P540 => (960, 540),
        Resolution.P720 => (1280, 720),
        Resolution.P1080 => (1920, 1080),
        Resolution.P1440 => (2560, 1440),
        Resolution.P2160 => (3840, 2160),
        _ => throw new ArgumentOutOfRangeException(nameof(Resolution)),
    };

    /// <summary>Default bitrate per resolution when none is set, scaled for 60 fps.</summary>
    public int EffectiveBitrateKbps => BitrateKbps ?? Resolution switch
    {
        Resolution.P360 => 2000,
        Resolution.P540 => 6000,
        Resolution.P720 => 10000,
        Resolution.P1080 => 15000,
        Resolution.P1440 => 30000,
        Resolution.P2160 => 50000,
        _ => 15000,
    };

    public StreamOptions ToStreamOptions(bool dualSense, double maxReportedLoss)
    {
        var (width, height) = Size;
        return new StreamOptions(width, height, Fps, EffectiveBitrateKbps, Codec, dualSense, maxReportedLoss);
    }
}

public sealed record AppSettings
{
    public StreamQuality Quality { get; init; } = new();

    public bool HardwareDecoding { get; init; } = true;

    public bool VSync { get; init; } = true;

    public bool Fullscreen { get; init; }

    /// <summary>Upper bound of the loss ratio reported to the console (docs/protocol/14-congestion.md).</summary>
    public double MaxReportedPacketLoss { get; init; } = 0.05;

    public string? FfmpegPath { get; init; }

    public string? PsnClientId { get; init; }

    public string? PsnClientSecret { get; init; }
}
