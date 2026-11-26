using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;
using Ignite.App.Ui;
using Ignite.Core.Consoles;
using Ignite.Core.Storage;
using Ignite.Core.Updates;
using Ignite.Protocol;
using Ignite.Protocol.Discovery;

namespace Ignite.App.Tests;

/// <summary>Draws every launcher page with a real (headless) Dear ImGui context to catch layout and id-stack errors.</summary>
public sealed unsafe class LauncherRenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ignite-ui-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ImGuiContextPtr _context;
    private readonly FakeUpdateFeed _updates = new();
    private readonly AppServices _services;

    public LauncherRenderTests()
    {
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.DisplaySize = new Vector2(1280, 800);
        io.DeltaTime = 1 / 60f;
        // Textures are never uploaded here; the flag just lets the atlas grow on demand.
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        Theme.LoadFonts(io);
        Theme.Apply(1.5f);
        _services = new AppServices(new AppPaths(_directory), _updates);
    }

    public void Dispose()
    {
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        ImGui.DestroyContext(_context);
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void FirstRunStartsWithTheSetup()
    {
        using var shell = new Shell(_services);

        Assert.Equal(Page.Setup, shell.Page);
        Render(() => shell.Draw(null));
    }

    [Fact]
    public void ConfiguredInstallationOpensTheHomeScreenWithItsConsoles()
    {
        _services.Account.Save(PsnAccountId.FromUserId(1));
        _services.Consoles.Save(new StoredConsole("PS5", "Living Room", "192.168.0.10", "00", "", "", 0));
        using var shell = new Shell(_services);

        Assert.Equal(Page.Home, shell.Page);
        var drawn = Render(() => shell.Draw(null));
        Assert.True(drawn > 0);

        _services.Sharing.Share(new ConsoleEntry("PS5", "Living Room", null, ConsoleState.Unknown, true, null, DateTimeOffset.MinValue));
        Assert.NotNull(_services.Sharing.Error);
        Render(() => shell.Draw(null));
    }

    [Fact]
    public void HomeScreenDrawsEveryUpdateStep()
    {
        _updates.CurrentVersion = "1.0.0";
        _updates.Newer = "1.1.0";
        var download = new TaskCompletionSource();
        _updates.Download = download.Task;
        _services.Settings.Save(_services.Settings.Current with { SetupCompleted = true });
        using var shell = new Shell(_services);
        var updater = _services.Updater;

        updater.Start();
        Assert.Equal(UpdateState.Available, updater.State);
        Render(() => shell.Draw(null));

        updater.Download();
        Assert.Equal(UpdateState.Downloading, updater.State);
        Render(() => shell.Draw(null));

        download.SetException(new IOException("disk full"));
        Assert.Equal(UpdateState.Failed, updater.State);
        Render(() => shell.Draw(null));

        _updates.Download = Task.CompletedTask;
        updater.Download();
        Assert.Equal(UpdateState.Ready, updater.State);
        Render(() => shell.Draw(null));
    }

    [Fact]
    public void EveryPanelAndScreenDraws()
    {
        using var signIn = new SignInPanel(_services);
        var pairing = new PairingPanel(_services);
        var forget = new ForgetConsoleDialog(_services);
        var settings = new SettingsScreen(_services, signIn, forget);
        var pair = new PairScreen(_services, signIn, pairing);
        var setup = new SetupScreen(_services, signIn, pairing);

        Render(() => InWindow(settings.Draw));
        Render(() => InWindow(pair.Draw));
        Render(() => InWindow(setup.Draw));
        Render(() => InWindow(() => QualityPicker.Draw(_services.Settings.Current.Quality)));
        _services.Account.Save(PsnAccountId.FromUserId(1));
        Render(() => InWindow(signIn.Draw));
        forget.Request("PS5", "Living Room");
        Render(() => InWindow(forget.Draw));
    }

    [Fact]
    public void StreamControlsAndSettingsDraw()
    {
        var panel = new StreamPanel(_services.Settings);
        var running = _services.Settings.Current;
        var statistics = new StreamStatistics(59.9, 14.2, 3, "d3d11va (zero-copy)", 1920, 1080, 4);
        var bottom = new Vector2(640, 600);

        Render(() => StreamToolbar.Draw(fullscreen: false, playing: true, opacity: 1, out _));
        Render(() => StreamToolbar.Draw(fullscreen: true, playing: false, opacity: 0.5f, out _));
        panel.Open(running);
        Render(() => panel.Draw(null, running, bottom));
        var drawn = Render(() => panel.Draw(statistics, running with { Quality = running.Quality with { BitrateKbps = 40000 } }, bottom));

        var playing = Render(() => panel.Draw(statistics, running, bottom));
        var watched = Render(() => panel.Draw(statistics, running, bottom, watched: true));
        var watching = Render(() => panel.Draw(statistics with { RttMs = null }, running, bottom, watching: true));
        var together = Render(() =>
        {
            panel.Draw(statistics, running, bottom);
            StreamToolbar.Draw(fullscreen: false, playing: true, opacity: 1, out _);
        });

        Assert.True(drawn > 0);
        Assert.True(watched > playing);
        Assert.True(watching is > 0 && watching < playing);
        Assert.True(together > playing);
        Assert.True(panel.Visible);
        panel.Close();
        Assert.Equal(0, Render(() => panel.Draw(statistics, running, bottom)));
    }

    [Fact]
    public void QualitySummaryNamesEveryValue()
    {
        var quality = new Ignite.Core.Settings.StreamQuality(Ignite.Core.Settings.Resolution.P720, 30, Ignite.Protocol.Session.VideoCodec.HevcHdr, 25000);

        Assert.Equal("720p  ·  30 fps  ·  HEVC HDR  ·  25 Mbps", QualityEditor.Describe(quality));
    }

    private static void InWindow(Action draw)
    {
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
        ImGui.Begin("test", ImGuiWindowFlags.NoDecoration);
        draw();
        ImGui.End();
    }

    /// <summary>Renders a few frames and returns the number of vertices of the last one.</summary>
    private static int Render(Action draw)
    {
        var vertices = 0;
        for (var frame = 0; frame < 3; frame++)
        {
            ImGui.NewFrame();
            draw();
            ImGui.Render();
            vertices = ImGui.GetDrawData().TotalVtxCount;
        }

        return vertices;
    }
}
