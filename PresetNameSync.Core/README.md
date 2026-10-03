# PresetNameSync.Core

Shared, read-only preset/scene-name and stored-dump parsing used by the application. For user instructions, start with the [main README](../README.md).

The frames below are verified for FM9 only. The controller prevents these queries on FM3 and Axe-Fx III until their corresponding transactions have been captured and tested.

## Contract

`SysexProtocol.BuildPresetNameQuery(slot)` creates only:

`F0 00 01 74 12 0D pp pp cs F7`

`TryParsePresetNameResponse` accepts only correctly framed device opcode `0D` responses with a valid manufacturer header, model byte, length, and XOR checksum. It decodes the 32-byte ASCII name, stops at NUL, and trims trailing spaces.

Scene additions:

- `BuildSceneNameQuery(index)` and `TryParseSceneNameResponse`: live scene names, indices 0–7, opcode `0E`.
- `BuildCurrentPresetQuery()` and `BuildCurrentSceneQuery()`: read current selection without changing it.
- `SysexAssembler`: converts fragmented or combined MIDI callbacks into frames, ignoring interleaved realtime bytes.
- `StoredPresetDecoder.BuildQuery(slot)` and `Accept(frame, expectedSlot)`: request and decode one stored preset, yielding its preset name and eight scene names only after checksum/CRC validation.
- `PresetScenes`: a zero-based preset slot, its name, and eight scene names. Empty strings are valid names, not missing scenes.

## Integration rules for AI/code programmers

- Use the existing application's open MIDI manager; never open a second `MidiIn` or `MidiOut` for the device ports.
- Send the query with `MidiOut.SendBuffer(byte[])` through the manager's serialized SysEx method.
- Subscribe to `SysexMessageReceived`; do not use short-message handling for SysEx.
- Allow one outstanding request at a time. Preset-name responses identify the slot; scene-name responses identify only the scene. Verify the active preset before and after a live snapshot, and invalidate it on preset changes/cancellation.
- Queries are read-only. Do not add Program Change, Bank Select, scene-select CC, store, or rename behavior here. Scene-name/current-state reads are in `SysexProtocol`; the separately validated stored-preset `0x03` reader is in `StoredPresetDecoder`.
- Keep timeout, cancellation, and progress in the caller/UI layer.
- Add protocol tests before changing the frame format.

Verify new device protocol behavior with bounded read-only hardware checks before exposing it through the production MIDI manager and an explicit user action. Temporary investigation tools should be removed when the investigation is complete.

A read-only FM9 slot-0 stored dump and eight scene names were verified through `MIDI Forwarder` on 2026-09-30 using a temporary console harness. The obsolete standalone probe and investigation harnesses were removed on 2026-10-03; shared-protocol regression tests are in `PresetMaestro.Tests`. See [SCENE-NAMES.md](../SCENE-NAMES.md) for the observed scope, wire format, application workflow, and remaining validation. Passing synthetic tests alone is not a claim of hardware validation.
