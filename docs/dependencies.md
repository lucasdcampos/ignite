# Dependencies and licensing

Ignite is released under the MIT license. Every dependency must be compatible with
distributing Ignite under MIT; this file records each one and why it is acceptable.

## Relationship with other open source clients

Open source projects like Chiaki are licensed under copyleft licenses (AGPL-3.0). Ignite does
**not** contain their code: the protocol was documented in `docs/protocol/` from studying
them, and Ignite is implemented from that documentation with its own design. Copying or
translating their source code into Ignite would make Ignite a derivative work, so it is not
allowed (see `AGENTS.md`).

The protocol constant tables (see `docs/protocol/03-rpcrypt.md`) are data required for
interoperability with the console. They are embedded in `Ignite.Protocol` as a binary
resource (`Crypto/rpcrypt-tables.bin`).

The OAuth client id/secret of the official Remote Play app are not stored in the repository;
they are supplied through configuration.

## Dependencies

| Dependency | Version | Use | License | Notes |
|---|---|---|---|---|
| .NET 10 BCL | 10.0 | Runtime, sockets, crypto | MIT | |
| Google.Protobuf | 3.36.2 | Protobuf runtime | BSD-3-Clause | |
| Grpc.Tools (build only) | 2.85.0 | `.proto` code generation | Apache-2.0 | Not redistributed |
| FFmpeg.AutoGen | 9.0.1.1 | FFmpeg bindings | LGPL-3.0 | Used unmodified as a separate assembly; ship it replaceable with its license |
| FFmpeg shared libraries (native) | 9.x (avcodec-63) | Video decoding | LGPL-2.1+ / GPL depending on the build | Distribute an LGPL build (no `--enable-gpl`/`--enable-nonfree`) with its license and source offer. Development builds may be GPL. Located through `IGNITE_FFMPEG_PATH` or the application directory. |
| Concentus | 2.2.3 | Opus decoding (managed) | BSD-3-Clause | |
| xUnit, Microsoft.NET.Test.Sdk, coverlet | see `Directory.Packages.props` | Tests | Apache-2.0 / MIT | Test only |

Planned (recorded here when added): SDL3 bindings and native SDL3 (zlib), Dear ImGui
bindings (MIT).

## Rules

- Prefer MIT, BSD, Apache-2.0, zlib or MPL-2.0 dependencies.
- LGPL is acceptable only for native libraries linked dynamically and replaceable by the user.
- GPL and AGPL dependencies are not allowed.
- Add a row here in the same change that adds a dependency, and update `THIRD-PARTY-NOTICES.md`
  once the first redistributed dependency is added.
