using Velopack;
using Velopack.Sources;

namespace Ignite.Core.Updates;

/// <summary>Updates from the GitHub releases of the Ignite repository, packaged with Velopack.</summary>
public sealed class VelopackUpdateFeed : IUpdateFeed
{
    public const string Repository = "https://github.com/lucasdcampos/ignite";

    /// <summary>Replaces the GitHub releases with a folder or URL, to try an update before publishing it.</summary>
    public const string SourceVariable = "IGNITE_UPDATE_SOURCE";

    private readonly UpdateManager _manager;
    private UpdateInfo? _update;

    public VelopackUpdateFeed()
    {
        var custom = Environment.GetEnvironmentVariable(SourceVariable);
        _manager = string.IsNullOrEmpty(custom)
            ? new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: false))
            : new UpdateManager(custom);
    }

    public string? CurrentVersion => _manager.IsInstalled ? _manager.CurrentVersion?.ToString() : null;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        _update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        return _update?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        var update = _update ?? throw new InvalidOperationException("No update was found to download.");
        return _manager.DownloadUpdatesAsync(update, progress, cancellationToken);
    }

    public void InstallAfterExit() => _manager.WaitExitThenApplyUpdates(_update?.TargetFullRelease, silent: false, restart: true);
}
