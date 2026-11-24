# Ignite

Ignite is an open source PlayStation 5 Remote Play client for Windows and Linux, written in
C# / .NET.

The goal is a client that is as simple as the official Remote Play app (sign in, add your
PS5, play) but gives you real control over stream quality: resolution, frame rate, codec
(H.264 / HEVC), HDR and bitrate.

> Status: early but working. PSN sign-in, discovery, pairing and streaming with video, audio
> and controller input have been verified on a real PS5 over the local network.

## Features

- Automatic discovery of PS5 consoles on the local network, wake from rest mode
- PSN sign-in (or manual account id) and pairing with the console PIN
- Hardware video decoding (D3D11VA on Windows, VAAPI on Linux) with software fallback
- Opus audio, DualSense and other SDL-supported gamepads, keyboard fallback
- Quality settings: 360p to 1080p, 30/60 fps, H.264, HEVC or HEVC HDR, custom bitrate up to 100 Mbps

Not planned for now: PS4 support, remote play over the internet.

## Requirements

- .NET 10 SDK
- Internet access on the first build: the build downloads a pinned LGPL build of FFmpeg 9
  (Windows x64 or Linux x64), verifies its SHA-256, caches it per user and copies it next to
  the executable. Nothing else needs to be installed.

## Building and running

```
dotnet build
dotnet test
dotnet run --project src/Ignite.App
```

To use an FFmpeg 9 installed on the system instead, build with
`-p:IgniteBundleFfmpeg=false`; Ignite then searches `IGNITE_FFMPEG_PATH`, the application
directory, the usual install locations (winget, scoop, chocolatey, MSYS2, `/usr/lib`...) and
the `PATH`. Media tests are skipped when no FFmpeg is found.

## Using

The first time Ignite opens, a short setup guides you through three steps:

1. **Sign in** to PlayStation Network in your browser, then copy the address of the page you
   land on (Ctrl+L, Ctrl+C); Ignite detects it on the clipboard and finishes the sign-in.
2. **Pair your PS5**: on the console open *Settings > System > Remote Play > Pair Device*,
   pick it in Ignite and type the 8-digit code shown on the TV.
3. **Choose the quality**: Performance (720p), Balanced (1080p) or Quality (1080p at a
   higher bitrate). Custom resolution, frame rate, codec and bitrate are in Settings.

After that, the home screen shows your consoles. Click **Play** (or **Turn on and play** if
the console is in rest mode). While streaming, **F1** shows statistics and the disconnect
button and **F11** toggles fullscreen.

Keyboard fallback: arrows = D-pad, Enter = Cross, Backspace = Circle, Space = Square,
Tab = Triangle, WASD = left stick, IJKL = right stick, Q/E = L1/R1, 1/3 = L2/R2,
O = Options, P = PS, T = touchpad click.

Settings and paired consoles are stored in `%APPDATA%/Ignite` (Windows) or
`$XDG_CONFIG_HOME/ignite` (Linux).

## Documentation

- [Architecture](docs/architecture.md)
- [Remote Play protocol](docs/protocol/README.md)
- [Dependencies and licensing](docs/dependencies.md)
- [Contributor and agent guidelines](AGENTS.md)

## Acknowledgements

The protocol documentation is based on the reverse engineering work of open source projects
like Chiaki. Ignite does not include their code.

## Legal

Ignite is not affiliated with or endorsed by Sony Interactive Entertainment. "PlayStation",
"PS5" and "DualSense" are trademarks of Sony Interactive Entertainment Inc.

## License

[MIT](LICENSE)
