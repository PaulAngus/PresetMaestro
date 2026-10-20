# Physical Fractal tests

These are developer diagnostics, separate from the normal hardware-independent suite. They use the host platform's production MIDI transport, stored-preset reader, decoder and catalogue. Windows hardware evidence is recorded below; the new macOS/CoreMIDI transport still needs physical acceptance. Run the scripts with PowerShell 7 (`pwsh`) on Mac. Each run writes TRX, timestamped transmitted/received MIDI frames and JSON evidence beneath ignored `build_test/hardware` or `build_test/compatibility`; it does not open the application's profile store. Close PresetMaestro and other MIDI editors first, and run only one physical suite at a time.

## Commands

```powershell
# FM9: identity, firmware, scene/name reads, repeated stored reads, cancellation,
# timeout recovery, port close/reopen and unchanged active preset/scene.
.\scripts\Test-Hardware.ps1

# Also scan all 512 FM9 slots through the production scanner and checkpoint store.
.\scripts\Test-Hardware.ps1 -FullScan

# Application commands + physical readback. Use the actual Scene Select CC assignment;
# 34 below is an example, not an assumed FM9 setting. Presets/scenes WILL change.
.\scripts\Test-Hardware.ps1 -AllowNavigation -MidiChannel 1 -SceneCc 34

# Read-only compatibility: configured capacity boundaries, decoding, amp catalogue.
.\scripts\Test-Compatibility.ps1 -Model FM9

# Explicit opt-in: active presets and scenes WILL change; no further prompt.
.\scripts\Test-Compatibility.ps1 -Model FM9 -AllowNavigation -Slots 0,127,128,255

# Address every slot for read-only compatibility (each stored read is repeated).
.\scripts\Test-Compatibility.ps1 -Model FM9 -FullScan

# Another device: choose the exact revision/capacity and ports.
.\scripts\Test-Compatibility.ps1 -Model AxeFxIIIMarkII `
    -InputPort 'Axe-Fx III MIDI In' -OutputPort 'Axe-Fx III MIDI Out'
