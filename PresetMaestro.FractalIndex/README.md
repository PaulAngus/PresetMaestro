# Fractal Preset Index

This optional project decodes the saved, programmed Amp data for a preset. It has separate FM3, FM9 and Axe-Fx III device definitions and a catalog registry that selects names by family, optional hardware variant and firmware. It does not interpret Scene Ignore or claim that a saved scene equals the effective live state.

The FM9 body offsets were checked against a connected FM9 running firmware 12.00. The FM3 and Axe-Fx III body layouts and shared saved-dump envelope are candidate rules from the reviewed open implementation; neither device has been tested on hardware here. Axe-Fx III callers must select Original (512 slots), Mark II (1024) or Mark II Turbo (1024) explicitly. A generic Axe-Fx III identity reply does not establish that revision.

| Definition | MIDI model | Slots | Maximum Amp blocks | Grid size used by decoder | Amp record / model field |
|---|---|---|---|---|---|
| FM3 | `0x11` | 512 | 1 | 12 columns × 4 rows | 144 words per channel; parameter word 0 |
| FM9 | `0x12` | 512 | 2 | 14 columns × 6 rows | 147 words per channel; parameter word 4 |
| Axe-Fx III | `0x10` | 512 or 1024 | 2 | 14 columns × 6 rows | 142 words per channel; header-based model field |

All three decode four Amp channels and eight programmed scene states. Grid size is a binary-format detail: the decoder uses the grid only to establish which Amp instances are present and check the record count. Grid coordinates and cables are not stored in `AmpBlockSnapshot` or used as Amp identity, search criteria or tag keys. Moving a block leaves its indexed Amp values unchanged; a lone Amp 2 remains Amp 2. The raw image hash detects changed saved bytes and is not a logical preset identity.

`PresetNameClient` implements `IStoredPresetImageSource` when the feature is enabled. Its existing request gate and timeout quarantine serialize index reads with the application's name and scene reads. `PresetIndexReader` validates the returned slot, decodes the body, resolves catalog names and exposes distinct `Contains`, `Selects` and `Uses` predicates for saved programmed state.

The FM9 catalog contains all 336 ID/name pairs read directly from the connected FM9 running firmware 12.00 on 2026-10-03. The index lookup and Amps browser use the same captured names. Raw replies and unchanged-device checks are preserved in [the device roster evidence](../docs/catalog/fm9-12-device-roster.json). FM3 and Axe-Fx III each have an initially empty catalog; neither inherits FM9 names. Other FM9 firmware versions require their own verified roster. Unknown IDs remain visible and searchable numerically. Real amplifier attributions are separately reviewed in the browser catalogue; numeric confirmation does not prove an exact reference year or circuit.

FM3 regression coverage includes a synthetic saved dump at slot 511 through the application's MIDI client and index reader, its channel offsets, scene states, grid boundaries, relocation invariance, rejection of unsupported Amp instances, device mismatch rejection and catalog isolation. Synthetic tests verify implementation consistency, not device compatibility. Sources and hardware evidence are recorded in [the protocol research](../docs/fractal-protocol-research.md).

The app project enables this module by default. Build it without the feature using:

```powershell
dotnet build PresetMaestro/PresetMaestro.csproj -p:EnableFractalIndex=false
```

## First usable workflow

The application now registers a Preset Index page and a Config card for device libraries. See [the usage guide](../docs/fractal-index-usage.md).

- `IndexScanner` reads saved presets through the existing serialized MIDI client. Each successful slot is checkpointed. Cancel, disconnect and three consecutive failures stop the job; Resume skips completed slots. A failed or partial scan never replaces the previous complete index.
- The host scans with `publish: false`: complete candidates remain in `LastAttempt` until the library match passes or the user confirms replacement. The first sync establishes a confirmed baseline. On connection, the user chooses a quick or full comparison. `LibraryMatch` compares same-slot raw-image fingerprints and scene names. Quick match randomly samples up to 16 populated slots without replacement; full match reads every slot to detect additions as well as changes. Checks do not publish a scan. A profile's `PresetMatchThresholdPercent` defaults to 99 and is adjustable in Config (1–100). The device name recorded in a scan is separate from the library's friendly name. Matching supports a probable content association, never unique physical identity or automatic profile reassignment.
- `IndexLibrary` stores one JSON file per local device ID in `%APPDATA%/PresetMaestro/FractalIndex`, with atomic replacement and a `.bak` copy. It stores decoded facts and hashes, never raw SysEx dumps. An imported device gets a fresh local ID and remains marked imported/offline until a full confirmed sync.
- `IndexProfile` stores one active library assignment and preset/scene tags inside the existing profile settings JSON, in an optional versioned `FractalIndex` object. Multiple profiles can share the same cache. The schema-1 `SelectedDeviceId` field is the single assignment; `Devices` also retains historical references so reassignment preserves old tags and exports. Those historical references are not browsing choices. Builds without the feature preserve this opaque object.
- Profile copy generates a new profile ID and independent annotations while retaining cache links. Rename and delete naturally carry the embedded data with the existing settings file. Export includes decoded device snapshots inside that object, so the ZIP remains the established settings/favorites pair. Import remaps device IDs and annotation references together and never merges by port or name.
- Search is local and matches names, model names/IDs, preset tags and scene tags. Any/All tags apply to the union of tags on a preset and its scenes. Only the selected preset's scene and channel controls are created.
- Tags bind to device, slot, firmware and exact saved content evidence. Changed content, moves, swaps and overwrites retain the original annotations as **Needs review**. The review permits explicit preset and scene-number reassignment and an undo. No name-only or Amp-model-only fingerprint transfers tags automatically.

The separate **Amps** browser now uses `AmpBrowserCatalog` for its device-confirmed FM9 12.00 roster and explicit family mappings. `AmpContainsFilter` qualifies raw IDs by library, hardware and firmware. Display-time resolution leaves saved snapshots and hashes unchanged. Personal `AmpReferenceStore` links are separate from catalogue authority and travel in profile exports. See [the implementation notes](../docs/amp-browser-implementation.md) and `Catalog/NOTICE.txt` for evidence and licensing.

Other device/firmware rosters, normalized rename/scene fingerprints, automatic Favorites target changes and remote cheap-change detection remain unimplemented. A fresh sync checks all slots; on FM9, a fresh name query returning the exact `<EMPTY>` convention skips the full dump. Blank/unknown names and failed checks fall back to full reads. `NameOnlyEmpty` records retain the evidence without inventing a content hash or editable scenes. Resume rechecks these slots and continues missing full reads. The list reuses Favorites' explicit-empty edge trimming. Saved scene state does not account for Scene Ignore. FM3 and Axe-Fx III hardware remain unverified; their decoder and catalogue paths are distinct and currently retain full reads.
