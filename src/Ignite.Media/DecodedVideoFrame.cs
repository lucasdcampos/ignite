namespace Ignite.Media;

public enum VideoPixelFormat
{
    /// <summary>8-bit, Y plane followed by interleaved UV.</summary>
    Nv12,

    /// <summary>8-bit planar Y, U, V.</summary>
    Yuv420P,

    /// <summary>10-bit in the high bits of 16-bit words, Y plane followed by interleaved UV.</summary>
    P010,
}

/// <summary>How the YUV samples map to colors. Both are limited range, as the console sends them.</summary>
public enum VideoColorSpace
{
    /// <summary>SDR: BT.709 primaries, transfer and matrix.</summary>
    Bt709,

    /// <summary>HDR10: BT.2020 primaries and matrix with the PQ (SMPTE ST 2084) transfer.</summary>
    Bt2020Pq,
}

/// <summary>
/// A decoded picture in system memory. The decoder reuses the buffers, so consumers copy or
/// upload the data before returning from the callback.
/// </summary>
public sealed class DecodedVideoFrame
{
    internal DecodedVideoFrame()
    {
    }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public VideoPixelFormat Format { get; internal set; }

    public VideoColorSpace ColorSpace { get; internal set; }

    public bool HardwareDecoded { get; internal set; }

    /// <summary>Planes: Y and UV for semi-planar formats, Y, U and V for planar ones.</summary>
    public IReadOnlyList<VideoPlane> Planes => _planes;

    internal readonly List<VideoPlane> _planes = [];
}

public readonly record struct VideoPlane(ReadOnlyMemory<byte> Data, int Stride);
