# Ignite

Ignite is an open source PlayStation 5 Remote Play client for Windows and Linux, written in
C# / .NET.

The goal is a client that is as simple as the official Remote Play app (sign in, add your
PS5, play) but gives you real control over stream quality: resolution, frame rate, codec
(H.264 / HEVC), HDR and bitrate.

> Status: early development. The protocol is being documented; there is no usable build yet.

## Planned features

- PSN sign-in to obtain the account id needed to pair a console
- Automatic discovery of PS5 consoles on the local network, wake from rest mode
- Pairing with the console PIN
- Low latency video with hardware decoding, audio, DualSense and other controllers
- Quality settings: 360p to 1080p (and higher where supported), 30/60 fps, H.264 or HEVC,
  HDR, custom bitrate

Not planned for now: PS4 support, remote play over the internet.

## Documentation

- [Architecture](docs/architecture.md)
- [Remote Play protocol](docs/protocol/README.md)
- [Dependencies and licensing](docs/dependencies.md)
- [Contributor and agent guidelines](AGENTS.md)

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

## Acknowledgements

The protocol documentation is based on the reverse engineering work of open source projects
like Chiaki. Ignite does not include their code.

## Legal

Ignite is not affiliated with or endorsed by Sony Interactive Entertainment. "PlayStation",
"PS5" and "DualSense" are trademarks of Sony Interactive Entertainment Inc.

## License

[MIT](LICENSE)
