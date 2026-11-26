using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ignite.Protocol.Discovery;
using Ignite.Protocol.Net;
using Ignite.Protocol.Registration;
using Ignite.Protocol.Session;
using Ignite.Protocol.Stream;

namespace Ignite.Relay;

/// <summary>
/// Makes a console on this computer's network reachable from other networks: this computer
/// listens on every Remote Play port and forwards to the console, so remote clients connect to
/// it as if it were the console (docs/architecture.md#console-sharing).
/// </summary>
public sealed class ConsoleRelay : IConsoleRelay
{
    private readonly List<IDisposable> _relays = [];
    private readonly List<UdpRelay> _udp = [];
    private readonly List<TcpRelay> _tcp = [];
    private readonly RelayedSessions _sessions = new();
    private volatile RelayClientFilter _filter;

    private ConsoleRelay(IPAddress console)
    {
        Console = console;
        _filter = RelayClientFilter.ForThisComputer(console);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public IPAddress Console { get; }

    public IReadOnlyList<IPAddress> ShareAddresses => _filter.ShareAddresses;

    public IReadOnlyCollection<IPAddress> Clients =>
        _udp.SelectMany(r => r.Clients).Concat(_tcp.SelectMany(r => r.Clients)).ToHashSet();

    public IRelayedSession? WatchableSession => _sessions.Watchable;

    /// <exception cref="RelayException">A Remote Play port is already in use on this computer.</exception>
    public static ConsoleRelay Start(IPAddress console)
    {
        var relay = new ConsoleRelay(console);
        try
        {
            relay.AddUdp(DiscoveryProtocol.ConsolePort, broadcast: false);
            relay.AddUdp(RegistrationClient.DefaultPort, broadcast: true);
            relay.AddTcp(CtrlChannel.DefaultPort);
            relay.AddUdp(StreamConnection.DefaultPort, broadcast: false, relay._sessions.Tap);
            relay.AddUdp(Senkusha.DefaultPort, broadcast: false);
            relay.TryAddSpectatorListener();
            return relay;
        }
        catch
        {
            relay.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        foreach (var relay in _relays)
            relay.Dispose();
    }

    private void AddUdp(int port, bool broadcast, Func<IPEndPoint, IUdpTap>? tap = null)
    {
        var broadcastAddress = broadcast ? LocalNetwork.BroadcastAddressFor(Console) : null;
        var relay = Listen("UDP", port, () => new UdpRelay(
            new IPEndPoint(IPAddress.Any, port),
            new IPEndPoint(Console, port),
            broadcastAddress is null ? null : new IPEndPoint(broadcastAddress, port),
            Accepts,
            tap: tap));
        _udp.Add(relay);
    }

    // Watching is optional: sharing still works when another program holds the spectator port.
    private void TryAddSpectatorListener()
    {
        try
        {
            _relays.Add(new SpectatorListener(new IPEndPoint(IPAddress.Any, SpectatorLink.Port), Accepts, _sessions.Offer));
        }
        catch (SocketException)
        {
        }
    }

    private void AddTcp(int port)
    {
        var relay = Listen("TCP", port, () => new TcpRelay(new IPEndPoint(IPAddress.Any, port), new IPEndPoint(Console, port), Accepts));
        _tcp.Add(relay);
    }

    private T Listen<T>(string protocol, int port, Func<T> create)
        where T : IDisposable
    {
        try
        {
            var relay = create();
            _relays.Add(relay);
            return relay;
        }
        catch (SocketException e)
        {
            throw new RelayException($"{protocol} port {port} is not available on this computer ({e.Message}). Close other Remote Play apps and try again.", e);
        }
    }

    private bool Accepts(IPAddress client) => _filter.Accepts(client);

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => _filter = RelayClientFilter.ForThisComputer(Console);
}
