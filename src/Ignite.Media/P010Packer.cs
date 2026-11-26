using System.Runtime.InteropServices;

namespace Ignite.Media;

/// <summary>
/// Repacks FFmpeg's planar 10-bit 4:2:0 (yuv420p10le, samples in the low bits) into P010
/// (samples in the high bits, interleaved UV), the only 10-bit layout SDL textures accept.
/// </summary>
internal static class P010Packer
{
    private const int Shift = 6;

    public static void PackLuma(ReadOnlySpan<byte> source, int sourceStride, Span<byte> destination, int destinationStride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var input = MemoryMarshal.Cast<byte, ushort>(source.Slice(y * sourceStride, width * 2));
            var output = MemoryMarshal.Cast<byte, ushort>(destination.Slice(y * destinationStride, width * 2));
            for (var x = 0; x < width; x++)
                output[x] = (ushort)(input[x] << Shift);
        }
    }

    public static void PackChroma(ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int sourceStride, Span<byte> destination, int destinationStride, int chromaWidth, int chromaHeight)
    {
        for (var y = 0; y < chromaHeight; y++)
        {
            var inputU = MemoryMarshal.Cast<byte, ushort>(u.Slice(y * sourceStride, chromaWidth * 2));
            var inputV = MemoryMarshal.Cast<byte, ushort>(v.Slice(y * sourceStride, chromaWidth * 2));
            var output = MemoryMarshal.Cast<byte, ushort>(destination.Slice(y * destinationStride, chromaWidth * 4));
            for (var x = 0; x < chromaWidth; x++)
            {
                output[2 * x] = (ushort)(inputU[x] << Shift);
                output[(2 * x) + 1] = (ushort)(inputV[x] << Shift);
            }
        }
    }
}
