using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.Core.Settings;

namespace Ignite.App.Ui;

/// <summary>Quality, display, account and console settings, plus the advanced options.</summary>
internal sealed class SettingsScreen
{
    private readonly AppServices _services;
    private readonly SignInPanel _signIn;
    private readonly ForgetConsoleDialog _forget;
    private bool _showCustom;
    private bool _showAdvanced;

    public SettingsScreen(AppServices services, SignInPanel signIn, ForgetConsoleDialog forget)
    {
        _services = services;
        _signIn = signIn;
        _forget = forget;
    }

    public event Action? BackRequested;

    public event Action? PairRequested;

    public event Action? SetupRequested;

    public void Draw()
    {
        Widgets.BeginColumn("settings", 860);
        Widgets.Space(20);
        if (Widgets.IconButton(Icons.ArrowLeft, "back", "Back to your consoles", 44))
            BackRequested?.Invoke();
        ImGui.SameLine(0, Theme.Dp(12));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Theme.Dp(2));
        Widgets.Title("Settings");
        Widgets.Space(8);

        Section("Stream quality", DrawQuality);
        Section("Display", DrawDisplay);
        Section("PlayStation Network", _signIn.Draw);
        Section("Consoles", DrawConsoles);
        Section("While playing", DrawControls);
        Section("Advanced", DrawAdvanced);
        var version = _services.Updater.CurrentVersion is { } current ? $"Ignite {current}" : "Ignite development build (updates are off)";
        Widgets.Text(version, Theme.SmallSize, Theme.TextFaint);
        Widgets.Text("Ignite is not affiliated with or endorsed by Sony Interactive Entertainment.", Theme.SmallSize, Theme.TextFaint);

