namespace Ignite.Relay;

/// <summary>Sees what the console sends on one relayed stream connection.</summary>
internal interface IUdpTap : IDisposable
{
    /// <summary>Called after the datagram was passed on to the client; it may be changed.</summary>
    void OnConsoleDatagram(Span<byte> datagram);
}
