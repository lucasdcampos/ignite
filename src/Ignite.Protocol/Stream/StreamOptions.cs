using Ignite.Protocol.Session;

namespace Ignite.Protocol.Stream;

/// <summary>Requested stream quality (docs/protocol/09-stream-connection.md#launch-spec).</summary>
public sealed record StreamOptions(
    int Width,
    int Height,
    int MaxFps,
    int BitrateKbps,
    VideoCodec Codec,
    bool DualSense = true,
    double MaxReportedPacketLoss = 0.05)
{
    public bool IsHevc => Codec is VideoCodec.Hevc or VideoCodec.HevcHdr;

    public bool IsHdr => Codec == VideoCodec.HevcHdr;
}
