using System.Net;
using Ignite.Core.Account;
using Ignite.Core.Consoles;
using Ignite.Core.Settings;
using Ignite.Core.Storage;
using Ignite.Protocol;
using Ignite.Protocol.Discovery;
using Ignite.Protocol.Registration;
using Ignite.Protocol.Session;

namespace Ignite.Core.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ignite-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var paths = new AppPaths(_directory);
        var settings = new AppSettings { Quality = new StreamQuality(Resolution.P1440, 60, VideoCodec.HevcHdr, 40000), VSync = false };

        new SettingsStore(paths).Save(settings);
        var loaded = new SettingsStore(paths).Current;

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void Settings_FallBackToDefaultsWhenFileIsCorrupt()
    {
        var paths = new AppPaths(_directory);
        File.WriteAllText(paths.SettingsFile, "{ not json");

        Assert.Equal(new AppSettings(), new SettingsStore(paths).Current);
    }

    [Theory]
    [InlineData(Resolution.P720, null, 1280, 720, 10000)]
    [InlineData(Resolution.P1080, 25000, 1920, 1080, 25000)]
    [InlineData(Resolution.P2160, null, 3840, 2160, 50000)]
    public void StreamQuality_MapsToStreamOptions(Resolution resolution, int? bitrate, int width, int height, int expectedBitrate)
    {
        var options = new StreamQuality(resolution, 60, VideoCodec.H264, bitrate).ToStreamOptions(dualSense: true, maxReportedLoss: 0.1);

        Assert.Equal(width, options.Width);
        Assert.Equal(height, options.Height);
        Assert.Equal(expectedBitrate, options.BitrateKbps);
        Assert.Equal(0.1, options.MaxReportedPacketLoss);
    }

    [Fact]
    public void ConsoleRegistry_StoresProtectedCredentials()
    {
        var paths = new AppPaths(_directory);
        var registered = new RegisteredConsole(
            "1a2b3c4d\0\0\0\0\0\0\0\0"u8.ToArray(), Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), 2, [1, 2, 3, 4, 5, 6], "Living Room");

        new ConsoleRegistry(paths).Save(StoredConsole.From("ABC", "192.168.0.10", registered, "PS5"));
        var stored = new ConsoleRegistry(paths).Find("abc");

        Assert.NotNull(stored);
        Assert.Equal("Living Room", stored.Nickname);
        Assert.Equal("010203040506", stored.MacAddress);
        var credentials = stored.ToCredentials();
        Assert.Equal(registered.RegistKey, credentials.RegistKey);
        Assert.Equal(registered.RpKey, credentials.RpKey);
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain(Convert.ToBase64String(registered.RpKey), File.ReadAllText(paths.ConsolesFile), StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleRegistry_RemovesConsole()
    {
        var registry = new ConsoleRegistry(new AppPaths(_directory));
        registry.Save(new StoredConsole("A", "A", null, "00", "", "", 0));

        registry.Remove("A");

        Assert.Empty(new ConsoleRegistry(new AppPaths(_directory)).Consoles);
    }

    [Fact]
    public void AccountStore_RoundTrip()
    {
        var paths = new AppPaths(_directory);
        var id = PsnAccountId.FromUserId(123456789);

        new AccountStore(paths).Save(id, "player");
        var store = new AccountStore(paths);

        Assert.Equal(id, store.AccountId);
        Assert.Equal("player", store.OnlineName);
        store.Clear();
        Assert.Null(new AccountStore(paths).AccountId);
    }

    [Fact]
    public async Task ConsoleBrowser_MergesDiscoveredAndRegisteredConsoles()
    {
        var registry = new ConsoleRegistry(new AppPaths(_directory));
        registry.Save(new StoredConsole("REG", "My PS5", "10.0.0.5", "00", "", "", 0));
        registry.Save(new StoredConsole("OFF", "Old PS5", null, "00", "", "", 0));
        await using var browser = new ConsoleBrowser(registry);

        browser.Observe(Discovered("REG", "10.0.0.7", ConsoleState.Standby, "Renamed"));
        browser.Observe(Discovered("NEW", "10.0.0.8", ConsoleState.Ready, "Friend PS5"));
        browser.Observe(Discovered("PS4", "10.0.0.9", ConsoleState.Ready, "PS4", ps5: false));
        var entries = browser.Snapshot();

        Assert.Equal(["Friend PS5", "My PS5", "Old PS5"], entries.Select(e => e.Name));
        var registered = entries.Single(e => e.HostId == "REG");
        Assert.True(registered.Registered);
        Assert.Equal(ConsoleState.Standby, registered.State);
        Assert.Equal(IPAddress.Parse("10.0.0.7"), registered.Address);
        Assert.Equal("10.0.0.7", registry.Find("REG")!.LastAddress);
        Assert.False(entries.Single(e => e.HostId == "NEW").Registered);
    }

    private static DiscoveredConsole Discovered(string hostId, string address, ConsoleState state, string name, bool ps5 = true) =>
        new(IPAddress.Parse(address), state, hostId, name, "PS5", "09400004", ps5 ? DiscoveryProtocol.ProtocolVersion : "00020020", 997, null, null);
}
