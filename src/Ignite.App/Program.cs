using System.Net;
using Hexa.NET.ImGui;
using Hexa.NET.SDL3;
using Ignite.App.Audio;
using Ignite.App.Input;
using Ignite.App.Platform;
using Ignite.App.Streaming;
using Ignite.App.Ui;
using Ignite.App.Video;
using Ignite.Core.Consoles;
using Ignite.Core.Storage;
using Ignite.Core.Updates;
using Ignite.Protocol.Input;
using Velopack;

namespace Ignite.App;

internal static class Program
{
    private const int ScreenshotFrame = 90;
    private const double PsButtonSeconds = 0.15;

    private static int Main()
    {
        // First thing: the installer and the updater start the app with their own arguments, and
        // an update downloaded during an earlier run is applied here. vpk pack checks for this call.
        VelopackApp.Build().Run();
        using var host = new SdlHost("Ignite", 1280, 800);
        Theme.LoadFonts(ImGui.GetIO());
        Theme.Apply(host.ContentScale);
        Logo.Load(host.Renderer);
        var services = new AppServices(AppPaths.Default(), new VelopackUpdateFeed());
        services.Browser.Start();
        services.Updater.Start();
        using var presenter = new VideoPresenter(host.Renderer, host.D3D11Device);
        using var gamepad = new GamepadInput();
        using var haptics = new HapticsOutput();
        using var shell = new Shell(services);
        var overlay = new StreamOverlay(services.Settings);
        var psButton = new ButtonPulse(ControllerButtons.PS, PsButtonSeconds);
        StreamController? stream = null;
        (IPAddress Address, StoredConsole Console)? playing = null;
        var fullscreen = false;

        void StartStream(IPAddress address, StoredConsole console, bool restart)
        {
            stream?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            gamepad.ResetFeedback();
            haptics.SetEnabled(false);
            presenter.Clear();
            stream = new StreamController(presenter, haptics);
            stream.Start(services.Tables, address, console, services.Settings.Current, gamepad.IsDualSense || !gamepad.Connected, restart);
            playing = (address, console);
            if (restart)
                return;
            fullscreen = services.Settings.Current.Fullscreen;
            host.SetFullscreen(fullscreen);
        }

        shell.WatchRequested += (session, consoleName) =>
        {
            stream?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            gamepad.ResetFeedback();
            haptics.SetEnabled(false);
            presenter.Clear();
            stream = new StreamController(presenter, haptics);
            stream.Watch(session, consoleName, services.Settings.Current);
            playing = null;
            fullscreen = services.Settings.Current.Fullscreen;
            host.SetFullscreen(fullscreen);
        };
        shell.PlayRequested += (entry, console) =>
        {
            if (entry.Address is not null)
                StartStream(entry.Address, console, restart: false);
        };
        overlay.RestartRequested += () =>
        {
            if (playing is { } current)
                StartStream(current.Address, current.Console, restart: true);
        };
        overlay.FullscreenRequested += () => host.SetFullscreen(fullscreen = !fullscreen);
        overlay.PsButtonPressed += () => psButton.Press(ImGui.GetTime());

        gamepad.Refresh();
        var screenshotPath = Environment.GetEnvironmentVariable("IGNITE_SCREENSHOT");
        var frameCount = 0;
        var running = true;
        while (running && !services.Updater.InstallRequested)
        {
            frameCount++;
            var streaming = stream is { State: StreamState.Connecting or StreamState.Streaming, IsStopping: false };
            SdlHost.PumpEvents(e =>
            {
                var type = (SDLEventType)e.Type;
                if (type == SDLEventType.Quit)
                    running = false;
                if (type == SDLEventType.WindowCloseRequested)
                {
                    // While streaming, closing the window ends the stream and returns to the launcher.
                    if (streaming)
                        _ = stream!.StopAsync();
                    else
                        running = false;
                }

                if (type is SDLEventType.GamepadAdded or SDLEventType.GamepadRemoved)
                    gamepad.Refresh();
                if (type is SDLEventType.AudioDeviceAdded or SDLEventType.AudioDeviceRemoved)
                    haptics.Refresh();
                if (type == SDLEventType.WindowDisplayScaleChanged)
                    Theme.Apply(host.ContentScale);
                if (type == SDLEventType.KeyDown && e.Key.Repeat == 0)
                {
                    if (e.Key.Scancode == SDLScancode.F1 && streaming)
                        overlay.ToggleSettings();
                    if (e.Key.Scancode == SDLScancode.F11)
                        host.SetFullscreen(fullscreen = !fullscreen);
                }
            });

            streaming = stream is { State: StreamState.Connecting or StreamState.Streaming, IsStopping: false };
            host.SetVSync(services.Settings.Current.VSync);
            host.BeginFrame();

            haptics.SetEnabled(streaming && !stream!.Watching && gamepad.IsUsbDualSense);
            if (!streaming)
            {
                stream?.GamepadFeedback.Clear();
                gamepad.ResetFeedback();
            }
            if (streaming)
            {
                var (width, height) = host.OutputSize;
                stream!.DecodePending();
                presenter.Draw(width, height);
                while (stream!.GamepadFeedback.TryDequeue(out var feedback))
                    feedback(gamepad);
                // While the stream settings have keyboard or gamepad focus the console sees an idle controller, so nothing stays held.
                var input = ImGui.GetIO().WantCaptureKeyboard ? ControllerState.Idle : gamepad.Read(KeyboardInput.Read());
                stream.UpdateInput(psButton.Apply(input, ImGui.GetTime()));
                overlay.Draw(stream, fullscreen);
            }
            else
            {
                if (fullscreen && stream is not null)
                    host.SetFullscreen(fullscreen = false);
                shell.Draw(stream);
            }

            var capture = screenshotPath is not null && frameCount == ScreenshotFrame;
            host.EndFrame(capture ? screenshotPath : null);
            if (capture)
                running = false;
        }

        haptics.SetEnabled(false);
        gamepad.ResetFeedback();
        stream?.GamepadFeedback.Clear();
        stream?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        services.Updater.InstallIfRequested();
        services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }
}
