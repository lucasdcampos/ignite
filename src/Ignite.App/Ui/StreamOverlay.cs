using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;
using Ignite.Core.Settings;

namespace Ignite.App.Ui;

/// <summary>Everything drawn over the video: the connecting screen, the stream controls and settings, and the passcode prompt.</summary>
internal sealed class StreamOverlay
{
    private const string PasscodeId = "passcode";

    private readonly OverlayVisibility _controls = new();
    private readonly StreamPanel _panel;
    private StreamController? _stream;
    private bool _toolbarHovered;
    private string _passcode = "";

    public StreamOverlay(SettingsStore settings)
    {
        _panel = new StreamPanel(settings);
        _panel.RestartRequested += () => RestartRequested?.Invoke();
        _panel.DisconnectRequested += () =>
        {
            _panel.Close();
            _ = _stream?.StopAsync();
        };
    }

    public event Action? FullscreenRequested;

    public event Action? PsButtonPressed;

    /// <summary>The user applied settings that need a new session.</summary>
    public event Action? RestartRequested;

    public void ToggleSettings()
    {
        if (_panel.Visible)
            _panel.Close();
        else if (_stream is { State: StreamState.Streaming } stream)
            _panel.Open(stream.StartedWith);
    }

    public void Draw(StreamController stream, bool fullscreen)
    {
        var now = ImGui.GetTime();
        if (!ReferenceEquals(stream, _stream))
        {
            _stream = stream;
            _panel.Close();
            _controls.Activity(now);
        }

        if (stream.State == StreamState.Connecting)
            DrawConnecting(stream);
        else
            DrawControls(stream, fullscreen, now);

        DrawPasscode(stream);
    }

    private static void DrawConnecting(StreamController stream)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Background);
        ImGui.Begin("connecting", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleColor();

        var cardSize = Theme.Dp(460, 250);
        ImGui.SetCursorPos((viewport.WorkSize - cardSize) / 2);
        Widgets.BeginCard("card", cardSize.X, cardSize.Y);
        Widgets.Space(6);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - Theme.Dp(46)) / 2);
        Widgets.Spinner(20);
        Widgets.Space(6);
        Widgets.Centered(stream.Message ?? "Connecting...", Theme.HeadingSize - 1, bold: true);
        Widgets.Centered("This usually takes a few seconds.", Theme.SmallSize, Theme.TextMuted);
        Widgets.Space(10);
        var width = Theme.Dp(160);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - width) / 2);
        if (Widgets.Button("Cancel", ButtonKind.Secondary, width))
            _ = stream.StopAsync();
        Widgets.EndCard();
        ImGui.End();
    }

    /// <summary>The toolbar shows while the mouse moves; the cursor hides with it.</summary>
    private void DrawControls(StreamController stream, bool fullscreen, double now)
    {
        if (ImGui.GetIO().MouseDelta != Vector2.Zero || ImGui.IsAnyMouseDown())
            _controls.Activity(now);

        var viewport = ImGui.GetMainViewport();
        var panelCorner = viewport.WorkPos + new Vector2(Theme.Dp(StreamToolbar.SideMargin), viewport.WorkSize.Y - Theme.Dp(StreamToolbar.Clearance + 12));
        _panel.Draw(stream.Statistics(), stream.StartedWith, panelCorner, stream.Watching, stream.Watched);

        var opacity = _controls.Opacity(now, _panel.Visible || _toolbarHovered);
        if (opacity <= 0)
        {
            _toolbarHovered = false;
            ImGui.SetMouseCursor(ImGuiMouseCursor.None);
            return;
        }

        switch (StreamToolbar.Draw(fullscreen, playing: !stream.Watching, opacity, out _toolbarHovered))
        {
            case ToolbarAction.Fullscreen:
                FullscreenRequested?.Invoke();
                break;
            case ToolbarAction.PsButton:
                PsButtonPressed?.Invoke();
                break;
            case ToolbarAction.Settings:
                ToggleSettings();
                break;
        }
    }

    private void DrawPasscode(StreamController stream)
    {
        if (!stream.PasscodeRequested)
            return;
        if (!ImGui.IsPopupOpen(PasscodeId))
            ImGui.OpenPopup(PasscodeId);
        if (!Widgets.BeginDialog(PasscodeId, 420))
            return;

        Widgets.Heading(stream.PasscodeRetry ? "Wrong passcode, try again" : "Console passcode");
        Widgets.Muted("Your PS5 asks for its login passcode.");
        ImGui.PushFont(Theme.SemiBold, 26);
        ImGui.SetNextItemWidth(Widgets.Fill);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        var submitted = ImGui.InputText("##passcode", ref _passcode, 16, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopFont();
        Widgets.Space(4);
        if (Widgets.Button("OK", ButtonKind.Primary, Theme.Dp(120)) || submitted)
        {
            stream.AnswerPasscode(_passcode);
            _passcode = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (Widgets.Button("Cancel", ButtonKind.Ghost))
        {
            stream.AnswerPasscode(null);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
