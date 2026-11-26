using System.Buffers.Binary;
using Google.Protobuf;
using Ignite.Protocol.Crypto;
using Ignite.Protocol.Takion;

namespace Ignite.Relay.Tests;

/// <summary>Builds what a console sends on a stream connection, encrypted with its cipher.</summary>
internal sealed class ConsolePackets : IDisposable
{
    public static readonly byte[] VideoHeader = [0, 0, 0, 1, 0x40, 0x01];

    private const int StreamInfoType = 13;

    private ulong _keyPos;
    private uint _sequence = 1;

    public GkCrypt Crypt { get; } = new(GkCrypt.RemoteIndex, new byte[16], Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    public void Dispose() => Crypt.Dispose();

    public byte[] StreamInfo() =>
        TakionPackets.BuildControl(1, TakionChunkType.Data, 1, TakionPackets.BuildData(_sequence++, 1, TakionDataType.Protobuf, StreamInfoMessage()));

    // The generated messages are internal to Ignite.Protocol, so this one is written field by field:
    // TakionMessage { type = STREAMINFO, stream_info_payload = { resolution = [1280x720], audio_header } }.
    private static byte[] StreamInfoMessage()
    {
        var resolution = Encode(o =>
        {
            o.WriteTag(1, WireFormat.WireType.Varint);
            o.WriteUInt32(1280);
            o.WriteTag(2, WireFormat.WireType.Varint);
            o.WriteUInt32(720);
            o.WriteTag(3, WireFormat.WireType.LengthDelimited);
            o.WriteBytes(ByteString.CopyFrom(VideoHeader));
        });
        var info = Encode(o =>
        {
            o.WriteTag(1, WireFormat.WireType.LengthDelimited);
            o.WriteBytes(ByteString.CopyFrom(resolution));
            o.WriteTag(2, WireFormat.WireType.LengthDelimited);
            o.WriteBytes(ByteString.CopyFrom([2, 16, 0, 0, 0xBB, 0x80, 0, 0, 0x01, 0xE0, 0, 0, 0, 1]));
        });
        return Encode(o =>
        {
            o.WriteTag(1, WireFormat.WireType.Varint);
            o.WriteEnum(StreamInfoType);
            o.WriteTag(15, WireFormat.WireType.LengthDelimited);
            o.WriteBytes(ByteString.CopyFrom(info));
        });
    }

    private static byte[] Encode(Action<CodedOutputStream> write)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer, leaveOpen: true))
            write(output);
        return buffer.ToArray();
    }

    /// <summary>A whole frame in one unit without FEC.</summary>
    public byte[] Video(ushort frameIndex, byte[] frame)
    {
        var datagram = new byte[0x15 + 2 + frame.Length];
        datagram[0] = (byte)TakionPacketType.Video;
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(1), frameIndex);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(3), frameIndex);
        datagram[9] = 6;
        frame.CopyTo(datagram, 0x15 + 2);

        var keyPos = _keyPos;
        _keyPos += (ulong)(datagram.Length + (datagram.Length % 16));
        Crypt.Transform(keyPos + GkCrypt.BlockSize, datagram.AsSpan(0x15));
        TakionPackets.Sign(Crypt, datagram, keyPos);
        return datagram;
    }
}
