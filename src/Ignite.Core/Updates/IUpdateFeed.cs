namespace Ignite.Core.Updates;

/// <summary>Finds, downloads and installs new releases of the app.</summary>
public interface IUpdateFeed
{
    /// <summary>The installed version, or null for a build that was not installed (it cannot update itself).</summary>
    string? CurrentVersion { get; }

    /// <summary>Returns the newer version that can be downloaded, or null when the app is up to date.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the version found by <see cref="CheckAsync"/>, reporting progress in percent.</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);

    /// <summary>Installs the downloaded version as soon as this process exits, then starts the app again.</summary>
    void InstallAfterExit();
}
