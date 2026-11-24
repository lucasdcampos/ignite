using System.Net;
using System.Numerics;
using Hexa.NET.ImGui;
using Ignite.App.Platform;
using Ignite.App.Streaming;
using Ignite.Core.Consoles;
using Ignite.Core.Settings;
using Ignite.Protocol;
using Ignite.Protocol.Discovery;
using Ignite.Protocol.Registration;
using Ignite.Protocol.Session;
using Ignite.Psn;

namespace Ignite.App.Ui;

/// <summary>The home screen: account, consoles and settings.</summary>
internal sealed class MainView : IDisposable
{
    private static readonly string[] ResolutionNames = ["360p", "540p", "720p", "1080p", "1440p", "2160p"];
    private static readonly int[] FpsValues = [30, 60];
    private static readonly string[] CodecNames = ["H.264", "HEVC", "HEVC HDR"];

    private readonly AppServices _services;
    private readonly HttpClient _http = new();
    private const int ClipboardPollMilliseconds = 500;

    private string _redirectUrl = "";
    private bool _awaitingRedirect;
    private bool _openRegisterPopup;
    private long _lastClipboardCheck;
    private string? _ignoredClipboard;
    private string _manualAccountId = "";
    private string _manualAddress = "";
    private string _pin = "";
    private string _passcode = "";
    private string? _status;
    private bool _busy;
    private ConsoleEntry? _registering;

    public MainView(AppServices services) => _services = services;

    public void Dispose() => _http.Dispose();

    /// <summary>Set when the user chooses to play on a console.</summary>
    public event Action<ConsoleEntry, StoredConsole>? PlayRequested;

    public void Draw(StreamController? lastStream)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.Begin("Ignite", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);

