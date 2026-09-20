# device scene names

For the everyday button-by-button guide, start with the [main README](README.md). This document describes the behavior and implementation in more detail.

The approved layout does not currently expose standalone scene buttons. Scene names are used by the favorite editor and selected-favorite detail display. Loading a preset refreshes cached names using read-only `0x0E` queries, while sending a favorite selects its saved scene using the configured scene CC.

## User manual note: required device Scene Select assignment

Scene selection requires configuration on both sides; choosing a Scene CC in the app does not configure the device automatically.

1. On the device, open **SETUP → MIDI/Remote → Other**.
2. Set **Scene Select** to the same controller number shown under **Config → Preset Mapping → Scene CC#** in the app.
3. The app defaults to CC#34. CC#34 is a Fractal convention/example for this purpose, not an industry-standard MIDI scene command.
4. The app sends values `0–7` for Scenes `1–8` (`value = scene number - 1`).

The device may have **Scene Select = None**. In that state, preset Bank Select and Program Change messages still work, but the device ignores the subsequent scene CC. This can look like an app timing fault even when the MIDI log shows the correct messages. Physical device testing on 2026-09-18 confirmed that changing Scene Select from **None** to **34** resolved this exact symptom; no artificial delay between the MIDI messages was required.

`Sync all stored scene names` reads slots 0–511 with `0x03` and extracts their names without selecting any preset. It is explicit because a DIN scan transfers roughly 12.6 MB and can take over an hour. Cancellation keeps completed entries; a failed/unsupported dump stops the scan and preserves the cache. A preset change cancels background sync so foreground actions take priority. Saved-dump names never replace the current live names during a scan.

The favorite editor shows cached scene names for its preset. `Read names for this preset` reads the active preset's live names, or that slot's stored dump if it is not active. It never loads a preset merely to populate the picker.

Caches persist in the existing settings file, keyed by MIDI input/output port pair and zero-based preset slot, with source and retrieval time. Display offset does not change keys. Two physical units with identical port names cannot be distinguished: use Clear scene-name cache when changing such units. Saved dumps contain saved names; live queries may reflect unsaved edits. Refresh current scene names after editing names externally.

## Connections and change detection

Use the existing app MIDI connection. For DIN, connect both directions and enable Send MIDI PC on the device. USB MIDI is also supported. PC notifications trigger a current-preset query (not a guessed bank); a two-second current-preset/current-scene poll covers changes without notifications. The current scene indicator updates without re-fetching names. Polling pauses during name scans.

All controller reads share one request queue. Live snapshots check the preset before and after, track PC notifications (including away-and-back changes), and use cancellation/generation guards. Cancelled or timed-out requests have a response quarantine before the next query. SysEx has no transaction IDs: arbitrary late replies or an unreported away-and-back hardware change cannot be perfectly identified. Names update atomically only after a complete, consistent snapshot. Missing replies retain cached names instead of inventing an empty scene.

## Troubleshooting and current limits

| What you see | What to check or do |
|---|---|
| Only `Scene 1`–`Scene 8` | These can be unnamed scenes or names that have not been retrieved. Read the status above the buttons and click Refresh. |
| Cached names remain after a failed refresh | Verify both MIDI directions, then click Refresh. A failed read does not erase previously known names. |
| A Favorite loads the correct preset but never changes scene | Check **SETUP → MIDI/Remote → Other → Scene Select** on the device. It must not be **None** and must match Config → Scene CC# in the app. |
| Port cannot be opened | Close the controller before opening the probe, or vice versa. Close device editor/other applications that hold the same port. |
| Names do not change after an external rename | Use Refresh current scene names. Polling tracks preset and scene selection, not name edits. |
| Bulk scan stops | Completed entries remain. A preset change cancels the scan; an invalid/unsupported dump or timeout stops it. Read the status and verify a single slot with the probe. |
| A read waits after cancellation | The request queue deliberately waits for possible late replies: up to the previous request timeout (1.5 seconds for a name read, 15 seconds for a stored dump). |
| Names from another device appear | Clear the scene-name cache after changing to a unit with the same MIDI port names. |

Bulk sync starts at slot 0 each time; there is no resume-from-last-slot feature. It does not start automatically on connection. Clearing the cache does not immediately re-read the current preset; use Refresh afterward. The favorite editor uses cached names when its preset field changes; use Read names for this preset when those names are missing or stale.

The new UI does not rename scenes, save presets to the device, or import `.syx` files. Its dump decoder is used internally to read saved names. Scene selection remains the existing configurable MIDI CC behavior.

## Code map

| File | Responsibility |
|---|---|
| `PresetMaestro/MainWindow.Scenes.cs` | Scene UI, polling, caches, favorite picker, and bulk scan |
| `PresetMaestro/Midi/PresetNameClient.cs` | Shared request queue, current-state/name queries, cancellation and response matching |
| `PresetMaestro/Midi/MidiManager.cs` | Existing MIDI ports, scene CC sends, and preset-change notifications |
| `PresetMaestro/Core/SceneCacheEntry.cs` | Cached names, retrieval time, and live/stored source |
| `PresetNameSync.Core/SysexProtocol.cs` | device query frames, checksum and response parsing |
| `PresetNameSync.Core/SysexAssembler.cs` | Split/combined SysEx callback reassembly |
| `PresetNameSync.Core/StoredPresetDecoder.cs` | Stored-dump validation, unpacking and scene-name extraction |
| `PresetMaestro.Tests/Scene*Tests.cs`, `StoredSceneTests.cs` | Protocol and simulated MIDI regression tests |

## Protocol and validation

- device header: `F0 00 01 74 12`; XOR checksum masked with `7F`.
- Scene-name query: opcode `0E`, one zero-based scene index; reply has index + 32 ASCII bytes.
- Current preset: opcode `0D`, `7F 7F`; current scene: opcode `0C`, `7F`.
- Stored dump request: opcode `03`, `[slot >> 7, slot & 127, 00]` (big-endian, unlike preset-name queries).
- Dump frames: `77` header (13 bytes), eight `78` chunks (3082 bytes), `79` footer (11 bytes).
- Unpack each chunk's 3072 data bytes to 1024 little-endian 16-bit words using three septets per word. Verify image magic, CRC-16/CCITT (seed AA55, CRC field zeroed), footer word XOR, and all frame checksums.
- Raw-image sizes at 48/4A hex; Huffman stream at 4C hex. Eight 32-byte scene names start at decompressed offset 4. Bounded decoding rejects malformed trees, truncation, bad sizes, and incomplete dumps.

Sources (format facts; implementation is independently written in C#):

- https://fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf
- https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/presetDump.ts
- https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-midi/src/shared/presetContainer.ts
- https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/presetBody.ts
- https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts

## Verification

Run controller tests and probe tests with `dotnet test` on their respective project files. Tests cover documented wire bytes, malformed frames, fragmentation, complete synthetic compressed dumps, wrong slots, corruption, cancellation, late replies, and device changes. The probe has explicit current-scene and stored-slot scene reads with raw logging. First verify one saved preset and its eight names on the device, then try bulk sync. Favorite scene selection has been verified on physical device hardware with Scene Select assigned to CC#34; other protocol behavior still requires hardware verification where noted.
