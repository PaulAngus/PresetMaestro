# Preset Index data and existing profiles

Design and implementation notes, 2026-10-01. The first usable Preset Index now includes scanning, JSON caching, tags and a compact scene inspector; see [the usage guide](fractal-index-usage.md). The separate Amps finder and complete real-amp catalogues remain planned. Protocol evidence remains in [fractal-protocol-research.md](fractal-protocol-research.md).

**Implemented persistence choice:** tags, profile ID and device links are embedded in a versioned `FractalIndex` object inside the existing profile settings JSON. This replaces the sidecar proposal below and lets existing rename/delete behavior preserve the feature data. Device snapshots use shared per-device JSON files, not SQLite. Export embeds compact decoded snapshots in that object, retaining the existing two-file ZIP. Import remaps local device IDs and annotations together and marks snapshots imported. The sidecar/manifest examples below describe earlier design alternatives, not the current file format.

The optional [`PresetMaestro.FractalIndex`](../PresetMaestro.FractalIndex/README.md) module supplies the decoder, reader, scan cache and profile tag model. It includes distinct FM3, FM9, Axe-Fx III Original, Mark II and Mark II Turbo definitions. FM3 and Axe-Fx III saved-dump exchanges and body formats remain hardware candidates. Axe-Fx III registration requires an explicit revision choice because the current device identity reply does not distinguish the 512-slot Original from the 1024-slot variants. FM3 has its own 512-slot, single-Amp definition and body layout. The catalog registry selects entries by device family, optional variant and firmware. FM9 names must never be used as silent fallback names for FM3 or Axe-Fx III IDs: an unknown ID remains visible until a suitable device catalog is available. This wiring makes future support possible without claiming either device has passed hardware validation.

An indexed Amp is an existing block instance (Amp 1 or Amp 2), its channel model IDs and its scene settings. Its routing-grid coordinates are not indexed or used as identity, tag keys or search criteria. Moving a block alone must not change that Amp's indexed values. The saved-byte hash remains transfer/change evidence, not a logical identity; future preset reconciliation must not interpret a changed hash alone as proof that tags belong to a different preset.

## One view assembled from three owners

| Owner | Existing or proposed storage | What it owns |
|---|---|---|
| This computer | Existing `settings.json` | Active profile, MIDI ports, theme and other machine choices. It must not become the owner of preset/scene tags. |
| Profile | Existing `<name>-settings.json` and `<name>-favorites.json`; proposed optional `<name>-index.json` | Performance mapping, Favorites, profile ID, associations with device records, preset tags and scene tags. Favorite tags remain assignments to Favorites. |
| Physical device record | One feature-owned cache under `%APPDATA%\PresetMaestro\FractalIndex\` | The last committed scan: device capabilities, firmware, slots, preset/scene names, Amp blocks/channels, scan quality and timestamps. It is shared by every associated profile. |
| App catalog | Versioned feature resource | Available Fractal Amp models and mappings to real amp families, with firmware/source/confidence. It is neither a user's profile nor a scan of used presets. |
| Personal Amp references | Small app-level JSON file, keyed by stable catalog family/variant IDs | Optional links the user adds to community Wiki pages; shared across profiles and devices, separate from authoritative mappings. |

The Preset Index view joins the profile's assigned device library to its tags and Favorites context. Amps reads the firmware-qualified catalog, then counts distinct matching presets in that library's committed scan. Editing a tag writes only profile-owned JSON; device sync writes only the device cache. Switching profiles restores that profile's single library assignment and annotation layer. Changing the assignment is a Config action, not a browsing control.

Curated Wiki references can ship with the versioned Amp catalog. A user may add extra Wiki links in the expanded Amps detail; store those in personal app-level JSON by stable family/variant ID, never by an unqualified raw model number. Validate and deduplicate URLs, include personal references in a full app backup, and carry relevant ones in a profile export without changing the underlying mapping on import. A link is reading material, not firmware availability evidence or an instruction to merge amp families.

Use a stable `ProfileId` UUID in profile settings and a local `DeviceId` UUID in the index, rather than a profile display name, MIDI port pair, slot, or editable device name as identity. No validated stable hardware ID has been found yet. The accepted design on 2026-10-02 is many profiles to one device library: each profile has one assignment, and several profiles can share a scan. The schema-1 `SelectedDeviceId` stores that assignment; `Devices` retains metadata for current and historical assignments so previous tags, review history and portable snapshots survive. Old files retain their selected library, or adopt their sole library when unselected. Ambiguous unselected files remain unassigned. Hardware model compatibility is checked when assigning; the existing sync confirmation establishes the intended physical unit for that read.

## Tags and identities

The same profile can reuse the text `Live` in a Favorite, on a preset, and on a scene. These are three separate assignments:

| Assignment | Key | Meaning |
|---|---|---|
| Existing Favorite tag | `Favorite.Id` inside that profile | Tags this named shortcut. It does not automatically tag the source preset or scene. |
| Preset tag | `ProfileId + DeviceId + TrackedPresetId` | Tags the user's logical preset on one selected unit in one profile. |
| Scene tag | `ProfileId + DeviceId + TrackedPresetId + TrackedSceneId` | Tags one scene within that preset. The local scene record includes its last known 1–8 scene number and snapshot evidence. |

The UI can suggest tag text already used anywhere in the active profile, without merging those assignments. Preset tags keep the existing Favorites pill appearance. Scene tags use the subtler scene treatment and include `S1`–`S8` when shown in a preset row, so colour is not the only distinction. The selected preset detail contains eight compact scene rows and allows an independent tag edit on each.

There is no verified device-provided preset or scene UUID. `TrackedPresetId` and `TrackedSceneId` are local identities, not values claimed to exist on the device. Each annotation record must also retain portable evidence: last known device/model/firmware, slot, names, exact or versioned normalized content fingerprints where available, scene number and scene fingerprint/status. A slot/name match alone cannot reassign tags. On rescan, unchanged snapshots can retain links; a likely move, copy, overwrite, or scene replacement creates a review candidate. Unresolved tags remain saved but are not displayed on a possibly different preset or scene until the user resolves the link. A scene-number-only binding is a physical address, not proof that the same scene still occupies it.

For Preset Index search, a preset row represents the union of its own tags and its scenes' tags. **All tags** means each selected tag occurs somewhere on that one preset or one of its scenes; it does not mean all tags occur on the same scene. Results expose the matching `S#` markers. If a future workflow needs “both tags on the same scene”, it should be a distinct scene-scoped search, not a hidden change to the existing Any/All control.

