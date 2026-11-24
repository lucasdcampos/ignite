using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;

namespace Ignite.App.Ui;

/// <summary>Everything drawn over the video: the connecting screen, the F1 menu and the passcode prompt.</summary>
internal sealed class StreamOverlay
{
    private const double HintSeconds = 5;
    private const string PasscodeId = "passcode";

    private StreamController? _stream;
    private double _streamingSince = -1;
    private bool _menuVisible;
    private string _passcode = "";

    public void ToggleMenu() => _menuVisible = !_menuVisible;

    public void Draw(StreamController stream)
    {
        if (!ReferenceEquals(stream, _stream))
        {
            _stream = stream;
            _streamingSince = -1;
            _menuVisible = false;
        }

        if (stream.State == StreamState.Connecting)
        {
            DrawConnecting(stream);
        }
        else
        {
            if (_streamingSince < 0)
                _streamingSince = ImGui.GetTime();
            DrawHint();
            if (_menuVisible)
                DrawMenu(stream);
        }

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

    private unsafe void DrawHint()
    {
        var elapsed = ImGui.GetTime() - _streamingSince;
        if (elapsed > HintSeconds || _menuVisible)
            return;
        var alpha = (float)Math.Clamp(HintSeconds - elapsed, 0, 1);
        var viewport = ImGui.GetMainViewport();
        var drawList = ImGui.GetForegroundDrawList(viewport);
        const string text = "Press F1 for the stream menu";
        ImGui.PushFont(Theme.SemiBold, Theme.BodySize);
        var size = ImGui.CalcTextSize(text);
        ImGui.PopFont();
        var padding = Theme.Dp(20, 12);
        var min = new Vector2(viewport.Pos.X + (viewport.Size.X - size.X) / 2 - padding.X, viewport.Pos.Y + Theme.Dp(24));
        var max = min + size + padding * 2;
        drawList.AddRectFilled(min, max, Theme.U32(Theme.WithAlpha(Theme.SurfaceRaised, 0.9f * alpha)), (max.Y - min.Y) / 2);
        drawList.AddText(Theme.SemiBold.Handle, Theme.Dp(Theme.BodySize), min + padding, Theme.U32(Theme.WithAlpha(Theme.Text, alpha)), text);
    }

    private void DrawMenu(StreamController stream)
    {
        ImGui.SetNextWindowPos(Theme.Dp(20, 20));
        ImGui.SetNextWindowSize(new Vector2(Theme.Dp(380), 0));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.WithAlpha(Theme.SurfaceRaised, 0.94f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(22, 20));
        ImGui.Begin("stream-menu", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        Widgets.Heading("Stream");
        if (stream.Statistics() is { } stats)
        {
            Stat("Picture", $"{stats.Width} x {stats.Height}  ·  {stats.Fps:0} fps");
            Stat("Bandwidth", $"{stats.VideoMbps:0.0} Mbps");
            Stat("Latency", $"{stats.RttMs} ms");
            Stat("Decoder", stats.Decoder ?? "-");
            Stat("Recovered packets", stats.FecRecoveries.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Widgets.Space(4);
        if (Widgets.Button($"{Icons.X}  Disconnect", ButtonKind.Danger, Widgets.Fill))
        {
            _ = stream.StopAsync();
            _menuVisible = false;
        }

        Widgets.Text("F1: hide this menu  ·  F11: fullscreen", Theme.SmallSize, Theme.TextFaint);
        ImGui.End();
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

    private static void Stat(string label, string value)
    {
        Widgets.Text(label, Theme.SmallSize, Theme.TextMuted);
        ImGui.SameLine(Theme.Dp(170));
        Widgets.Text(value, Theme.SmallSize, bold: true);
    }
}
