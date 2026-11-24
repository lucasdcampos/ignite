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
| UI | Dear ImGui (Hexa.NET.ImGui) with the SDL3 platform and SDL_Renderer backends | The UI is simple (account, console list, settings, PIN/passcode prompts). One window and one render loop for both UI and video. |
| Video decoding | FFmpeg through FFmpeg.AutoGen, D3D11VA on Windows / VAAPI on Linux with software fallback | Only realistic option for H.264/HEVC with hardware decoding on both platforms. |
| Video presentation | SDL_Renderer streaming textures (NV12, IYUV, P010) | Decoded pictures are copied back to system memory and uploaded; the newest picture always wins. Zero-copy (sharing the D3D11/VAAPI surface with the renderer) is a later optimisation. |
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
  Ignite.Protocol.Tests, Ignite.Psn.Tests, Ignite.Media.Tests, Ignite.Core.Tests
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
- Gamepad feedback from the console (rumble, lightbar) is queued and applied on the render
  thread.

## Testing approach

- Protocol code is tested with known-answer vectors produced by a reference implementation
  (inputs chosen by us, only the outputs are kept) and with in-process fake consoles that
  play the console side of each exchange over loopback sockets.
- Media tests decode small H.264/HEVC clips generated with FFmpeg (`tests/Ignite.Media.Tests/Fixtures`).
- The app has a smoke-test mode: `IGNITE_SCREENSHOT=<file.bmp>` renders 90 frames, saves a
  screenshot and exits.

## Configuration and storage

Per-user application data directory (`%APPDATA%/Ignite` on Windows,
`$XDG_CONFIG_HOME/ignite` on Linux), JSON files:

- `settings.json`: quality presets and input options.
- `consoles.json`: registered consoles (nickname, MAC, host id, regist key, RP key).
- `account.json`: PSN account id and refresh token. Secrets are stored with the platform
  protection API where available (DPAPI on Windows; on Linux the files are readable only by the user).
