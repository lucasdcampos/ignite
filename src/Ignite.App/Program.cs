using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.SDL3;
using Ignite.App.Audio;
using Ignite.App.Input;
using Ignite.App.Platform;
using Ignite.App.Streaming;
using Ignite.App.Ui;
using Ignite.App.Video;

namespace Ignite.App;

internal static class Program
{
    private const int ScreenshotFrame = 90;

    private static int Main()
    {
        using var host = new SdlHost("Ignite", 1280, 720);
        var services = new AppServices();
        using var presenter = new VideoPresenter(host.Renderer, host.D3D11Device);
        using var gamepad = new GamepadInput();
        using var haptics = new HapticsOutput();
        using var view = new MainView(services);
        StreamController? stream = null;
        var showOverlay = false;
        var fullscreen = false;

        view.PlayRequested += (entry, console) =>
        {
            if (entry.Address is null)
                return;
            stream?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            gamepad.ResetFeedback();
            haptics.SetEnabled(false);
            presenter.Clear();
            stream = new StreamController(presenter, haptics);
            stream.Start(services.Tables, entry.Address, console, services.Settings.Current, gamepad.IsDualSense || !gamepad.Connected);
            fullscreen = services.Settings.Current.Fullscreen;
            host.SetFullscreen(fullscreen);
        };

        gamepad.Refresh();
        var screenshotPath = Environment.GetEnvironmentVariable("IGNITE_SCREENSHOT");
        var frameCount = 0;
        var running = true;
        while (running)
        {
            frameCount++;
            running = SdlHost.PumpEvents(e =>
            {
                var type = (SDLEventType)e.Type;
                if (type is SDLEventType.GamepadAdded or SDLEventType.GamepadRemoved)
                    gamepad.Refresh();
                if (type is SDLEventType.AudioDeviceAdded or SDLEventType.AudioDeviceRemoved)
                    haptics.Refresh();
                if (type == SDLEventType.KeyDown && e.Key.Repeat == 0)
                {
                    if (e.Key.Scancode == SDLScancode.F1)
                        showOverlay = !showOverlay;
                    if (e.Key.Scancode == SDLScancode.F11)
                        host.SetFullscreen(fullscreen = !fullscreen);
                }
            });

            host.SetVSync(services.Settings.Current.VSync);
            host.BeginFrame();

            var streaming = stream is { State: StreamState.Connecting or StreamState.Streaming, IsStopping: false };
            haptics.SetEnabled(streaming && gamepad.IsUsbDualSense);
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
                if (!ImGui.GetIO().WantCaptureKeyboard)
                    stream.UpdateInput(gamepad.Read(KeyboardInput.Read()));
                DrawStreamOverlay(stream, ref showOverlay);
                view.DrawPasscodePrompt(stream);
            }
            else
            {
                if (fullscreen && stream is not null)
                    host.SetFullscreen(fullscreen = false);
                view.Draw(stream);
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
        services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    private static void DrawStreamOverlay(StreamController stream, ref bool showOverlay)
    {
        if (stream.State == StreamState.Connecting)
        {
            ImGui.SetNextWindowPos(new Vector2(20, 20));
            ImGui.Begin("connecting", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
            ImGui.Text(stream.Message ?? "Connecting...");
            if (ImGui.Button("Cancel"))
                _ = stream.StopAsync();
            ImGui.End();
            return;
        }

        if (!showOverlay)
            return;

        ImGui.SetNextWindowPos(new Vector2(20, 20));
        ImGui.SetNextWindowBgAlpha(0.7f);
        ImGui.Begin("Stream", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        if (stream.Statistics() is { } stats)
        {
            ImGui.Text($"{stats.Width}x{stats.Height}  {stats.Fps:0} fps  {stats.VideoMbps:0.0} Mbps");
            ImGui.Text($"Decoder: {stats.Decoder}  RTT: {stats.RttMs} ms  FEC recoveries: {stats.FecRecoveries}");
        }

        ImGui.TextDisabled("F1: toggle this panel   F11: fullscreen");
        if (ImGui.Button("Disconnect"))
        {
            _ = stream.StopAsync();
            showOverlay = false;
        }

        ImGui.End();
    }
}
