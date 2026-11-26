using System.Net;
using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.Relay;

/// <summary>A remote client's stream that passes through the relay and can be watched on this computer.</summary>
public interface IRelayedSession
{
    /// <summary>The playing client.</summary>
    IPAddress Client { get; }

    VideoCodec Codec { get; }

    /// <summary>Completes with a reason when the stream ends.</summary>
    Task<string> Ended { get; }

    /// <summary>Shows the stream on <paramref name="sink"/> until the result is disposed. Only video and audio are delivered.</summary>
    IDisposable Watch(IStreamSink sink);
}
