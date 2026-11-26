using Ignite.Core.Updates;

namespace Ignite.Core.Tests;

public sealed class AppUpdaterTests
{
    private readonly FakeFeed _feed = new();

    [Fact]
    public async Task DevelopmentBuildsNeverLookForUpdates()
    {
        _feed.CurrentVersion = null;
        using var updater = new AppUpdater(_feed);

        await updater.CheckAsync();

        Assert.Equal(0, _feed.Checks);
        Assert.Equal(UpdateState.UpToDate, updater.State);
    }

    [Fact]
    public async Task FindsANewerVersionButDownloadsOnlyWhenAsked()
    {
        _feed.Newer = "1.1.0";
        using var updater = new AppUpdater(_feed);

        await updater.CheckAsync();

        Assert.Equal(UpdateState.Available, updater.State);
        Assert.Equal("1.1.0", updater.AvailableVersion);
        Assert.Equal(0, _feed.Downloads);
    }

    [Fact]
    public async Task StaysUpToDateWhenNothingNewerOrTheCheckFails()
    {
        using var updater = new AppUpdater(_feed);
        await updater.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, updater.State);

        _feed.CheckFailure = new HttpRequestException("offline");
        await updater.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, updater.State);
        Assert.Null(updater.Error);

        updater.Download();
        Assert.Equal(0, _feed.Downloads);
    }

    [Fact]
    public async Task ReportsDownloadProgressThenInstallsOnlyAfterTheUserRestarts()
    {
        _feed.Newer = "1.1.0";
        using var updater = new AppUpdater(_feed);
        await updater.CheckAsync();
        var download = new TaskCompletionSource();
        _feed.Download = download.Task;

        var downloading = updater.DownloadAsync();
        _feed.ReportProgress(40);

        Assert.Equal(UpdateState.Downloading, updater.State);
        Assert.Equal(40, updater.Progress);
        updater.RequestInstall();
        Assert.False(updater.InstallRequested);

        download.SetResult();
        await downloading;
        Assert.Equal(UpdateState.Ready, updater.State);
        updater.InstallIfRequested();
        Assert.Equal(0, _feed.Installs);

        updater.RequestInstall();
        Assert.True(updater.InstallRequested);
        updater.InstallIfRequested();
        Assert.Equal(1, _feed.Installs);
    }

    [Fact]
    public async Task AFailedDownloadShowsItsErrorAndCanBeRetried()
    {
        _feed.Newer = "1.1.0";
        using var updater = new AppUpdater(_feed);
        await updater.CheckAsync();
        _feed.Download = Task.FromException(new IOException("disk full"));

        await updater.DownloadAsync();
        Assert.Equal(UpdateState.Failed, updater.State);
        Assert.Equal("disk full", updater.Error);

        _feed.Download = Task.CompletedTask;
        await updater.DownloadAsync();
        Assert.Equal(UpdateState.Ready, updater.State);
        Assert.Null(updater.Error);
        Assert.Equal(2, _feed.Downloads);
    }

    private sealed class FakeFeed : IUpdateFeed
    {
        private Action<int>? _progress;

        public string? CurrentVersion { get; set; } = "1.0.0";

        public string? Newer { get; set; }

        public Exception? CheckFailure { get; set; }

        public Task Download { get; set; } = Task.CompletedTask;

        public int Checks { get; private set; }

        public int Downloads { get; private set; }

        public int Installs { get; private set; }

        public void ReportProgress(int percent) => _progress?.Invoke(percent);

        public Task<string?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return CheckFailure is null ? Task.FromResult(Newer) : Task.FromException<string?>(CheckFailure);
        }

        public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
        {
            Downloads++;
            _progress = progress;
            return Download;
        }

        public void InstallAfterExit() => Installs++;
    }
}