```

Both scripts accept explicit `-InputPort` and `-OutputPort`. Otherwise exactly one port matching the chosen family must exist; ambiguity fails. Compatibility models are `FM9`, `FM3`, `AxeFxIIIOriginal`, `AxeFxIIIMarkII` and `AxeFxIIIMarkIITurbo`. Identity distinguishes families, not Axe-Fx III hardware revisions: select the correct variant. The definitions expect 512 slots except Mark II/Turbo's 1024. Tests verify selected addresses, including bank boundaries and the last configured slot; they do not infer capacity from missing replies or prove that no higher slot exists. Custom `-Slots` are zero-based and take precedence over `-FullScan`.

For `Test-Compatibility.ps1`, `-AllowNavigation` is the explicit developer authorization to load presets and all eight scenes for the selected slots. Preset navigation uses the application's bank/program-change sender; scenes use the documented scene-select SysEx, avoiding dependence on a user-configured scene CC. Use `-MidiChannel` (default 1), with the device's program-change mapping disabled. Readback verifies actual selection, names are checked against saved data, and each saved preset is reread to detect changes. State and scene-name reads after navigation retry missed replies within an eight-second deadline (500 ms per query, 100 ms between attempts). Only these idempotent reads are retried; preset/scene changes are not resent, and amp-roster queries retain their no-retry rule. Persistent silence or a wrong selection fails with diagnostic context. The suite attempts to return to the starting preset/scene and reports restoration failure. Loading another preset can discard unsaved edits; restoring the selection cannot restore those edits. No store, parameter-write, bypass-write, reset, firmware-update or MIDI-thru operation is allowed, even in navigation mode. Compatibility `-FullScan -AllowNavigation` intentionally navigates every slot and is much longer.

Normal `dotnet test` runs skip all ten physical test entry points. Five existing diagnostics cover reads, scans and protocol navigation; five application acceptance cases cover the user-command matrix below. Entry-point counts are not a measure of outcome coverage. Explicitly enabled tests fail when hardware is absent, unresponsive or incompatible; absence never counts as a pass. The harness has offline tests for its transmit allowlists, explicit Scene CC permission, model/capacity bounds, packet fragmentation/correlation, cancellation, amp-name parsing and selection readback.

## Application command acceptance, added 20 October 2026

The original diagnostics did not call `SendFavorite` or `SendScene`: those operations threw `NotSupportedException` in the hardware harness. Successful SysEx scene navigation therefore did not establish that the application's configured Scene CC worked. The application suite uses a headless `MainWindow`, production command handlers and production MIDI transport. Each successful assertion queries the FM9 for the actual selected preset/scene; a successful send return value alone is insufficient.

| Action | Required physical outcome | Cases |
| --- | --- | --- |
| Type a preset and press Enter | FM9 selects the intended zero-based slot | Slots 0, 1, 126, 127, 128, 255, 256, 383, 384, 510, 511 with display offsets 0 and 1 |
| Type a seeded favorite slot and press Enter | FM9 reaches both the favorite preset and scene using the configured Scene CC | All eight scenes in slots 127 and 128, both display offsets |
| Invoke Go to scene on the current preset | Scene changes without a preset reload | All eight scenes in slots 127 and 128; additionally assert no program-change call |
| Next, Previous, Last | Expected wraparound, bank crossing and return to previous selection | 511 to 0, 0 to 511, 127 to 128 and back, both display offsets |
| Send several preset selections without waiting | FM9 finishes on the final requested preset | 127, 128, 255, 256, 511, 0 from a verified different starting selection |

`Test-Hardware.ps1 -AllowNavigation` requires an explicit `-SceneCc` matching the device's Scene Select assignment. Use its actual MIDI channel and disable device program-change mapping. Save/discard edits first and leave the hardware controls alone during the run. Each case refuses to navigate if the FM9 reports an edited starting preset, records saved snapshots of touched presets, and attempts restoration even after a failure. Restoration uses scene-select SysEx so an incorrect CC assignment cannot itself prevent scene restoration. This protects saved data but cannot recover edits made concurrently during a test.

State queries share the application's serialized client with its background reads. Verification retries reads for up to twelve seconds per outcome; commands are not resent. Each scenario writes `application-acceptance.json` with expected and observed selections, timings, restoration outcomes and failures alongside its MIDI trace. Preflight failures appear in TRX and the trace, before a scenario report is created. `-FullScan` additionally enables the separate read-only scan; it does not expand this command matrix to all presets.

These cases seed settings/favorites and inject an identified connection. They do **not** prove Connect/profile setup, favorite creation/persistence, pointer hit targets, external footswitch operation, physical cable loss/reconnection, or audio output. Those need separate acceptance evidence; offline coverage is not physical verification. For an FM9 release claim, retain a passing application report, compatibility report and full read-only scan from that build/firmware/platform. An offline test pass with these cases skipped is not a hardware acceptance pass. The October 5 results below predate this application suite and do not validate it.

## Amp-name and firmware compatibility

Here “amp name” means the Fractal **amp model selected in an Amp block/channel**, such as `59 Bassguy Bright`, rather than an `Amp 1` instance label. `amp-compatibility.json` records each device ID/name, code name, and `Match`, `Name differs`, or `Unknown ID or firmware scope`. Unknown IDs, renamed models and an unsupported catalogue firmware interval fail this check and remain in the report. The read-only roster probes all amp choices, including models unused in saved presets.

The verified roster adapter is FM9 firmware 12.00, editor read opcode `0x01`, sub-action `0x1F`, effect 58, parameter 10. It reads until an unrecognized descriptor, with a 1024-entry bound. The observed boundary of 336 names is accepted only for FM9 12.00. An unrecognized descriptor on another firmware is **incomplete evidence**, not a successful end-of-table result. Timeouts stop the probe: responses do not echo the ordinal and retrying could associate a late name with the wrong ID.

FM3 and Axe-Fx III share diagnostic name/state queries and have candidate stored-preset decoders, but do not yet have verified device-name/amp-roster parameter adapters or populated amp catalogues. The compatibility run explicitly reports that gap as a failure. A developer with captured read-protocol evidence can supply `-AmpTypeParameter` to exercise the same bounded roster transaction on another model; a successful response does not by itself verify its completeness. Do not copy FM9 parameter numbers or amp IDs into another model's production catalogue by assumption. Captures and reports from those devices are the evidence needed to add their verified adapters. The normal application's live-name client remains FM9-specific; diagnostic queries do not silently enable unsupported UI functionality.

`capacity.json` includes decoded amp IDs/names, channel and scene state, hashes, per-slot errors, configured model/capacity, firmware and timing. `navigation.json` records each visited preset/scene and failures. Raw traces permit replay and protocol review; they contain the device name and user preset names, so review them before sharing.

## Hardware attempt, 20 October 2026

The user authorized physical navigation with edits saved. Read the active `Default` profile from `%APPDATA%/PresetMaestro/Default-settings.json`: MIDI channel 1, Scene CC 34; machine port selections were empty. Ran `Test-Hardware.ps1 -AllowNavigation -MidiChannel 1 -SceneCc 34 -FullScan` on `PAULPC`. All seven enabled cases failed at port discovery before transmitting any MIDI. Only virtual `Preset Sender` and `PresetMaestro-Test-f5a180b19b` ports (and their relays) were enumerated. Windows retained FM9 device records but reported its USB device's `DEVPKEY_Device_IsPresent` as false. No application command outcome, full scan or restoration was exercised. The compatibility suite was deferred until the physical endpoint is available; no code assertion was relaxed to turn absent hardware into a pass.

Evidence: `build_test/hardware/paulg_PAULPC_2026-10-20_21_10_40_net10.0.trx` and the seven timestamped trace folders beginning `20261020-201037` through `20261020-201051`. This failed attempt does not supersede the earlier physical results or validate the newly added application scenarios.

### Forwarded FM9 acceptance and correction, 20 October 2026

The user clarified that the physical Mac's Forwarder connection had to be closed first. Closed that client on `Pauls-MacBook-Air.local` over SSH, then used the Windows production Forwarder client service with its saved route, `tcp://192.168.2.155:5180`, and public `MIDI Forwarder` input/output. No direct USB connection to PAULPC was required. The relay ran temporarily for this acceptance session and was stopped afterward; no app or Forwarder settings were saved by the test harness. Identity confirmed FM9 firmware 12.00. Commands used the active Maestro profile's MIDI channel 1 and Scene CC 34.