        foreach (var problem in _services.StartupProblems)
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), problem);
        if (lastStream is { State: StreamState.Ended, Message: { } message })
            ImGui.TextColored(new Vector4(1, 0.8f, 0.3f, 1), message);
        if (_status is not null)
            ImGui.TextWrapped(_status);

        if (ImGui.CollapsingHeader("PlayStation Network account", ImGuiTreeNodeFlags.DefaultOpen))
            DrawAccount();
        if (ImGui.CollapsingHeader("Consoles", ImGuiTreeNodeFlags.DefaultOpen))
            DrawConsoles();
        if (ImGui.CollapsingHeader("Stream settings", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSettings();

        DrawRegisterPopup();
        ImGui.End();
    }

    /// <summary>Modal asking for the console login passcode while connecting.</summary>
    public void DrawPasscodePrompt(StreamController stream)
    {
        if (!stream.PasscodeRequested)
            return;
        ImGui.OpenPopup("Console passcode");
        if (!ImGui.BeginPopupModal("Console passcode", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.Text(stream.PasscodeRetry ? "Wrong passcode, try again:" : "Enter the console login passcode:");
        ImGui.InputText("##passcode", ref _passcode, 16, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.Button("OK"))
        {
            stream.AnswerPasscode(_passcode);
            _passcode = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            stream.AnswerPasscode(null);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawAccount()
    {
        if (_services.Account.AccountId is { } accountId)
        {
            ImGui.Text($"Signed in. Account id: {accountId.ToBase64()}");
            ImGui.SameLine();
            if (ImGui.Button("Sign out"))
                _services.Account.Clear();
            return;
        }

        DrawSignIn(_services.PsnOptions);

        if (ImGui.TreeNode("Enter the account id manually"))
        {
            ImGui.InputText("Account id (base64)", ref _manualAccountId, 64);
            ImGui.SameLine();
            if (ImGui.Button("Save account id"))
            {
                try
                {
                    _services.Account.Save(PsnAccountId.FromBase64(_manualAccountId.Trim()));
                    _status = "Account saved.";
                }
                catch (FormatException)
                {
                    _status = "The account id must be 8 bytes in base64 (12 characters).";
                }
            }

            ImGui.TreePop();
        }
    }

    private void DrawSignIn(PsnOAuthOptions options)
    {
        if (ImGui.Button("Sign in with PSN"))
        {
            SdlHost.OpenUrl(new PsnAuthClient(_http, options).LoginUri.ToString());
            _awaitingRedirect = true;
            _ignoredClipboard = SdlHost.ClipboardText();
        }

        if (!_awaitingRedirect)
            return;

        ImGui.TextWrapped(
            "1. Sign in with your email and password in the browser window that opened.\n"
            + "2. When it finishes, the browser shows a page whose address contains \"code=\".\n"
            + "3. Copy that address (Ctrl+L, Ctrl+C). Ignite picks it up automatically.");
        ImGui.InputText("Or paste the address here", ref _redirectUrl, 2048);
        ImGui.SameLine();
        if (ImGui.Button("Confirm") && !_busy)
            _ = RunAsync("Signing in...", CompleteSignInAsync);

        WatchClipboardForRedirect();
    }

    private void WatchClipboardForRedirect()
    {
        var now = Environment.TickCount64;
        if (_busy || now - _lastClipboardCheck < ClipboardPollMilliseconds)
            return;
        _lastClipboardCheck = now;

        var clipboard = SdlHost.ClipboardText();
        if (clipboard is null || clipboard == _ignoredClipboard
            || !clipboard.Contains("remoteplay/redirect", StringComparison.OrdinalIgnoreCase)
            || PsnAuthClient.ExtractCode(clipboard) is null)
        {
            return;
        }

        _ignoredClipboard = clipboard;
        _redirectUrl = clipboard;
        _ = RunAsync("Signing in...", CompleteSignInAsync);
    }

    private async Task CompleteSignInAsync()
    {
        var code = PsnAuthClient.ExtractCode(_redirectUrl) ?? throw new PsnException("The address has no code.");
        var client = new PsnAuthClient(_http, _services.PsnOptions);
        var tokens = await client.ExchangeCodeAsync(code);
        var userId = await client.GetUserIdAsync(tokens.AccessToken);
        _services.Account.Save(PsnAccountId.FromUserId(userId), refreshToken: tokens.RefreshToken);
        _redirectUrl = "";
        _awaitingRedirect = false;
        _status = "Signed in.";
    }

    private void DrawConsoles()
    {
        var entries = _services.Browser.Snapshot();
        if (entries.Count == 0)
            ImGui.TextDisabled("Searching for consoles on the network...");

        if (ImGui.BeginTable("consoles", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            foreach (var entry in entries)
            {
                ImGui.PushID(entry.HostId);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(entry.Name);
                ImGui.TableNextColumn();
                ImGui.Text(entry.Address?.ToString() ?? "-");
                ImGui.TableNextColumn();
                ImGui.Text(DescribeState(entry));
                ImGui.TableNextColumn();
                DrawConsoleActions(entry);
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        ImGui.InputText("Console address", ref _manualAddress, 64);
        ImGui.SameLine();
        if (ImGui.Button("Search address") && IPAddress.TryParse(_manualAddress.Trim(), out var address))
            _services.Browser.AddManualAddress(address);
    }

    private void DrawConsoleActions(ConsoleEntry entry)
    {
        var stored = _services.Consoles.Find(entry.HostId);
        if (stored is null)
        {
            if (ImGui.Button("Register") && entry.Address is not null)
            {
                _registering = entry;
                _pin = "";
                _openRegisterPopup = true;
            }

            return;
        }

        if (entry.State == ConsoleState.Standby && entry.Address is not null && ImGui.Button("Wake"))
            _ = RunAsync("Waking the console...", () => _services.Browser.WakeAsync(entry.Address, WakeupCredential.FromRegistKey(stored.ToCredentials().RegistKey)));
        if (entry.State == ConsoleState.Ready)
        {
            ImGui.SameLine();
            if (ImGui.Button("Play"))
                PlayRequested?.Invoke(entry, stored);
        }

        ImGui.SameLine();
        if (ImGui.Button("Forget"))
            _services.Consoles.Remove(entry.HostId);
    }

    private void DrawRegisterPopup()
    {
        // Opened here rather than from the table row: popup ids depend on the id stack, and the
        // row pushes the console id.
        if (_openRegisterPopup)
        {
            ImGui.OpenPopup("Register console");
            _openRegisterPopup = false;
        }

        if (!ImGui.BeginPopupModal("Register console", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped("On the PS5 open Settings > System > Remote Play > Pair Device and enter the PIN shown:");
        ImGui.InputText("PIN", ref _pin, 9, ImGuiInputTextFlags.CharsDecimal);
        var accountId = _services.Account.AccountId;
        if (accountId is null)
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), "Sign in or enter the account id first.");

        if (ImGui.Button("Register") && accountId is not null && _registering?.Address is not null
            && uint.TryParse(_pin, out var pin) && _pin.Length == 8)
        {
            var entry = _registering;
            var tables = _services.Tables;
            _ = RunAsync("Registering...", async () =>
            {
                var registered = await new RegistrationClient(tables).RegisterAsync(entry.Address!, accountId.Value, pin);
                _services.Consoles.Save(StoredConsole.From(entry.HostId, entry.Address!.ToString(), registered, entry.Name));
                _status = $"Registered {registered.Nickname ?? entry.Name}.";
            });
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawSettings()
    {
        var settings = _services.Settings.Current;
        var quality = settings.Quality;

        var resolution = (int)quality.Resolution;
        var fps = Array.IndexOf(FpsValues, quality.Fps);
        var codec = (int)quality.Codec - 1;
        var bitrate = quality.BitrateKbps ?? 0;
        var hardware = settings.HardwareDecoding;
        var vsync = settings.VSync;
        var fullscreen = settings.Fullscreen;
        var maxLoss = (float)settings.MaxReportedPacketLoss * 100;
        var ffmpegPath = settings.FfmpegPath ?? "";

        var changed = Combo("Resolution", ref resolution, ResolutionNames);
        changed |= Combo("Frame rate", ref fps, ["30 fps", "60 fps"]);
        changed |= Combo("Codec", ref codec, CodecNames);
        changed |= ImGui.InputInt("Bitrate (kbps, 0 = automatic)", ref bitrate, 1000, 5000);
        changed |= ImGui.Checkbox("Hardware decoding", ref hardware);
        changed |= ImGui.Checkbox("VSync", ref vsync);
        changed |= ImGui.Checkbox("Fullscreen while streaming (F11)", ref fullscreen);
        changed |= ImGui.SliderFloat("Max reported packet loss (%)", ref maxLoss, 0, 20, "%.0f");
        changed |= ImGui.InputText("FFmpeg path (optional, restart required)", ref ffmpegPath, 512);
        ImGui.TextDisabled($"Effective bitrate: {new StreamQuality((Resolution)resolution, FpsValues[Math.Max(0, fps)], (VideoCodec)(codec + 1), bitrate > 0 ? bitrate : null).EffectiveBitrateKbps} kbps"
            + (_services.FfmpegVersion is null ? "" : $"  |  FFmpeg {_services.FfmpegVersion}"));

        if (!changed)
            return;
        _services.Settings.Save(settings with
        {
            Quality = new StreamQuality((Resolution)resolution, FpsValues[Math.Max(0, fps)], (VideoCodec)(codec + 1), bitrate > 0 ? bitrate : null),
            HardwareDecoding = hardware,
            VSync = vsync,
            Fullscreen = fullscreen,
            MaxReportedPacketLoss = maxLoss / 100,
            FfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath.Trim(),
        });
    }

    private static bool Combo(string label, ref int index, string[] items)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, items[Math.Clamp(index, 0, items.Length - 1)]))
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (ImGui.Selectable(items[i], i == index))
                {
                    index = i;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static string DescribeState(ConsoleEntry entry)
    {
        var state = entry.State switch
        {
            ConsoleState.Ready => "On",
            ConsoleState.Standby => "Rest mode",
            _ => "Not found",
        };
        var registered = entry.Registered ? "registered" : "not registered";
        return entry.RunningApp is null ? $"{state}, {registered}" : $"{state}, {registered}, {entry.RunningApp}";
    }

    private async Task RunAsync(string progress, Func<Task> action)
    {
        _busy = true;
        _status = progress;
        try
        {
            await action();
            if (_status == progress)
                _status = "Done.";
        }
        catch (Exception e) when (e is RemotePlayException or PsnException or IOException or HttpRequestException or System.Net.Sockets.SocketException or OperationCanceledException or FormatException or ArgumentException)
        {
            _status = e.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
