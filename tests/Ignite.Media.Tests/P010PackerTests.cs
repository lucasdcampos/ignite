using System.Buffers.Binary;

namespace Ignite.Media.Tests;

public class P010PackerTests
{
    [Fact]
    public void PackLuma_MovesSamplesToHighBitsAndDropsStridePadding()
    {
        // 3x2 picture, source rows padded to 8 bytes, destination rows to 6 bytes.
        var source = Samples(8, [0x000, 0x3FF, 0x200, 0xDEAD], [0x001, 0x040, 0x3FE, 0xBEEF]);
        var destination = new byte[12];

        P010Packer.PackLuma(source, 8, destination, 6, width: 3, height: 2);

        Assert.Equal(Samples(6, [0x0000, 0xFFC0, 0x8000], [0x0040, 0x1000, 0xFF80]), destination);
    }

    [Fact]
    public void PackChroma_InterleavesUAndV()
    {
        var u = Samples(4, [0x100, 0x3FF], [0x001, 0x000]);
        var v = Samples(4, [0x200, 0x002], [0x3FE, 0x080]);
        var destination = new byte[16];

        P010Packer.PackChroma(u, v, 4, destination, 8, chromaWidth: 2, chromaHeight: 2);

        Assert.Equal(Samples(8, [0x4000, 0x8000, 0xFFC0, 0x0080], [0x0040, 0xFF80, 0x0000, 0x2000]), destination);
    }

    private static byte[] Samples(int stride, params ushort[][] rows)
    {
        var bytes = new byte[stride * rows.Length];
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((y * stride) + (x * 2)), rows[y][x]);
        }

        return bytes;
    }
}
