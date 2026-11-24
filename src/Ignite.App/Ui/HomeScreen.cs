using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.Core.Consoles;
using Ignite.Protocol.Discovery;

namespace Ignite.App.Ui;

/// <summary>The launcher: one card per paired console with a single main action.</summary>
internal sealed class HomeScreen
{
    private readonly AppServices _services;
    private readonly ForgetConsoleDialog _forget;
    private readonly WakeAndPlay _wake = new();
    private readonly BackgroundJob _wakeJob = new();
    private string? _notice;

    public HomeScreen(AppServices services, ForgetConsoleDialog forget)
    {
        _services = services;
        _forget = forget;
    }

    public event Action<ConsoleEntry, StoredConsole>? PlayRequested;

    public event Action? PairRequested;

    public void ShowNotice(string message) => _notice = message;

    public void Draw()
    {
        var entries = _services.Browser.Snapshot();
        UpdateWake(entries);

        Widgets.BeginColumn("home", 1120);
        Widgets.Space(24);
        var paired = entries.Where(e => e.Registered).ToList();
        Widgets.Title("Your consoles");
        Widgets.Muted(paired.Count == 0 ? "Pair your PS5 to get started." : "Pick a console to start playing.");
        Widgets.Space(6);
        DrawBanners(entries);

        if (paired.Count == 0)
            DrawEmptyState();
        else
            DrawGrid(paired);

        _forget.Draw();
        Widgets.Space(40);
        Widgets.EndColumn();
    }

    private void DrawBanners(IReadOnlyList<ConsoleEntry> entries)
    {
        foreach (var problem in _services.StartupProblems)
            Widgets.Banner(problem, Icons.CircleAlert, problem, Theme.Danger);
        if (_notice is not null && Widgets.Banner("notice", Icons.Info, _notice, Theme.TextMuted, "Dismiss"))
            _notice = null;
        if (_wakeJob.Error is { } error && Widgets.Banner("wake-error", Icons.CircleAlert, $"Could not wake the console: {error}", Theme.Danger, "Dismiss"))
            _wakeJob.ClearError();

        var found = entries.FirstOrDefault(e => !e.Registered && e.State == ConsoleState.Ready);
        if (found is not null && Widgets.Banner("found", Icons.Sparkles, $"New PS5 found on your network: {found.Name}", Theme.Accent, "Pair"))
            PairRequested?.Invoke();
    }

