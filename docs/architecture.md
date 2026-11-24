# Architecture

## Goals

- Sign in with a PSN account, register a PS5 on the local network, stream and play.
- Low latency, correct rendering, configurable quality (resolution, fps, codec, HDR, bitrate).
- Windows and Linux as first-class platforms. macOS should not be blocked by design choices.

## Non-goals (for now)

PS4 consoles, remote play over the internet.

## Technology choices

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 (LTS) | Current LTS, good `Span`/SIMD support, AOT possible later. |
| Window, input, audio output | SDL3 (Hexa.NET.SDL3) | One cross-platform layer for windowing, gamepads (DualSense included), audio. Native SDL3 ships with the package for Windows, Linux and macOS. |
| UI | Dear ImGui (Hexa.NET.ImGui) with the SDL3 platform and SDL_Renderer backends, own theme and widgets, Inter and Lucide fonts embedded | The UI is a small launcher (setup, console cards, settings, PIN/passcode prompts). One window and one render loop for both UI and video. See [User interface](#user-interface). |
| Video decoding | FFmpeg through FFmpeg.AutoGen, D3D11VA on Windows / VAAPI on Linux with software fallback | Only realistic option for H.264/HEVC with hardware decoding on both platforms. The build bundles an LGPL FFmpeg 9 (`build/Ffmpeg.targets`); `FfmpegLibrary` falls back to searching the usual install locations and accepts only libavcodec 63. |
| Video presentation | SDL_Renderer textures (NV12, IYUV, P010) | Windows: the decoder shares SDL's D3D11 device and each picture is copied GPU-to-GPU into the SDL texture (no system-memory round trip). SDL creates that device single-threaded, so in this mode decoding runs on the render thread. Elsewhere pictures are copied back to system memory and uploaded. The newest picture always wins. |
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
  Ignite.Core         Settings and quality presets, registered consoles, PSN account,
                      console browser (continuous discovery).
  Ignite.App          Executable "Ignite": SDL3 window, ImGui UI, video presentation, audio
                      output, gamepad/keyboard input, stream controller.
tests/
  Ignite.Protocol.Tests, Ignite.Psn.Tests, Ignite.Media.Tests, Ignite.Core.Tests,
  Ignite.App.Tests
```

Dependency direction: `App -> Core -> (Protocol, Psn, Media)`. `Protocol`, `Psn` and `Media`
do not reference each other. Interfaces that cross boundaries (`IStreamSink`,
`IVideoReceiverSink`, `IFeedbackTransport`, `ITakionHandler`) are defined by the lower layer.

## Threading model

- Takion: one dedicated receive thread per UDP socket with blocking receives. Packets are
  verified, decrypted and reassembled on that thread.
- Video: reassembled frames go through a bounded channel (drop oldest) to a decoder thread;
  decoded pictures are handed to the render thread, which always presents the newest one.
- Audio: decoded on the receive thread and pushed into an SDL audio stream; when more than
  80 ms are queued the queue is cleared to keep latency bounded.
- Input: the render loop samples SDL input once per frame and pushes state to the feedback
  sender, which sends at most every 8 ms and at least every 200 ms.
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
    disabled *Not available* when it is not found. Banners report start-up problems, why the
    last stream ended and unpaired consoles found on the network.
  - **Pair**: sign in when needed, pick a discovered console, type the PIN.
  - **Settings**: quality presets (Performance 720p/H.264/10 Mbps, Balanced 1080p/HEVC/15 Mbps,
    Quality 1080p/HEVC/30 Mbps, all 60 fps) with custom values (360p to 1080p, since the console
    rejects higher resolutions), display toggles, account, consoles, keyboard controls and
    advanced options. Settings saved by versions that offered 1440p/2160p load as 1080p.
    Disconnects caused by rejected video settings and "in use" errors are explained in plain
    words (`StreamEndMessages`).
- `Ui/StreamOverlay` draws over the video: the connecting screen, a short "Press F1" hint, the
  F1 menu (statistics, disconnect) and the console passcode prompt.
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
- `Ui/Theme` holds colors, the type scale and the ImGui style. Sizes are in design units
  scaled by the window's content scale (`SdlHost.ContentScale`: display scale divided by pixel
  density) and updated when the window moves to a display with another scale. Text uses Inter
  with Lucide icons merged into each weight; Inter's own private-use glyphs are excluded so they
  do not hide the icons. Dear ImGui 1.92 rasterizes glyphs on demand at any size.
- PSN sign-in ends on a redirect page the app cannot intercept: the user copies its address
  and the app picks it up from the clipboard (or a "Paste address" button).

## Testing approach

- Protocol code is tested with known-answer vectors produced by a reference implementation
  (inputs chosen by us, only the outputs are kept) and with in-process fake consoles that
  play the console side of each exchange over loopback sockets.
- Media tests decode small H.264/HEVC clips generated with FFmpeg (`tests/Ignite.Media.Tests/Fixtures`).
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
