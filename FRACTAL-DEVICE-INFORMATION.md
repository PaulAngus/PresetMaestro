# Fractal connection verification

## Hardware evidence and correction

The original proposed `0x46` query was rejected by the user's FM9 firmware 12.0
with `F0 00 01 74 12 64 46 05 30 F7`. It has been removed from connection setup.
The subsequently supplied USB capture contains these successful FM9-Edit reads:

```
Identity request: F0 00 01 74 7F 00 7A F7
FM9 identity:     F0 00 01 74 12 64 00 00 73 F7
Name request:    F0 00 01 74 12 01 1A 00 01 00 44 0A 00 00 00 00 00 00 00 00 00 43 F7
```

WHO_AM_I uses broadcast model 0x7F. A successful response must have a supported
model (0x10 Axe-Fx III, 0x11 FM3, 0x12 FM9), function 0x64, echoed function 0,
and status 0. Manufacturer, length, framing, seven-bit data and checksum are
validated. Unrelated parameter replies, loopback echoes, and nonzero statuses
cannot establish a connection. Port-name guesses and sequential 0x46 probes
are no longer necessary with this captured discovery request.

After FM9 identification, the client sends only the captured 23-byte name read.
The parameter bytes `44 0A` represent 1348 using two little-endian seven-bit
values. The whole captured envelope matters; the parameter ID alone was not
used to invent a request. No name-setting or configuration-change commands
from the editor capture are replayed.

The response is function 0x01, not 0x07. Its 37 packed septets at frame offset 21
contain a continuous MSB-first bitstream: 32 decoded bytes plus three zero bits.
The captured PM-TEST and FM9 responses contain ASCII text followed by NUL padding.
The parser requires the full observed envelope, checks padding, permits up to
eight printable ASCII characters, and preserves visible spaces.

See `PresetMaestro.Tests/Fixtures/Fractal/README.md` for capture provenance,
packet numbers, complete hardware fixtures and synthetic-test distinctions.
Raw name and parameter payloads are now redacted in normal diagnostics.

## Connection behavior

The listener is registered before each request. Each read has a 700 ms timeout.
Opening MIDI ports alone does not establish a connection. The identity reply
must arrive on the selected input. Once identification and the short name read
finish, the shared compact header shows `FM9 · PM-TEST` or `FM9 · FM9`.
A decoded blank name displays `FM9 · Unnamed device`. An unavailable or
unrecognized name leaves the validated model-only status.

Failed identification closes both ports and shows the connection error.
Disconnect, reported output failure, and disappearance of the selected Windows
MIDI port clear the identity. Port enumeration runs once per second.

Cancellation and generation checks reject callbacks from expired attempts;
callbacks from previously opened native input objects are ignored. As this
protocol carries no transaction ID, a delayed same-model wire reply arriving
in a later matching request window cannot be distinguished from a fresh reply.

## Remaining physical verification

- Test Preset Maestro's new read sequence on FM9 firmware 12.0, including after
  power-up with FM9-Edit closed. The captured editor also queries firmware and
  capabilities before reading the name; whether any startup dependency exists
  must be checked on hardware.
- The supplied capture explicitly changes the name to FM9; it does not prove
  that clearing the name automatically returns FM9. Blank and eight-character
  maximum names have synthetic tests but still need hardware captures.
- Non-ASCII names are not guessed or decoded; obtain samples before adding them.
- FM3/Axe-Fx III identity mapping is tested with synthetic replies. Their full
  name-read transactions are not captured, so only model labels are enabled.
- Unplugging a DIN cable behind a still-present USB interface is not detected
  by Windows port enumeration; no device heartbeat is added here.

## Files changed for the capture-driven correction

- `PresetMaestro/Midi/FractalDeviceInformationClient.cs`: captured identity and
  FM9 name requests, strict packed-name parser, redacted diagnostics.
- `PresetMaestro.Tests/FractalDeviceInformationTests.cs`: capture replay,
  malformed frames, bounds, cancellation, and correlation tests.
- `PresetMaestro.Tests/FractalConnectionTests.cs`: real decoded name in the
  shared header, plus existing failed-connect and disconnect checks.
- `PresetMaestro.Tests/PresetMaestro.Tests.csproj`: copy capture fixtures for tests.
- `PresetMaestro.Tests/Fixtures/Fractal/`: selected read-only exchange fixtures
  and provenance, excluding unrelated device traffic.
- `FRACTAL-DEVICE-INFORMATION.md`: corrected protocol and verification limits.
