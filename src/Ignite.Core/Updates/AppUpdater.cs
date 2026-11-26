namespace Ignite.Core.Updates;

public enum UpdateState
{
    UpToDate,
    Available,
    Downloading,
    Ready,
    Failed,
}

/// <summary>
/// Looks for a newer release at start-up and downloads it only when the user asks
/// (docs/architecture.md#updates).
/// </summary>
public sealed class AppUpdater : IDisposable
{
    private readonly IUpdateFeed _feed;
    private readonly CancellationTokenSource _cancel = new();
    private volatile UpdateState _state;
    private volatile string? _availableVersion;
    private volatile string? _error;
    private volatile int _progress;
    private volatile bool _installRequested;

    public AppUpdater(IUpdateFeed feed) => _feed = feed;

    public string? CurrentVersion => _feed.CurrentVersion;

    public UpdateState State => _state;

    public string? AvailableVersion => _availableVersion;

    /// <summary>Download progress in percent.</summary>
    public int Progress => _progress;

    public string? Error => _error;

    /// <summary>True once the user chose to restart into the downloaded version.</summary>
    public bool InstallRequested => _installRequested;

    public void Start() => _ = CheckAsync();

    /// <summary>Failures are ignored: being offline is normal and the next start checks again.</summary>
    public async Task CheckAsync()
    {
        if (CurrentVersion is null)
            return;
        try
        {
            if (await _feed.CheckAsync(_cancel.Token) is { } version)
            {
                _availableVersion = version;
                _state = UpdateState.Available;
            }
        }
        // Velopack reports many failures as plain exceptions, so nothing more specific can be caught.
        catch (Exception)
        {
        }
    }

    public void Download()
    {
        if (_state is UpdateState.Available or UpdateState.Failed)
            _ = DownloadAsync();
    }

    public void RequestInstall()
    {
        if (_state == UpdateState.Ready)
            _installRequested = true;
    }

    /// <summary>Called when the app is about to exit.</summary>
    public void InstallIfRequested()
    {
        if (_installRequested)
            _feed.InstallAfterExit();
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }

    internal async Task DownloadAsync()
    {
        _state = UpdateState.Downloading;
        _progress = 0;
        _error = null;
        try
        {
            await _feed.DownloadAsync(percent => _progress = percent, _cancel.Token);
            _state = UpdateState.Ready;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _error = e.Message;
            _state = UpdateState.Failed;
        }
    }
}
