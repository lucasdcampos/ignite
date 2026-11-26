using Ignite.Protocol.Crypto;
using Ignite.Protocol.Session;

namespace Ignite.Relay;

/// <summary>What the relay tells a playing client over the spectator channel, one byte each.</summary>
internal enum SpectatorSignal : byte
{
    Watching = 1,
    NotWatching = 2,
    KeyFrame = 3,
}

/// <summary>
/// The only message a playing client sends on the spectator channel: its stream's codec and the
/// console's key material (docs/architecture.md#watching-a-shared-console).
/// </summary>
internal static class SpectatorOffer
{
    public const byte Version = 1;
    public const int Size = 4 + 1 + 1 + GkCrypt.KeyMaterialSize;

    private static ReadOnlySpan<byte> Magic => "IGNW"u8;

    public static byte[] Encode(VideoCodec codec, ReadOnlySpan<byte> consoleKey)
    {
        if (consoleKey.Length != GkCrypt.KeyMaterialSize)
            throw new ArgumentException($"The console key must be {GkCrypt.KeyMaterialSize} bytes.", nameof(consoleKey));
        var offer = new byte[Size];
        Magic.CopyTo(offer);
        offer[4] = Version;
        offer[5] = (byte)codec;
        consoleKey.CopyTo(offer.AsSpan(6));
        return offer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> offer, out VideoCodec codec, out byte[] consoleKey)
    {
        codec = default;
        consoleKey = [];
        if (offer.Length != Size || !offer[..4].SequenceEqual(Magic) || offer[4] != Version || !Enum.IsDefined((VideoCodec)offer[5]))
            return false;
        codec = (VideoCodec)offer[5];
        consoleKey = offer[6..].ToArray();
        return true;
    }
}
