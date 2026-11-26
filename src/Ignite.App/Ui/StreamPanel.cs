using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;
using Ignite.Core.Settings;

namespace Ignite.App.Ui;

/// <summary>
/// The stream's status and the settings that can be changed while playing. VSync applies at
/// once; quality settings are fixed for a session, so applying them restarts the stream
/// (docs/protocol/09-stream-connection.md#changing-the-quality). A watched stream has no
/// quality settings: the friend who plays chose them.
/// </summary>
internal sealed class StreamPanel
{
    private const float Width = 780;
    private const float StatusWidth = 320;

    private readonly SettingsStore _settings;
    private StreamQuality _quality = new();
    private bool _hardwareDecoding;

    public StreamPanel(SettingsStore settings) => _settings = settings;

    public event Action? RestartRequested;

    public event Action? DisconnectRequested;

    public bool Visible { get; private set; }

    /// <summary>Shows the panel with the running stream's settings as the starting point.</summary>
    public void Open(AppSettings running)
    {
        _quality = running.Quality;
        _hardwareDecoding = running.HardwareDecoding;
        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Draws the panel with its bottom-left corner at <paramref name="bottomLeft"/>.</summary>
    /// <param name="watching">This computer only watches a friend's stream.</param>
    /// <param name="watched">The computer sharing the console watches this stream.</param>
    public void Draw(StreamStatistics? statistics, AppSettings running, Vector2 bottomLeft, bool watching = false, bool watched = false)
    {
        if (!Visible)
            return;

        var viewport = ImGui.GetMainViewport();
        var width = MathF.Min(Theme.Dp(watching ? StatusWidth + 40 : Width), viewport.WorkSize.X - Theme.Dp(32));
        var maxHeight = MathF.Max(Theme.Dp(200), bottomLeft.Y - viewport.WorkPos.Y - Theme.Dp(20));
        ImGui.SetNextWindowPos(bottomLeft, ImGuiCond.Always, new Vector2(0, 1));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, maxHeight));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.WithAlpha(Theme.Background, 0.96f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(20, 16));
        ImGui.Begin(
            "stream-settings",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        DrawHeader(running, watching);

        // Tighter than the launcher, so everything fits above the toolbar in a small window.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Theme.Dp(12, 8));
        var top = ImGui.GetCursorPos();
        ImGui.BeginGroup();
        Section("Status", StatusWidth, () => DrawStatus(statistics, running, watching, watched));
        Section("Display", StatusWidth, DrawDisplay);
        if (Widgets.Button(watching ? $"{Icons.X}  Stop watching" : $"{Icons.X}  Disconnect", ButtonKind.Danger, Theme.Dp(StatusWidth)))
            DisconnectRequested?.Invoke();
        ImGui.EndGroup();
        if (!watching)
        {
            // Positioned explicitly: SameLine would align the column's first text with the button before it.
            ImGui.SetCursorPos(top + new Vector2(Theme.Dp(StatusWidth + 16), 0));
            ImGui.BeginGroup();
            Section("Quality", Widgets.Fill, DrawQuality);
            ImGui.EndGroup();
        }

        ImGui.PopStyleVar();

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.IsAnyItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            Visible = false;
        ImGui.End();
    }

    private static void Section(string title, float width, Action body)
    {
        Widgets.Text(title, Theme.SmallSize, Theme.TextMuted, bold: true);
        Widgets.BeginCard(title, width == Widgets.Fill ? width : Theme.Dp(width));
        body();
        Widgets.EndCard();
        Widgets.Space(4);
    }

    private static void DrawStatus(StreamStatistics? statistics, AppSettings running, bool watching, bool watched)
    {
        if (statistics is null)
        {
            Widgets.Progress("Measuring...");
        }
        else
        {
            Stat("Picture", $"{statistics.Width} x {statistics.Height}  ·  {statistics.Fps:0} fps");
            Stat("Bandwidth", $"{statistics.VideoMbps:0.0} Mbps");
            if (statistics.RttMs is { } rtt)
                Stat("Latency", $"{rtt} ms");
            Stat("Decoder", statistics.Decoder ?? "-");
            Stat("Recovered packets", statistics.FecRecoveries.ToString(CultureInfo.InvariantCulture));
        }

        Widgets.Space(2);
        if (watching)
        {
            Widgets.Text("Your friend chooses the quality on their computer.", Theme.SmallSize, Theme.TextMuted);
            return;
        }

        Widgets.Text("Requested", Theme.SmallSize, Theme.TextMuted);
        Widgets.Text(QualityEditor.Describe(running.Quality), Theme.SmallSize, bold: true);
        if (watched)
        {
            Widgets.Space(2);
            Widgets.Text($"{Icons.Eye}  The computer sharing this console is watching.", Theme.SmallSize, Theme.Accent);
        }
    }

    private void DrawDisplay()
    {
        var settings = _settings.Current;
        var vsync = settings.VSync;
        if (Widgets.Toggle("VSync", "Avoids tearing. Off gives the lowest delay.", ref vsync))
            _settings.Save(settings with { VSync = vsync });
    }

    /// <summary>The title with the apply button, which stays visible however far the panel scrolls.</summary>
    private void DrawHeader(AppSettings running, bool watching)
    {
        const string apply = $"{Icons.RotateCw}  Apply and restart";
        const string undo = "Undo";
        const float close = 36;
        var changed = !watching && (_quality != running.Quality || _hardwareDecoding != running.HardwareDecoding);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = Theme.Dp(close) + (watching ? 0 : Widgets.ButtonWidth(apply) + spacing) + (changed ? Widgets.ButtonWidth(undo) + spacing : 0);

        var y = ImGui.GetCursorPosY();
        Widgets.Heading("Stream settings");
        Widgets.SameLineRight(width);
        ImGui.SetCursorPosY(y - Theme.Dp(8));
        if (watching)
        {
            DrawCloseButton(y, close);
            return;
        }

        if (changed)
        {
            if (Widgets.Button(undo))
            {
                _quality = running.Quality;
                _hardwareDecoding = running.HardwareDecoding;
            }

            ImGui.SameLine();
            ImGui.SetCursorPosY(y - Theme.Dp(8));
        }

        if (Widgets.Button(apply, ButtonKind.Primary, enabled: changed))
        {
            _settings.Save(_settings.Current with { Quality = _quality, HardwareDecoding = _hardwareDecoding });
            Visible = false;
            RestartRequested?.Invoke();
        }

        ImGui.SameLine();
        DrawCloseButton(y, close);
    }

    private void DrawCloseButton(float y, float size)
    {
        ImGui.SetCursorPosY(y - Theme.Dp(4));
        if (Widgets.IconButton(Icons.X, "close", "Close (F1 or Esc)", size))
            Visible = false;
    }

    private void DrawQuality()
    {
        QualityEditor.Draw(ref _quality);
        Widgets.Toggle("Hardware decoding", "Uses your graphics card to decode the video.", ref _hardwareDecoding);
        Widgets.Text("Applying restarts the stream (about 10 seconds).", Theme.SmallSize, Theme.TextFaint);
    }

    private static void Stat(string label, string value)
    {
        Widgets.Text(label, Theme.SmallSize, Theme.TextMuted);
        ImGui.SameLine(Theme.Dp(150));
        Widgets.Text(value, Theme.SmallSize, bold: true);
    }
}
