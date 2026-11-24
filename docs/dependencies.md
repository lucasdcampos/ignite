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

## Planned dependencies

| Dependency | Use | License | Notes |
|---|---|---|---|
| .NET 10 BCL | Runtime, sockets, crypto | MIT | |
| Google.Protobuf | Protobuf runtime | BSD-3-Clause | |
| Grpc.Tools (build only) | `.proto` code generation | Apache-2.0 | Not redistributed |
| SDL3 (native) | Window, input, audio, GPU | zlib | Ship the native library alongside the app |
| SDL3 C# bindings (e.g. `SDL3-CS`) | Bindings | zlib/MIT (choose at integration) | Record the exact package when added |
| Dear ImGui (native, via cimgui) | UI | MIT | |
| ImGui C# bindings (e.g. `Hexa.NET.ImGui`) | Bindings | MIT | Record the exact package when added |
| FFmpeg (libavcodec, libavutil) | Video decoding | LGPL-2.1+ | Must be a build **without** `--enable-gpl`/`--enable-nonfree`, dynamically linked and replaceable by the user. Ship its license and source offer. |
| FFmpeg.AutoGen | FFmpeg bindings | LGPL-3.0 | Library is used unmodified as a separate assembly; LGPL obligations are met by shipping it as a replaceable DLL with its license. Alternative: hand-written P/Invoke for the few functions needed (keeps everything MIT). |
| libopus (native) or Concentus (managed) | Opus decoding | BSD-3-Clause | |
| BouncyCastle.Cryptography (only if needed) | secp256k1, GCM with 16-byte IV | MIT | Only if the BCL cannot cover it |
| xUnit, FluentAssertions-free assertions | Tests | Apache-2.0 / MIT | Test only. Avoid FluentAssertions 8+ (commercial license). |

## Rules

- Prefer MIT, BSD, Apache-2.0, zlib or MPL-2.0 dependencies.
- LGPL is acceptable only for native libraries linked dynamically and replaceable by the user.
- GPL and AGPL dependencies are not allowed.
- Add a row here in the same change that adds a dependency, and update `THIRD-PARTY-NOTICES.md`
  once the first redistributed dependency is added.
