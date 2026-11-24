using Ignite.Core.Account;
using Ignite.Core.Consoles;
using Ignite.Core.Settings;
using Ignite.Core.Storage;
using Ignite.Media;
using Ignite.Protocol.Crypto;
using Ignite.Psn;

namespace Ignite.App;

/// <summary>Application-wide services built at start-up.</summary>
internal sealed class AppServices : IAsyncDisposable
{
    public AppServices()
    {
        Paths = AppPaths.Default();
        Settings = new SettingsStore(Paths);
        Consoles = new ConsoleRegistry(Paths);
        Account = new AccountStore(Paths);
        Browser = new ConsoleBrowser(Consoles);

        try
        {
            FfmpegVersion = FfmpegLibrary.Load(Settings.Current.FfmpegPath);
        }
        catch (Exception e) when (e is DllNotFoundException or NotSupportedException or EntryPointNotFoundException or InvalidOperationException)
        {
            StartupProblems.Add($"FFmpeg could not be loaded: {e.Message} Reinstall Ignite or set the FFmpeg path in the settings.");
        }

        Browser.Start();
    }

    public AppPaths Paths { get; }

    public SettingsStore Settings { get; }

    public ConsoleRegistry Consoles { get; }

    public AccountStore Account { get; }

    public ConsoleBrowser Browser { get; }

    public RpCryptTables Tables { get; } = RpCryptTables.Default;

    public string? FfmpegVersion { get; }

    public List<string> StartupProblems { get; } = [];

    public PsnOAuthOptions PsnOptions => PsnOAuthOptions.Resolve(Settings.Current.PsnClientId, Settings.Current.PsnClientSecret);

    public ValueTask DisposeAsync() => Browser.DisposeAsync();
}