The first connected hardware run passed the read-only diagnostics, full scan, rapid selections and scene-only navigation, but failed typed presets, favorites and Next/Previous/Last. Traces showed those failures had no state queries during a 15-second quarantine after cancelling the application's background stored-preset dump. The remaining valid dump frames arrived within roughly half a second. A successful MIDI send had therefore hidden a significant pause in state/name updates.

`PresetNameClient` now retains the cancelled dump's decoder and discards its remaining frames through the fully validated image/CRC/footer before releasing quarantine early. It preserves the original quarantine when that boundary cannot be established. A cancelled caller still receives cancellation; a subsequent read gets fresh data rather than the cancelled image. Six regression cases cover cancellation before/within the dump, fresh state and same-slot rereads, and missing/corrupt completion. Four positive cases failed on the previous implementation; all six pass with the correction. All 39 focused read/diagnostic/recovery tests pass in both standard and OfflineWindows configurations, and the no-index Release build passes.

All **ten distinct physical cases passed after the correction, with zero failures or skips**, across the application, compatibility and read-only runs. Their evidence is combined in `build_test/fm9-live-20261020/acceptance-summary.json`, including the tested app assembly's SHA-256 and exact TRX/trace paths. The five application scenarios recorded **100 matching readbacks**: 90 action observations and ten restoration observations. The maximum observed readback time was 899 ms; this is an observation from this route, not a general timing guarantee. All touched saved-preset comparisons passed. Compatibility verified all 336 amp names and all eight scenes in six presets. The final full scan checked all 512 slots, decoded 254 populated presets, skipped 258 explicit empty slots and reported zero errors; its scan phase took 115.074 seconds. It preserved zero-based slot 2 and scene index 0 (displayed scene 1), which was also restored by each application scenario.

Reproduction for the same established route:

```powershell
.\scripts\Test-Hardware.ps1 -AllowNavigation -FullScan -MidiChannel 1 -SceneCc 34 `
    -InputPort 'MIDI Forwarder' -OutputPort 'MIDI Forwarder'
