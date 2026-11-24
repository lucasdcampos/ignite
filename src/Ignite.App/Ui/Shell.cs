using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Streaming;
using Ignite.Core.Consoles;

namespace Ignite.App.Ui;

internal enum Page
{
    Setup,
    Home,
    Settings,
    Pair,
}

/// <summary>The window chrome (background, top bar) and navigation between the launcher pages.</summary>
internal sealed class Shell : IDisposable
{
    private readonly AppServices _services;
    private readonly SignInPanel _signIn;
    private readonly PairingPanel _pairing;
    private readonly SetupScreen _setup;
    private readonly HomeScreen _home;
    private readonly SettingsScreen _settings;
    private readonly PairScreen _pair;
    private StreamController? _reportedStream;

    public Shell(AppServices services)
    {
        _services = services;
        _signIn = new SignInPanel(services);
        _pairing = new PairingPanel(services);
        var forget = new ForgetConsoleDialog(services);
        _setup = new SetupScreen(services, _signIn, _pairing);
        _home = new HomeScreen(services, forget);
        _settings = new SettingsScreen(services, _signIn, forget);
        _pair = new PairScreen(services, _signIn, _pairing);

        _setup.Finished += () => Page = Page.Home;
        _home.PlayRequested += (entry, console) => PlayRequested?.Invoke(entry, console);
        _home.PairRequested += OpenPairing;
        _settings.BackRequested += () => Page = Page.Home;
        _settings.PairRequested += OpenPairing;
        _settings.SetupRequested += () =>
        {
            _setup.Restart();
            Page = Page.Setup;
        };
        _pair.BackRequested += () => Page = Page.Home;
        Page = services.NeedsSetup ? Page.Setup : Page.Home;
    }

    public event Action<ConsoleEntry, StoredConsole>? PlayRequested;

    public Page Page { get; private set; }

    public void Dispose() => _signIn.Dispose();

    public void Draw(StreamController? lastStream)
    {
        ReportStreamEnd(lastStream);
        var viewport = ImGui.GetMainViewport();
        DrawBackground(viewport);

        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Ignite", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        if (Page != Page.Setup)
            DrawTopBar();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(28, 0));
        ImGui.BeginChild("content", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();
        HandleBackShortcut();
        switch (Page)
        {
            case Page.Setup:
                _setup.Draw();
                break;
            case Page.Settings:
                _settings.Draw();
                break;
            case Page.Pair:
                _pair.Draw();
                break;
            default:
                _home.Draw();
                break;
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void OpenPairing()
    {
        _pairing.Reset();
        Page = Page.Pair;
    }

    private void ReportStreamEnd(StreamController? stream)
    {
        if (stream is not { State: StreamState.Ended, Message: { } message } || ReferenceEquals(stream, _reportedStream))
            return;
        _reportedStream = stream;
        _home.ShowNotice(message);
    }

    /// <summary>Escape or the gamepad's Circle button goes back from the secondary pages.</summary>
    private void HandleBackShortcut()
    {
        if (Page is not (Page.Settings or Page.Pair) || ImGui.IsAnyItemActive() || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
            return;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false))
            Page = Page.Home;
    }

    private void DrawTopBar()
    {
        var height = Theme.Dp(72);
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var width = ImGui.GetWindowWidth();
        drawList.AddLine(origin + new Vector2(0, height), origin + new Vector2(width, height), Theme.U32(Theme.WithAlpha(Theme.Border, 0.6f)));

        var middle = height / 2;
        ImGui.SetCursorPos(new Vector2(Theme.Dp(28), middle - Theme.Dp(18)));
        Logo.Draw(36);
        ImGui.SameLine(0, Theme.Dp(10));
        ImGui.SetCursorPosY(middle - Theme.Dp(14));
        Widgets.Text("Ignite", 22, bold: true);

        var settingsX = width - Theme.Dp(28 + 44);
        var accountLabel = _services.Account.AccountId is null ? $"{Icons.User}  Sign in" : $"{Icons.CircleCheck}  Signed in";
        var accountWidth = Widgets.ButtonWidth(accountLabel);
        ImGui.SetCursorPos(new Vector2(settingsX - accountWidth - Theme.Dp(8), middle - Theme.Dp(22)));
        if (Widgets.Button(accountLabel, ButtonKind.Ghost))
            Page = Page.Settings;
        ImGui.SetItemTooltip(_services.Account.AccountId is null ? "Sign in to PlayStation Network" : "PlayStation Network account");

        ImGui.SetCursorPos(new Vector2(settingsX, middle - Theme.Dp(22)));
        if (Widgets.IconButton(Icons.Settings, "settings", "Settings", 44))
            Page = Page == Page.Settings ? Page.Home : Page.Settings;
        ImGui.SetCursorPos(new Vector2(0, height + 1));
    }

    private static void DrawBackground(ImGuiViewportPtr viewport)
    {
        var drawList = ImGui.GetBackgroundDrawList(viewport);
        var min = viewport.Pos;
        var max = viewport.Pos + viewport.Size;
        drawList.AddRectFilled(min, max, Theme.U32(Theme.Background));
        var glow = Theme.U32(Theme.BackgroundGlow);
        var clear = Theme.U32(Theme.Background);
        drawList.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + viewport.Size.Y * 0.7f), glow, glow, clear, clear);
    }
}
