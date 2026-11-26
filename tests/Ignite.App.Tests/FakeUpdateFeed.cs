using Ignite.Core.Updates;

namespace Ignite.App.Tests;

/// <summary>An update feed that answers immediately with what the test set up.</summary>
internal sealed class FakeUpdateFeed : IUpdateFeed
{
    public string? CurrentVersion { get; set; }

    public string? Newer { get; set; }

    public Task Download { get; set; } = Task.CompletedTask;

    public Task<string?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Newer);

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        progress(50);
        return Download;
    }

    public void InstallAfterExit()
    {
    }
}