        _forget.Draw();
        Widgets.Space(40);
        Widgets.EndColumn();
    }

    private static void Section(string title, Action body)
    {
        Widgets.Text(title, Theme.SmallSize, Theme.TextMuted, bold: true);
        Widgets.BeginCard(title);
        body();
        Widgets.EndCard();
        Widgets.Space(14);
    }

    private void DrawQuality()
    {
        var settings = _services.Settings.Current;
        Widgets.Muted("Higher quality needs a faster network. If the picture stutters, pick a lower setting.");
        Widgets.Space(4);
        if (QualityPicker.Draw(settings.Quality) is { } preset)
            Save(settings with { Quality = preset.ToQuality() });

        var custom = QualityPresets.Match(settings.Quality) is null;
        Widgets.Space(2);
        if (Widgets.Button($"{Icons.Sliders}  {(_showCustom ? "Hide custom settings" : "Customize")}", ButtonKind.Ghost))
            _showCustom = !_showCustom;
        if (custom)
        {
            ImGui.SameLine();
            Widgets.Text("Custom settings in use", Theme.SmallSize, Theme.AccentHover);
        }

        if (_showCustom || custom)
            DrawCustomQuality(settings);
    }

    private void DrawCustomQuality(AppSettings settings)
    {
        var quality = settings.Quality;
        Widgets.Space(4);
        if (QualityEditor.Draw(ref quality))
        {
            _showCustom = true;
            Save(settings with { Quality = quality });
        }
    }

    private void DrawDisplay()
    {
        var settings = _services.Settings.Current;
        var fullscreen = settings.Fullscreen;
        var vsync = settings.VSync;
        var hardware = settings.HardwareDecoding;
        var changed = Widgets.Toggle("Fullscreen while playing", "You can also switch any time with F11.", ref fullscreen);
        changed |= Widgets.Toggle("VSync", "Avoids tearing. Turn it off for the lowest possible delay.", ref vsync);
        changed |= Widgets.Toggle("Hardware decoding", "Uses your graphics card to decode the video. Applies to the next stream.", ref hardware);
        if (changed)
            Save(settings with { Fullscreen = fullscreen, VSync = vsync, HardwareDecoding = hardware });
    }

    private void DrawConsoles()
    {
        var consoles = _services.Consoles.Consoles;
        if (consoles.Count == 0)
            Widgets.Muted("No paired consoles yet.");
        foreach (var console in consoles)
        {
            ImGui.PushID(console.HostId);
            var y = ImGui.GetCursorPosY();
            Widgets.Text($"{Icons.Gamepad}  {console.Nickname}", bold: true);
            Widgets.Text(console.LastAddress ?? "Address unknown", Theme.SmallSize, Theme.TextMuted);
            var label = $"{Icons.Trash}  Forget";
            Widgets.SameLineRight(Widgets.ButtonWidth(label));
            ImGui.SetCursorPosY(y + Theme.Dp(2));
            if (Widgets.Button(label, ButtonKind.Danger))
                _forget.Request(console.HostId, console.Nickname);
            ImGui.PopID();
        }

        if (Widgets.Button($"{Icons.Plus}  Pair a console"))
            PairRequested?.Invoke();
    }

    private static void DrawControls()
    {
        Row("F1", "Show or hide the stream settings (status, quality, VSync, disconnect)");
        Row("F11", "Switch fullscreen");
        Widgets.Space(4);
        Widgets.Text("Without a controller, the keyboard works as one:", Theme.SmallSize, Theme.TextMuted, bold: true);
        Widgets.Text(
            "Arrows: D-pad  ·  Enter: Cross  ·  Backspace: Circle  ·  Space: Square  ·  Tab: Triangle  ·  WASD: left stick  ·  IJKL: right stick  ·  "
            + "Q / E: L1 / R1  ·  1 / 3: L2 / R2  ·  O: Options  ·  P: PS button  ·  T: touchpad",
            Theme.SmallSize,
            Theme.TextMuted);
    }

    private void DrawAdvanced()
    {
        if (Widgets.Button(_showAdvanced ? "Hide advanced options" : "Show advanced options", ButtonKind.Ghost))
            _showAdvanced = !_showAdvanced;
        if (!_showAdvanced)
            return;

        var settings = _services.Settings.Current;
        var maxLoss = (float)settings.MaxReportedPacketLoss * 100;
        var ffmpegPath = settings.FfmpegPath ?? "";

        Widgets.Label("Highest packet loss reported to the console");
        ImGui.SetNextItemWidth(Widgets.Fill);
        var changed = ImGui.SliderFloat("##loss", ref maxLoss, 0, 20, "%.0f %%");
        Widgets.Text("Lower values keep the quality up on unstable networks; higher values let the console lower its bitrate sooner.", Theme.SmallSize, Theme.TextFaint);

        Widgets.Label("FFmpeg folder (optional, needs a restart)");
        ImGui.SetNextItemWidth(Widgets.Fill);
        changed |= ImGui.InputTextWithHint("##ffmpeg", "Use the bundled FFmpeg", ref ffmpegPath, 512);
        Widgets.Text(_services.FfmpegVersion is { } version ? $"FFmpeg {version} loaded." : "FFmpeg is not loaded.", Theme.SmallSize, Theme.TextFaint);
        Widgets.Text($"Settings are stored in {_services.Paths.DataDirectory}", Theme.SmallSize, Theme.TextFaint);
        Widgets.Space(4);
        if (Widgets.Button("Run the setup again"))
            SetupRequested?.Invoke();

        if (changed)
        {
            Save(settings with
            {
                MaxReportedPacketLoss = maxLoss / 100,
                FfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath.Trim(),
            });
        }
    }

    private void Save(AppSettings settings) => _services.Settings.Save(settings);

    private static void Row(string key, string description)
    {
        ImGui.PushFont(Theme.SemiBold, Theme.SmallSize);
        var size = ImGui.CalcTextSize(key) + Theme.Dp(16, 6);
        ImGui.PopFont();
        var position = ImGui.GetCursorScreenPos();
        var width = Theme.Dp(52);
        ImGui.GetWindowDrawList().AddRectFilled(position, position + new Vector2(width, size.Y), Theme.U32(Theme.SurfaceRaised), Theme.Dp(6));
        Widgets.DrawCentered(ImGui.GetWindowDrawList(), Theme.SemiBold, Theme.SmallSize, position + new Vector2(width, size.Y) / 2, Theme.Text, key);
        ImGui.Dummy(new Vector2(width, size.Y));
        ImGui.SameLine(0, Theme.Dp(14));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Theme.Dp(3));
        Widgets.Text(description, Theme.SmallSize, Theme.TextMuted);
    }
}
