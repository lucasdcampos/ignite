using System.Diagnostics;
using Ignite.Protocol;

namespace Ignite.App.Streaming;

/// <summary>
/// Retries a connection while the console reports Remote Play as in use. A PS5 does so for
/// several seconds after the previous session ended (docs/protocol/04-registration.md#error-codes-rp-application-reason).
/// </summary>
internal static class ConnectRetry
{
    public static async Task<T> WhileInUseAsync<T>(
        Func<CancellationToken, Task<T>> connect,
        TimeSpan timeout,
        TimeSpan interval,
        Action onWaiting,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return await connect(cancellationToken);
            }
            catch (RemotePlayException e) when (e.Error == RemotePlayError.InUse && clock.Elapsed + interval < timeout)
            {
                onWaiting();
                await Task.Delay(interval, cancellationToken);
            }
        }
    }
}