## Rename, move, swap and copy reconciliation

A scan compares committed old and new snapshots for the *whole selected device*, not just each slot against its previous occupant. Exact saved-byte fingerprints and any validated content fingerprint that excludes names are evidence; neither a name nor a slot is identity. Fingerprints are comparable only under supported device/firmware/decoder rules. A quick name sweep cannot resolve moves or swaps. Resolve only annotations/Favorites affected by uncertainty; ordinary untagged inventory changes need no review.

| Change seen after a full scan | Treatment of preset/scene tags |
|---|---|
| Same saved content at the same slot; name-only change proven by a validated name-independent fingerprint | Keep the tracked identity and tags automatically; show the new name. |
| Unique old content found at a different slot, with the old location changed/empty | Suggest a move. Keep tags with the old tracked identity until the user confirms that it is the same preset. |
| Two uniquely matching presets exchange slots | Present one grouped **swap** review. On confirmation, the tracked identities and their tags follow the sounds to their new slots. |
| Old content remains and also appears at another slot | Treat the newcomer as a copy with a new tracked identity. Offer **Copy tags**; do not share the original's tags automatically. |
| Slot, name and/or content changed without a unique supported match | Keep old tags in a pending record and ask whether the new content is an edit of the same preset or a replacement. Never display old tags on a possibly unrelated occupant. |
| Only names were refreshed, scan is incomplete, or fingerprint support is unavailable | Show the saved index as stale/uncertain and leave old tag links unresolved; do not infer a rename, move or swap from names. |

Apply the same idea *inside a confirmed preset* to scenes. If a scene's number is unchanged and a validated scene-content fingerprint proves only its name changed, its tags remain. If two uniquely distinguishable scenes exchange positions, show one scene-swap review; after confirmation the tags follow the scene content and their `S#` markers change. A copied scene gets a new local identity. If two scenes have identical content, or the decoder cannot distinguish them, there is no sound basis to pick one automatically; leave their tags pending for manual assignment. Ordinary scene parameter edits can be confirmed as **Same scene**; an apparent replacement is **Different scene**. Do not equate a matching scene name with continuity.

After a scan, show one unobtrusive **Needs review** count. Its list groups related moves/swaps and shows old/new device, slot, name, scene number and the evidence for the suggestion. Actions are **Same preset/scene**, **Copy tags**, **Different**, and **Later**; decisions are reversible from history. A confirmed choice updates the tag display and search index together. Pending tags remain visible in the review screen, not attached to an uncertain current row.

Existing Favorites are a separate safety issue: they store a displayed preset number and scene, so their send target stays at the old address even if tags follow a moved sound. After a relevant move/swap/scene rearrangement, mark affected Favorites for target review and offer the proposed new preset/scene number. Do not silently rewrite or send an unverified target, especially in a multi-device profile.

## How sync fits the current process

The existing **Sync Preset Names** is an FM9-only, fixed 0–511 name sweep. It stores names in `ProfileSettings.PresetNameCache` and retains partial progress even when cancelled. Scene names currently live in `SceneNameCaches`, keyed by MIDI port pair, with `live`/`stored` source and retrieval time. Those caches support today's Favorites and picker; a name or matching port pair does not verify the Amp content or physical device identity.

Add **Sync Device Index** as a separate, explicit, read-only operation for a confirmed device record. It uses the same serialized MIDI request path and may share progress/cancellation UI, but it must use discovered device capacity and keep a scan generation and per-slot results. A successful slot read is committed atomically. The previous complete scan stays searchable if a run is cancelled or fails; any partial run is visibly partial and never presented as a complete current Amp index. Profile switches do not start device scans.

