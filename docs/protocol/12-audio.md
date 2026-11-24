# 12. Audio

## Audio header

`STREAMINFO.audio_header` is 14 bytes:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | Channels |
| 1 | 1 | Bits per sample |
| 2 | 4 | Sample rate (Hz) |
| 6 | 4 | Frame size (samples per channel per frame) |
| 10 | 4 | Unknown (1) |

Typical values: 2 channels, 16 bits, 48000 Hz, 480 samples (10 ms). The order of bytes 0
and 1 is not fully confirmed; Ignite only reads the header and should sanity check the result
(channels 1–8, bits 16).

## Audio packets

Audio packets use the AV header from [Video](11-video.md#av-packet-header-takion-v12). Only
codec `5` (Opus) is handled. The payload is `total units × unit size` bytes; each unit is one
encoded Opus frame of exactly `unit size` bytes.

Audio uses simple redundancy instead of Reed–Solomon. With `s` source units and `f` FEC
units in a packet whose frame index is `F`:

- source unit `i` (0 ≤ i < s) is audio frame `F + i`;
- FEC unit `j` (0 ≤ j < f) is a repeated copy of audio frame `F − f + j`.

The receiver decodes each audio frame index once, in order, using the copies to fill frames
whose original packet was lost. At the very start of the stream (frame index small), FEC
units that would refer to negative indices are ignored.

Packets with the haptics flag carry the DualSense haptics stream instead of game audio and
are handled by a separate receiver (out of scope for Ignite's first version).

## Playback

Decode with Opus at the header's sample rate and channel count, then push PCM to the audio
output with a small jitter buffer. Dropping or stretching samples to keep latency bounded is
an implementation choice documented in `docs/architecture.md`.