    private void DrawEmptyState()
    {
        Widgets.Space(30);
        Widgets.BeginCard("empty");
        Widgets.Space(16);
        Widgets.Centered(Icons.Gamepad, Theme.HeroSize, Theme.TextMuted);
        Widgets.Space(6);
        Widgets.Centered("No consoles yet", Theme.HeadingSize, bold: true);
        Widgets.Centered("Pair your PS5 once and it shows up here.", color: Theme.TextMuted);
        Widgets.Space(14);
        var width = Theme.Dp(240);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - width) / 2);
        if (Widgets.Button($"{Icons.Plus}  Pair a console", ButtonKind.Primary, width))
            PairRequested?.Invoke();
        Widgets.Space(16);
        Widgets.EndCard();
    }

    private void DrawGrid(List<ConsoleEntry> paired)
    {
        var size = Theme.Dp(300, 360);
        var spacing = Theme.Dp(20);
        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (size.X + spacing)));
        for (var i = 0; i <= paired.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0, spacing);
            else if (i > 0)
                ImGui.Dummy(new Vector2(0, spacing - ImGui.GetStyle().ItemSpacing.Y));

            if (i < paired.Count)
            {
                var stored = _services.Consoles.Find(paired[i].HostId);
                if (stored is not null)
                    DrawCard(paired[i], stored, size);
            }
            else if (Widgets.Tile("add-console", Icons.Plus, "Pair a console", size))
            {
                PairRequested?.Invoke();
            }
        }
    }

    private void DrawCard(ConsoleEntry entry, StoredConsole stored, Vector2 size)
    {
        var status = ConsoleStatus.For(entry, _wake.HostId == entry.HostId);
        var tone = ToneColor(status.Tone);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(20, 20));
        ImGui.BeginChild(entry.HostId, size, ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var artMin = origin + Theme.Dp(8, 8);
        var artMax = new Vector2(origin.X + size.X - Theme.Dp(8), origin.Y + Theme.Dp(170));
        drawList.AddRectFilled(artMin, artMax, Theme.U32(Theme.SurfaceRaised), Theme.Dp(12));
        var artCenter = (artMin + artMax) / 2;
        drawList.AddCircleFilled(artCenter, Theme.Dp(58), Theme.U32(Theme.WithAlpha(tone, 0.10f)));
        drawList.AddCircleFilled(artCenter, Theme.Dp(42), Theme.U32(Theme.WithAlpha(tone, 0.10f)));
        Widgets.DrawCentered(drawList, Theme.Regular, 56, artCenter, Theme.Text, Icons.Gamepad);

        ImGui.SetCursorPos(new Vector2(size.X - Theme.Dp(56), Theme.Dp(16)));
        if (Widgets.IconButton(Icons.EllipsisVertical, "menu", "More options"))
            ImGui.OpenPopup("console-menu");
        DrawMenu(entry);

        ImGui.SetCursorPosY(Theme.Dp(186));
        Widgets.Text(entry.Name, Theme.HeadingSize, bold: true);
        Widgets.Dot(tone);
        ImGui.SameLine(0, Theme.Dp(8));
        Widgets.Text(status.Label, Theme.SmallSize + 1, tone, bold: true);
        if (status.Detail is not null)
            Widgets.Text(status.Detail, Theme.SmallSize, Theme.TextMuted);

        ImGui.SetCursorPosY(size.Y - Theme.Dp(20 + 44));
        DrawAction(entry, stored, status.Action);
        ImGui.EndChild();
    }

    private void DrawMenu(ConsoleEntry entry)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.Dp(8, 8));
        if (ImGui.BeginPopup("console-menu"))
        {
            if (ImGui.Selectable($"{Icons.Trash}  Forget this console"))
                _forget.Request(entry.HostId, entry.Name);
            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();
    }

    private void DrawAction(ConsoleEntry entry, StoredConsole stored, ConsoleAction action)
    {
        switch (action)
        {
            case ConsoleAction.Play:
                if (Widgets.Button($"{Icons.Play}  Play", ButtonKind.Primary, Widgets.Fill))
                    PlayRequested?.Invoke(entry, stored);
                break;
            case ConsoleAction.WakeAndPlay:
                if (Widgets.Button($"{Icons.Power}  Turn on and play", ButtonKind.Primary, Widgets.Fill))
                    _wake.Begin(entry.HostId);
                break;
            case ConsoleAction.Waking:
                if (Widgets.Button("Cancel", ButtonKind.Secondary, Widgets.Fill))
                    _wake.Cancel();
                break;
            default:
                Widgets.Button("Not available", ButtonKind.Secondary, Widgets.Fill, enabled: false);
                break;
        }
    }

    private void UpdateWake(IReadOnlyList<ConsoleEntry> entries)
    {
        if (_wake.HostId is not { } hostId)
            return;
        var entry = entries.FirstOrDefault(e => e.HostId == hostId);
        var stored = _services.Consoles.Find(hostId);
        if (stored is null)
        {
            _wake.Cancel();
            return;
        }

        switch (_wake.Update(entry))
        {
            case WakeStep.SendWakeup:
                var credential = WakeupCredential.FromRegistKey(stored.ToCredentials().RegistKey);
                _wakeJob.Run(() => _services.Browser.WakeAsync(entry!.Address!, credential));
                break;
            case WakeStep.Launch:
                PlayRequested?.Invoke(entry!, stored);
                break;
            case WakeStep.TimedOut:
                _notice = $"{stored.Nickname} did not turn on. Press the PS button on its controller, then try again.";
                break;
        }
    }

    private static Vector4 ToneColor(StatusTone tone) => tone switch
    {
        StatusTone.Ready => Theme.Success,
        StatusTone.Resting => Theme.Warning,
        StatusTone.Working => Theme.Accent,
        _ => Theme.TextFaint,
    };
}
