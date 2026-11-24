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
| Window, input, audio output | SDL3 | One cross-platform layer for windowing, gamepads (DualSense included), audio. |
| UI | Dear ImGui rendered in the SDL window | The UI is simple (host list, settings, PIN prompt). One window and one render loop for both UI and video avoids embedding a native video surface in a UI toolkit. |
| Video decoding | FFmpeg (libavcodec), hardware accelerated (D3D11VA on Windows, VAAPI/Vulkan on Linux) with software fallback | Only realistic option for H.264/HEVC with hardware decoding on both platforms. |
| Video presentation | SDL3 GPU / renderer, uploading decoded frames as textures (zero-copy where the platform allows) | Keeps presentation inside the same SDL window. |
| Audio decoding | Opus (libopus via P/Invoke, or Concentus as a managed fallback) | The console sends Opus. |
| Crypto | `System.Security.Cryptography` (AES, HMAC-SHA256, SHA-256, AES-GCM); secp256k1 ECDH via explicit curve parameters, BouncyCastle only if the platform provider refuses the curve | Avoid native crypto dependencies. |
| Protobuf | Google.Protobuf | Stream connection messages are protobuf (proto2). |

Bindings for SDL3, ImGui and FFmpeg are chosen when the corresponding project is created and
recorded in `docs/dependencies.md`.

## Projects

```
src/
  Ignite.Protocol     Pure protocol: discovery, registration, session request, ctrl channel,
                      Takion transport, stream crypto, stream connection, senkusha,
                      AV packet reassembly and FEC, feedback (input) encoding.
                      Depends on nothing but the BCL and Google.Protobuf.
  Ignite.Psn          PSN OAuth sign-in and account id retrieval (HTTP only).
  Ignite.Media        Video decoding (FFmpeg) and audio decoding (Opus). No windowing.
  Ignite.Core         Application services: console list and persistence, settings,
                      session orchestration (connect, wake, reconnect), stream statistics.
  Ignite.App          Executable: SDL3 window, ImGui UI, video presentation, audio output,
                      gamepad/keyboard input mapping.
tests/
  Ignite.Protocol.Tests
  Ignite.Psn.Tests
  Ignite.Media.Tests
  Ignite.Core.Tests
```

Dependency direction: `App -> Core -> (Protocol, Psn, Media)`. `Protocol`, `Psn` and `Media`
do not reference each other. Interfaces that cross boundaries (for example "a sink for
decoded frames" or "a source of controller state") are defined by the lower layer.

## Threading model (planned)

- One receive loop per UDP socket (Takion), running on a dedicated thread with blocking
  receives to minimise latency. Packets are parsed in place from pooled buffers.
- Video: reassembled frames go through a bounded channel to the decoder thread; decoded
  frames go to the render thread, which always presents the newest frame (no queueing).
- Audio: decoded PCM goes to an SDL audio stream with a small, adaptive jitter buffer.
- Input: the render loop samples SDL input once per frame and pushes state to the
  feedback sender, which sends at most every 8 ms and at least every 200 ms.

## Configuration and storage

Per-user application data directory (`%APPDATA%/Ignite` on Windows,
`$XDG_CONFIG_HOME/ignite` on Linux), JSON files:

- `settings.json`: quality presets and input options.
- `consoles.json`: registered consoles (nickname, MAC, host id, regist key, RP key).
- `account.json`: PSN account id and refresh token. Secrets are stored with the platform
  protection API where available (DPAPI on Windows, libsecret on Linux).