Do not make the two caches compete as independent truths. When a verified device index is available, existing preset/scene pickers should eventually read its committed names through a small adapter; otherwise they use the current profile caches. The old Sync Preset Names action can remain a quick name refresh and may update provisional name information, but it never marks Amp, channel or scene-state data current. A full index scan can update that adapter view without rewriting every profile's JSON. Legacy name caches remain for older profiles, offline fallback, and a feature-disabled build. The scene `live` cache remains distinct from saved preset snapshots because unsaved edit-buffer state can differ from the stored preset.

## JSON and import/export

Keep small, user-authored annotations portable in JSON. An optional `<name>-index.json` sidecar contains a schema version, profile ID, device association descriptors, preset/scene tag assignments, tracked local IDs, and reconciliation evidence. It does not contain 1024 raw preset dumps. A feature-owned SQLite cache is a practical query/snapshot store, but its internal layout is not the profile interchange format. If SQLite is removed and rebuilt, the JSON annotations and their evidence must survive. All writes should use the existing temporary-file/backup pattern; tag saves must not depend on a successful MIDI connection.

Example shape, abbreviated:

```json
{
  "SchemaVersion": 1,
  "ProfileId": "profile-uuid",
  "DeviceAssociations": [
    { "DeviceRef": "portable-device-uuid", "Model": "FM9", "DisplayName": "Stage FM9" }
  ],
  "PresetAnnotations": [
    {
      "DeviceRef": "portable-device-uuid",
      "TrackedPresetId": "preset-uuid",
      "LastKnownSlot": 13,
      "LastKnownName": "Brit Lead",
      "Fingerprint": "versioned-content-fingerprint",
      "Tags": ["Live"],
      "Scenes": [
        { "TrackedSceneId": "scene-uuid", "LastKnownNumber": 3,
          "LastKnownName": "Solo", "Fingerprint": "versioned-scene-fingerprint",
          "Tags": ["Lead"] }
      ]
    }
  ]
}
```

The **normal profile export** should be a versioned ZIP containing settings, Favorites, index annotations and a compact decoded snapshot for each associated device needed for offline browsing. The same shared device snapshot may occur in separate profile exports; that duplication makes each ZIP self-contained. Raw SysEx dumps need not be included in the normal profile export. The manifest records versions, source device references, scan time, firmware, completeness and hashes. On another computer, imported snapshots are *offline/imported*, and physical-device associations are *unverified* until the user confirms them; tags remain pending against the imported device record until safely linked. A later device sync can reconcile them. Exporting a profile never edits the device.

Keep the existing two-file ZIP and matching JSON-pair import accepted as legacy input; missing index data creates an empty annotation layer. New ZIP import needs versioned validation, size limits and explicit allowed entry names instead of today's `Entries.Count == 2` check. A standalone JSON pair may optionally have a matching index sidecar; absence is valid. On import, assign a new local `ProfileId`, retain the source ID only as provenance, and remap association/annotation references together. Do not merge an imported device into an existing local record based on port or name alone. On export from an older client that cannot understand the new ZIP, compatibility is not implied; old-format export would explicitly omit the new data and warn before doing so.

## Profile and Favorite lifecycle

| Action | Required behavior |
|---|---|
| Create profile | New `ProfileId`, empty associations and tags. |
| Copy profile | New `ProfileId`; copy Favorites and tag assignments/association intent, while referring to the same local device cache. No rescan. The UI should make the choice to copy annotations clear. |
| Rename profile | Keep `ProfileId`; move the optional JSON sidecar with the existing settings/Favorites pair. All cache links remain valid. |
| Delete profile | Archive the sidecar with settings/Favorites in `DeletedProfiles`; do not delete device scans used by another profile. Orphaned device caches can be cleaned separately. |
| Import profile | Create a new local ID; import annotations and offline device snapshots, then require deliberate device binding before device-directed actions. |
| Disable optional feature | Leave profile JSON, Favorites and legacy sync unchanged; preserve inert index data for later re-enable. |

Existing Favorites store displayed preset number and scene but no physical device identity. The accepted single-library assignment avoids presenting one profile as interchangeable between units. Reassignment still does not translate a Favorite's number or scene to another unit. Keep the existing Favorite behavior and require verified device context before any future index-to-Favorite shortcut. A future shared-show feature spanning several units would require explicit per-device target mappings.

## Migration sequence

1. Add optional stable profile ID and annotation sidecar with backward-compatible loading. Preserve original files before writing a migrated version; older two-file profiles load with empty index annotations.
2. Add device identity/association records and a feature cache. Do not infer the unit from a MIDI port pair or device name. Keep current name/scene sync working while the index is disabled.
3. After the protocol gate for a given device passes, add its full scan with committed generations and a read adapter for existing pickers. Show exactly which cache supplied each displayed name when the distinction matters.
4. Add preset and scene tags, export/import, copy/rename/delete integration and reconciliation review. Test two profiles on one unit, one profile on two units, a copied preset, a replaced scene, a cancelled scan and legacy ZIP import.

This approach keeps the existing profile format and workflows recognizable, gives the new UI a shared device inventory, and ensures user-authored tags survive both cache rebuilds and profile export/import.
