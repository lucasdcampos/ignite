# Architecture

## Goals

- Sign in with a PSN account, register a PS5 on the local network, stream and play.
- Low latency, correct rendering, configurable quality (resolution, fps, codec, HDR, bitrate).
- Windows and Linux as first-class platforms. macOS should not be blocked by design choices.

## Non-goals (for now)

PS4 consoles, remote play over the internet through Sony's servers. Playing from another
network is supported only through [console sharing](#console-sharing), where a computer on
the console's network relays the traffic.

## Technology choices

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 (LTS) | Current LTS, good `Span`/SIMD support, AOT possible later. |
| Window, input, audio output | SDL3 (Hexa.NET.SDL3) | One cross-platform layer for windowing, gamepads (DualSense included), audio. Native SDL3 ships with the package for Windows, Linux and macOS. |
| UI | Dear ImGui (Hexa.NET.ImGui) with the SDL3 platform and SDL_Renderer backends, own theme and widgets, Inter and Lucide fonts embedded | The UI is a small launcher (setup, console cards, settings, PIN/passcode prompts). One window and one render loop for both UI and video. See [User interface](#user-interface). |
| Video decoding | FFmpeg through FFmpeg.AutoGen, D3D11VA on Windows / VAAPI on Linux with software fallback | Only realistic option for H.264/HEVC with hardware decoding on both platforms. H.264 is always decoded in software on Windows: FFmpeg's DXVA H.264 decoder drops slices past the 32nd and PS5 key frames have more (docs/protocol/11-video.md#decoder-input). Software decoding of a 1080p PS5 H.264 stream takes about 1 ms per frame. Software-decoded 10-bit pictures are repacked to P010. The build bundles an LGPL FFmpeg 9 (`build/Ffmpeg.targets`); `FfmpegLibrary` falls back to searching the usual install locations and accepts only libavcodec 63. |
| Video presentation | SDL_Renderer textures (NV12, IYUV, P010) | Textures are created with the picture's colorspace (BT.709 limited, or BT.2020 PQ limited for HDR): SDL would otherwise assume BT.601 for YUV and refuse P010. On an SDR output SDL tone maps HDR pictures. Windows: the decoder shares SDL's D3D11 device and each picture is copied GPU-to-GPU into the SDL texture (no system-memory round trip). SDL creates that device single-threaded, so in this mode decoding runs on the render thread. Elsewhere pictures are copied back to system memory and uploaded. The newest picture always wins. |
| Audio decoding | Concentus (managed Opus) | No native dependency; the console sends Opus. |
| Crypto | `System.Security.Cryptography` (AES, HMAC-SHA256, SHA-256, secp256k1 ECDH) plus an own GHASH for the 16-byte-IV GMAC | Avoid native crypto dependencies. |
| Protobuf | Google.Protobuf with an own `takion.proto` subset | Stream connection messages are protobuf (proto2). |

## Projects

```
src/
  Ignite.Protocol     Pure protocol: discovery, registration, session request, ctrl channel,
                      Takion transport, stream crypto, stream connection, senkusha,
                      AV packet reassembly and FEC, controller feedback encoding,
                      RemotePlaySession (the whole connection sequence).
                      Depends on the BCL and Google.Protobuf only.
  Ignite.Psn          PSN OAuth sign-in and account id retrieval (HTTP only).
  Ignite.Media        Video decoding (FFmpeg) and audio decoding (Opus). No windowing.
  Ignite.Relay        Console relay: listens on the Remote Play ports and forwards them to
                      a console, so clients on other networks can reach it.
                      Depends on the BCL and Ignite.Protocol (ports, subnet helpers) only.
  Ignite.Core         Settings and quality presets, registered consoles, PSN account,
                      console browser (continuous discovery), console sharing.
  Ignite.App          Executable "Ignite": SDL3 window, ImGui UI, video presentation, audio
                      output, gamepad/keyboard input, stream controller.
tests/
  Ignite.Protocol.Tests, Ignite.Psn.Tests, Ignite.Media.Tests, Ignite.Relay.Tests,
  Ignite.Core.Tests, Ignite.App.Tests
```

Dependency direction: `App -> Core -> (Protocol, Psn, Media, Relay)`, and `Relay -> Protocol`.
`Protocol`, `Psn` and `Media` do not reference each other. Interfaces that cross boundaries
(`IStreamSink`, `IVideoReceiverSink`, `IFeedbackTransport`, `ITakionHandler`, `IConsoleRelay`)
are defined by the lower layer.

## Threading model

- Takion: one dedicated receive thread per UDP socket with blocking receives. Packets are
  verified, decrypted and reassembled on that thread.
- Video: reassembled frames go through a bounded channel (drop oldest) to a decoder thread;
  decoded pictures are handed to the render thread, which always presents the newest one.
- Audio: decoded on the receive thread and pushed into an SDL audio stream; when more than
  80 ms are queued the queue is cleared to keep latency bounded.
- Frame pacing: the render loop presents with VSync when enabled, and `FrameLimiter` caps it
  at 1.05 × the display refresh rate (2 × without VSync). The cap matters because some drivers
  ignore VSync for Direct3D and Vulkan: on a PC with an NVIDIA GTX 1660 and a 144 Hz monitor
  even a bare SDL window presented about 5000 frames per second through Direct3D 11 (OpenGL
  was capped at 144), and Ignite burned well over one CPU core. When VSync works, each frame
  already takes a refresh interval and the limiter never waits.
- Input: the render loop samples SDL input once per frame and pushes state to the feedback
  sender, which sends at most every 8 ms and at least every 200 ms. While the stream settings
  panel has keyboard or gamepad focus the console gets an idle controller, so nothing stays
  held. The toolbar's PS button holds the PS button for 150 ms (`ButtonPulse`).
- Gamepad feedback from the console (rumble, lightbar, adaptive triggers) is queued and
  applied on the render thread. Ending a stream discards pending feedback and resets the
  triggers on that thread; closing a gamepad also resets any active effects.
- DualSense haptics bypass Opus. A dedicated SDL stream expands 3 kHz stereo PCM to four
  channels (silent channels 1/2, actuators on 3/4) and resamples to 48 kHz. The render thread
  opens/closes the named USB audio device as the selected gamepad and stream change;
  a lock serializes these operations with PCM submission on the network thread. The input
  queue has the same 80 ms bound as game audio. Legacy rumble is suppressed while this
  output is available, because it disables the DualSense's audio haptic mode.

## User interface

The UI targets people who just want to play, so every page has one obvious action and
technical options stay behind "Customize" and "Advanced".

- `Ui/Shell` draws the window chrome (background, top bar) and switches between pages:
  - **Setup** (first run): welcome, sign in, pair, quality, done. It is shown until
    `settings.json` records `SetupCompleted`; installations that already have an account and a
    paired console skip it. Pairing can be skipped and done later; "I'll do this later" skips
    the whole setup. Settings > Advanced can run it again.
  - **Home**: one card per paired console with a single main button: *Play* when the console
    is ready, *Turn on and play* in rest mode (`WakeAndPlay` sends wake-up packets every 10 s
    and launches as soon as discovery reports the console ready, giving up after 60 s), or a
    disabled *Not available* when it is not found. The card's menu shares the console with a
    friend or stops sharing it. Banners report start-up problems, why the last stream ended,
    unpaired consoles found on the network and, while a console is shared, the addresses to
    give the friend and how many computers are connected.
  - **Pair**: sign in when needed, pick a discovered console, type the PIN. "Can't find your
    console?" searches a typed address and says what happened: still looking, found, already
    paired, or nothing answered within 6 s (with hints about the address, the VPN and sharing).
  - **Settings**: quality presets (Performance 720p/H.264/10 Mbps, Balanced 1080p/HEVC/15 Mbps,
    Quality 1080p/HEVC/30 Mbps, all 60 fps) with custom values (360p to 1080p, since the console
    rejects higher resolutions), display toggles, account, consoles, keyboard controls and
    advanced options. Settings saved by versions that offered 1440p/2160p load as 1080p.
    Disconnects caused by rejected video settings and "in use" errors are explained in plain
    words (`StreamEndMessages`).
- `Ui/StreamOverlay` draws over the video: the connecting screen, the stream controls, the
  stream settings and the console passcode prompt.
  - `StreamToolbar` places three round buttons along the bottom edge: settings in the
    bottom-left corner, the PS button (white, with a grey "PS"; hidden while watching) in the
    middle and fullscreen in the bottom-right corner. The corner buttons are translucent dark
    circles, so they stay readable over any picture. The buttons appear when the stream starts
    and whenever the mouse moves, and fade out 3 s after the mouse rests (`OverlayVisibility`);
    the mouse cursor hides with them. They never take keyboard or gamepad focus, so clicking
    them leaves the controller with the game. The settings panel opens above the settings
    button.
  - `StreamPanel` (settings button or F1, closed with F1, Escape or its close button) shows
    the stream's status (picture size and frame rate, bandwidth, latency, decoder, recovered
    packets, requested quality) and the settings that can change while playing. VSync applies
    at once. Resolution, frame rate, video format, bitrate and hardware decoding are fixed for a
    session ([changing the quality](protocol/09-stream-connection.md#changing-the-quality)), so
    "Apply and restart" saves them and starts a new session to the same console. The console
    reports "in use" for a few seconds after a session ends, so every stream start retries
    the session request once per second for up to 20 s (`ConnectRetry`); a restart takes
    about 10 s. `QualityEditor` draws the same quality controls here and in Settings.
- Every way a stream ends leads back to the launcher: closing the window while connecting or
  streaming ends the stream instead of quitting (SDL's quit-on-last-window-close is turned
  off; closing the window from the launcher quits), and a stream that ends by itself (console
  disconnect, 5 s of silence, see [stream connection](protocol/09-stream-connection.md#detecting-a-lost-connection))
  releases its session immediately. The home screen then explains why, except when the user
  ended the stream.
- `Ui/Widgets` builds the controls (buttons, cards, toggles, option cards, steps, banners) on
  top of Dear ImGui items, so keyboard and gamepad navigation keep working; custom-drawn items
  draw their own focus ring. Escape or the gamepad's Circle button goes back from secondary
  pages.
- `Ui/Theme` holds colors, the type scale and the ImGui style. The look is minimal and
  monochrome: near-black backgrounds, neutral grey surfaces with 1 px borders, and white as
  the accent for primary buttons, selected options and switches (with dark text and marks on
  top). Color is used only for status (ready, resting, warnings, errors). Sizes are in design units
  scaled by the window's content scale (`SdlHost.ContentScale`: display scale divided by pixel
  density) and updated when the window moves to a display with another scale. Text uses Inter
  with Lucide icons merged into each weight; Inter's own private-use glyphs are excluded so they
  do not hide the icons. Dear ImGui 1.92 rasterizes glyphs on demand at any size.
- Icons: `assets/logo.png` is the source artwork. `src/Ignite.App/Assets/Icons` holds a square,
  rounded crop of it (tighter at 16-32 px so the mark stays legible): `ignite.ico` is the
  executable icon on Windows (`ApplicationIcon`), and the embedded `ignite-*.png` images become the
  window icon through `SDL_SetWindowIcon` on every platform (32 px base with 48-256 px
  alternates for high-DPI). Inside the app (top bar, welcome screen) `Ui/Logo` draws the
  transparent logo from `Assets/Images/logo-{64,128,256,512}.png`, picking the smallest texture
  at least as tall as the target because SDL_Renderer textures have no mipmaps. The README
  shows `assets/logo-no-bg.png`, the transparent source those images are made from (trimmed,
  without the faint haze left by its background removal).
- PSN sign-in ends on a redirect page the app cannot intercept: the user copies its address
  and the app picks it up from the clipboard (or a "Paste address" button).

## Console sharing

Lets a friend on another network play on this user's console without opening router ports.
Both computers join the same VPN (Radmin VPN, Tailscale, ZeroTier...), and the sharing
computer, which is on the console's network, relays the Remote Play ports. The friend's
Ignite adds the sharing computer's VPN address with "Can't find your console?" and from then
on treats it as the console: pairing, wake-up and streaming all go through it. Nothing is
decoded or re-encoded; the stream stays encrypted end to end between the friend's Ignite and
the console.

- `Ignite.Relay/ConsoleRelay` listens on all Remote Play ports (see the
  [port table](protocol/README.md#connection-overview)) and forwards each to the same port of
  the console. Every exchange is started by the client and the console answers the address it
  came from, so no protocol message has to be rewritten.
- `UdpRelay` gives every client address its own upstream socket, so the console's replies go
  back to the client that caused them, and drops replies that do not come from the console.
  A client that sent or received nothing for 30 s is forgotten. On port 9295 every datagram
  is also sent to the console subnet's broadcast address, because a console in pairing mode
  answers only broadcast `SRC3` searches ([registration](protocol/04-registration.md)).
- `TcpRelay` opens one console connection per accepted client and copies bytes both ways,
  passing half-closes on, until both sides have closed.
- `RelayClientFilter` serves only clients outside the console's network that are not this
  computer, so sharing cannot be tried with a second Ignite on the sharing computer (it would
  also share the same data folder and see the console as already paired). Relaying discovery broadcasts from the home network, or this computer's own
  searches on its VPN interface, would make the console appear at two addresses. The
  addresses shown to the user are this computer's IPv4 addresses outside the console's
  network, without link-local and host-only adapters (Hyper-V/WSL, Docker), which another
  computer can never reach.
- `Ignite.Core/ConsoleSharing` shares one console at a time (the ports are fixed), restarts
  the relay when the console's address changes and stops it when the console is forgotten.
  Sharing is not saved: it ends when Ignite closes.
- The console sends its large video packets only to the relay, over the home network; the
  relay sends them on through ordinary sockets, which the operating system can fragment if
  the VPN's MTU is smaller. The client still reports the fixed MTU described in
  [senkusha](protocol/10-senkusha.md).
- Windows Defender Firewall may ask whether Ignite can accept connections. VPN adapters are
  often classified as public networks, so access must be allowed there too.

Verified against a PS5 through the relay, from a client on another subnet: discovery is
answered with the console's normal status, and a TCP 9295 session request gets the same
answer as when sent directly. Pairing and a full stream from a friend's computer on another
network, over a VPN, were confirmed on 2026-11-26, so the console accepts the relay's address
in the `Host` header of authenticated requests. Not yet verified: waking the console through
the relay.

### Watching a shared console

The sharing computer can show the friend's stream in its own Ignite, without input, for the
console's owner to watch. The console takes only one Remote Play session at a time, so the
picture can only come from the friend's session, and nothing on the relay can decrypt it on
its own.

- The console's control messages, STREAMINFO included, are authenticated but not encrypted
  ([Takion](protocol/07-takion.md#authentication-of-control-packets)), so the relay reads the
  stream's resolutions, codec headers and audio format as they pass.
- Video and audio need the console's GKCrypt instance (index 3). The friend's Ignite gives
  the relay only that instance's key material, which cannot be used to send as the client
  ([one direction only](protocol/08-stream-crypto.md#one-direction-only)).
- `Ignite.Protocol/StreamObserver` follows one stream connection from the console's side of
  the path: it caches STREAMINFO, ends on DISCONNECT and, with the key and an attached sink,
  verifies, decrypts in place and reassembles AV packets with the same receivers as the
  client. While nobody watches it only tracks key positions, because the 32 bits on the
  wire cannot be resolved after a gap of 2 GiB or more. A sink that joins a running stream,
  or loses frames, needs a key frame, which the observer reports.
- The spectator channel is Ignite's own, on TCP port 9320 of the relay (`SpectatorLink.Port`,
  not a Remote Play port). After BANG the playing client connects to the console's address on
  that port. A real console refuses the connection, which costs one refused connection per
  session and nothing else; a relay accepts it only from the clients it serves. The client
  sends one 38-byte offer: `IGNW`, version `1`, the stream's codec as an `RP-StreamingType`
  value, and the 32-byte key material. The relay answers with one-byte signals: `1` someone
  started watching, `2` stopped watching, `3` key frame needed. The client asks the console
  for a key frame (`IDRREQUEST`) at most once per second, because key frames cost the player
  bandwidth. It closes the link when its stream ends.
- `UdpRelay` on port 9296 gives every client a tap that sees each console datagram right after
  it was passed on, so watching adds no delay for the player, and nothing a tap throws reaches
  the relay. `RelayedSessions` hands an offer's key to that client's sessions that have none.
  A session is watchable once its key verified a packet and STREAMINFO was seen, and ends on
  DISCONNECT, when the link closes or when the relay forgets the client.
- The key travels over the VPN in the clear; it decrypts only that one session's video and
  audio. If another program holds port 9320, sharing works without watching.
- In the app, the home screen of the sharing computer shows "Your friend is playing" with a
  Watch button while such a session exists. `StreamController.Watch` attaches to the session
  instead of connecting: the decoder follows the codec from the offer, input, gamepad feedback
  and haptics are left out, and the connecting screen stays until the first frame arrives.
  The toolbar has no PS button and the stream settings show status and display only, with
  "Stop watching". The view ends with the friend's stream. The player's stream settings say
  when the sharing computer is watching.
- Both computers need an Ignite with this feature: an older client never offers the key, so
  Watch never appears.

## Testing approach

- Protocol code is tested with known-answer vectors produced by a reference implementation
  (inputs chosen by us, only the outputs are kept) and with in-process fake consoles that
  play the console side of each exchange over loopback sockets.
- Media tests decode small H.264/HEVC clips generated with FFmpeg (`tests/Ignite.Media.Tests/Fixtures`).
  `testsrc-320x240-pq.hevc` is 10-bit HEVC tagged BT.2020/PQ, like the PS5's HDR stream
  (encoded with `hevc_nvenc -profile:v main10` and the `hevc_metadata` bitstream filter).
- Relay tests forward real datagrams and TCP connections between loopback sockets that play
  the client and the console. Spectator tests run a link and a listener over loopback and
  feed console packets encrypted with a known key to a relayed session.
- App tests check DualSense effects against byte vectors, capture SDL effect submission and
  resets with a virtual gamepad, and exercise native SDL resampling to verify channel
  routing, sample counts, continuity and the haptics queue's latency bound without hardware.
- Launcher logic (wake-and-play timing, console status, setup gating) has unit tests, and
  every page is drawn for a few frames with a headless Dear ImGui context to catch layout and
  id-stack errors.
- The app has a smoke-test mode: `IGNITE_SCREENSHOT=<file.bmp>` renders 90 frames, saves a
  screenshot and exits. Combined with `IGNITE_DATA_DIR` pointing at an empty directory it
  captures the first-run setup.

## Configuration and storage

Per-user application data directory (`%APPDATA%/Ignite` on Windows,
`$XDG_CONFIG_HOME/ignite` on Linux), or the directory named by the `IGNITE_DATA_DIR`
environment variable (handy to try the first-run setup without touching real data). JSON files:

- `settings.json`: stream quality (one of the Performance / Balanced / Quality presets or
  custom values), display options and whether the first-run setup was completed.
- `consoles.json`: registered consoles (nickname, MAC, host id, regist key, RP key).
- `account.json`: PSN account id and refresh token. Secrets are stored with the platform
  protection API where available (DPAPI on Windows; on Linux the files are readable only by the user).
