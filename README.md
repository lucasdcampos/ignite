# Ignite

Ignite is an open source PlayStation 5 Remote Play client for Windows and Linux, written in
C# / .NET.

The goal is a client that is as simple as the official Remote Play app (sign in, add your
PS5, play) but gives you real control over stream quality: resolution, frame rate, codec
(H.264 / HEVC), HDR and bitrate.

> Status: the full local-network pipeline is implemented and tested against simulated
> consoles; discovery has been verified against a real PS5. Pairing and streaming still need
> to be validated on real hardware.

## Features

- Automatic discovery of PS5 consoles on the local network, wake from rest mode
- PSN sign-in (or manual account id) and pairing with the console PIN
- Hardware video decoding (D3D11VA on Windows, VAAPI on Linux) with software fallback
- Opus audio, DualSense and other SDL-supported gamepads, keyboard fallback
- Quality settings: 360p to 2160p, 30/60 fps, H.264, HEVC or HEVC HDR, custom bitrate

Not planned for now: PS4 support, remote play over the internet.

## Requirements

- .NET 10 SDK
- FFmpeg 9 shared libraries (`avcodec-63`, `avutil-61`, ...). Point `IGNITE_FFMPEG_PATH` (or
  the FFmpeg path in the settings) at the directory containing them, or place them next to
  the executable. Distribute only LGPL builds of FFmpeg.

## Building and running

```
dotnet build
dotnet test
dotnet run --project src/Ignite.App
```

Media tests are skipped when FFmpeg is not found.

PSN sign-in needs the OAuth client of the official app, supplied through
`IGNITE_PSN_CLIENT_ID` and `IGNITE_PSN_CLIENT_SECRET` (or the settings file). Without it,
enter the account id manually.

## Using

1. Sign in with PSN, or paste your account id (base64).
2. Your PS5 appears in the console list. On the console open
   *Settings > System > Remote Play > Pair Device*, click **Register** and type the PIN.
3. Click **Wake** if the console is in rest mode, then **Play**.
4. While streaming: **F1** shows statistics and the disconnect button, **F11** toggles
   fullscreen.

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