.\scripts\Test-Compatibility.ps1 -Model FM9 -AllowNavigation -MidiChannel 1 `
    -InputPort 'MIDI Forwarder' -OutputPort 'MIDI Forwarder'
```

The Forwarder connection must already reach the intended hardware before running these commands. Original failing reports remain under `build_test/hardware`; they were not replaced or treated as passes. The separate five-case application rerun and two-case read-only rerun establish coverage of every hardware case on the corrected assembly without repeating the already passing application matrix unnecessarily.

Final separate software regression: **954 passed, zero failed, ten physical cases skipped**. The ten cases were enabled and passed in the live acceptance runs above. Its TRX is `build_test/fm9-live-20261020/software-regression.trx`; the two-minute per-test hang guard detected no hang. The app assembly hash remained identical across hardware acceptance and the software rebuild.

## Evidence and limitations, 5 October 2026

Connected device: FM9, firmware 12.00, `FM9 MIDI In` / `FM9 MIDI Out`. Read-only compatibility passed for all 336 device amp names and repeated preset reads at slots 0, 127, 128, 255, 256 and 511. Navigation was subsequently exercised on the FM9 after the developer invoked `-AllowNavigation` and reported a failure. The corrected suite passed all three physical compatibility tests, visiting all eight scenes in six presets (48 selections) and restoring its starting selection. FM3/Axe-Fx III and other firmware versions have not been physically validated.

The first full production scan read 512 slots, including 254 populated presets, with zero errors in 91.728 seconds. The final rerun is recorded in `build_test/fm9-hardware-final.log` and its timestamped trace. The baseline passed state preservation. The final run decoded all 512 slots (254 populated, zero errors) in 159.517 seconds but correctly failed state preservation: Eruption remained selected while the active scene changed from index 1 to 5. The trace contains only read requests from this harness, plus unsolicited scene responses during the scan; the cause remains unconfirmed. A further full rerun passed in 161.895 seconds with the same 512/254 counts and zero errors, preserving Eruption (slot 130), scene index 5 throughout. Evidence: `build_test/fm9-hardware-rerun.log` and `build_test/hardware/20261005-073305-42b5e7cfbd294a7ebc24e7a2466f6819/trace.jsonl`. The failed run remains available; it was not suppressed. Cancellation and receive-timeout tests use valid read requests while deliberately suppressing received replies. Port reopening is real; cable removal/reconnection and physical driver hangs remain manual checks. These tests make no audio-quality or DSP-performance claims.

A real saved-preset transaction from slot 0 is checked into `PresetMaestro.Tests/Fixtures/Fractal/fm9-12-slot-000.hex`, with expected decoded data beside it. Replay verifies scenes, channels, models, checksums and normalized fingerprints. Bypass and parameter changes are applied only to in-memory fixture bytes. The amp parser replays every raw reply in the existing `docs/catalog/fm9-12-device-roster.json`, linked into test output rather than copied into a second source file.

## Protocol sources

- [Fractal Audio: Axe-Fx III MIDI for third-party devices, revision 1.4](https://fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf): preset-name, scene-name and scene-selection/query formats. This official document describes Axe-Fx III; use on FM9 is additionally supported by the physical tests, while FM3 remains a candidate.
- [Captured FM9 amp roster](catalog/fm9-12-device-roster.json): evidence for the private amp-name read transaction, which is not specified by the public document.
- [Device discovery evidence](../FRACTAL-DEVICE-INFORMATION.md) and [stored-scene protocol notes](../SCENE-NAMES.md).

### Navigation harness correction

The original developer run at 07:51 UTC on 5 October reached slot 127 and acknowledged scene 0, but the immediately following current-preset query received no reply. The one-shot three-second wait failed despite successful navigation; restoration succeeded. A diagnostic rerun also captured an unanswered scene-name query after a successful state read. Bounded read retries address both cases without relaxing name/content assertions. Offline tests cover dropped preset and scene replies, wrong selection, permanent silence, caller cancellation, and rejection of another scene's delayed name. The final physical run passed all three compatibility cases in 10.277 seconds. Evidence: `build_test/navigation-recovery-unit.log`, `build_test/navigation-recovery-hardware.log` and the corresponding timestamped navigation report/trace. This demonstrates transient missed replies, not a claim about the device firmware's internal cause.
