using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;
using Ignite.Core.Settings;

namespace Ignite.App.Ui;

/// <summary>
/// The stream's status and the settings that can be changed while playing. VSync applies at
/// once; quality settings are fixed for a session, so applying them restarts the stream
/// (docs/protocol/09-stream-connection.md#changing-the-quality).
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

    /// <summary>Draws the panel with its bottom edge centered on <paramref name="bottomCenter"/>.</summary>
    public void Draw(StreamStatistics? statistics, AppSettings running, Vector2 bottomCenter)
    {
        if (!Visible)
            return;

        var viewport = ImGui.GetMainViewport();
        var width = MathF.Min(Theme.Dp(Width), viewport.WorkSize.X - Theme.Dp(32));
        var maxHeight = MathF.Max(Theme.Dp(200), bottomCenter.Y - viewport.WorkPos.Y - Theme.Dp(20));
        ImGui.SetNextWindowPos(bottomCenter, ImGuiCond.Always, new Vector2(0.5f, 1));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, maxHeight));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.WithAlpha(Theme.Background, 0.96f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(20, 16));
        ImGui.Begin(
            "stream-settings",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        DrawHeader(running);

        // Tighter than the launcher, so everything fits above the toolbar in a small window.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Theme.Dp(12, 8));
        var top = ImGui.GetCursorPos();
        ImGui.BeginGroup();
        Section("Status", StatusWidth, () => DrawStatus(statistics, running));
        Section("Display", StatusWidth, DrawDisplay);
        if (Widgets.Button($"{Icons.X}  Disconnect", ButtonKind.Danger, Theme.Dp(StatusWidth)))
            DisconnectRequested?.Invoke();
        ImGui.EndGroup();
        // Positioned explicitly: SameLine would align the column's first text with the button before it.
        ImGui.SetCursorPos(top + new Vector2(Theme.Dp(StatusWidth + 16), 0));
        ImGui.BeginGroup();
        Section("Quality", Widgets.Fill, DrawQuality);
        ImGui.EndGroup();
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

    private static void DrawStatus(StreamStatistics? statistics, AppSettings running)
    {
        if (statistics is null)
        {
            Widgets.Progress("Measuring...");
        }
        else
        {
            Stat("Picture", $"{statistics.Width} x {statistics.Height}  ·  {statistics.Fps:0} fps");
            Stat("Bandwidth", $"{statistics.VideoMbps:0.0} Mbps");
            Stat("Latency", $"{statistics.RttMs} ms");
            Stat("Decoder", statistics.Decoder ?? "-");
            Stat("Recovered packets", statistics.FecRecoveries.ToString(CultureInfo.InvariantCulture));
        }

        Widgets.Space(2);
        Widgets.Text("Requested", Theme.SmallSize, Theme.TextMuted);
        Widgets.Text(QualityEditor.Describe(running.Quality), Theme.SmallSize, bold: true);
    }

    private void DrawDisplay()
    {
        var settings = _settings.Current;
        var vsync = settings.VSync;
        if (Widgets.Toggle("VSync", "Avoids tearing. Off gives the lowest delay.", ref vsync))
            _settings.Save(settings with { VSync = vsync });
    }

    /// <summary>The title with the apply button, which stays visible however far the panel scrolls.</summary>
    private void DrawHeader(AppSettings running)
    {
        const string apply = $"{Icons.RotateCw}  Apply and restart";
        const string undo = "Undo";
        const float close = 36;
        var changed = _quality != running.Quality || _hardwareDecoding != running.HardwareDecoding;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = Widgets.ButtonWidth(apply) + spacing + Theme.Dp(close) + (changed ? Widgets.ButtonWidth(undo) + spacing : 0);

        var y = ImGui.GetCursorPosY();
        Widgets.Heading("Stream settings");
        Widgets.SameLineRight(width);
        ImGui.SetCursorPosY(y - Theme.Dp(8));
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
        ImGui.SetCursorPosY(y - Theme.Dp(4));
        if (Widgets.IconButton(Icons.X, "close", "Close (F1 or Esc)", close))
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
