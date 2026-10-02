# Using Preset Index

1. Select your profile using the **Profile** control in the application header. Open **Config → Device library for this profile**. Choose its **Assigned library**, or open **Manage library**. On that screen, choose **+ Create…**, enter a unique name, device model/revision and optional firmware, then **Create library**. Choose **Use for this profile** to assign the new library. Each profile has one assigned library; several profiles can share one library.
2. Connect MIDI using the existing **MIDI Connection** controls.
3. Open **Preset Index**. The header shows the assigned **Library** as information beside the **Profile** selector. Switching profiles restores that profile's library and tags. **Sync device** reads saved presets from the connected matching model. The first sync asks you to establish the library's baseline. Subsequent complete scans compare against that baseline before replacing it; a low or uncertain match requires confirmation.
4. The sync notice explains that reading can take several minutes or longer over slower MIDI connections. A prominent bar shows the percentage, checked slots, downloaded presets, empty slots skipped and failures together. **Cancel** retains completed reads. **Resume** checks a sample of previously saved reads before reusing them, continues missing slots and rechecks previously skipped empty slots. If that sample cannot confirm the previous reads, it starts a fresh scan. A previous complete index remains visible until a replacement is accepted; the first incomplete scan is explicitly marked partial.
5. Click a preset to show eight scenes in compact single-line rows. **+ preset tag / Edit preset tags** edits profile-local preset tags; **+ scene tag / Edit scene tags** edits the tags for that scene. Press Enter to add a tag, or type it and choose **Save tags**. Scene tags appear beside preset tags in the preset list as subtle **S1–S8** hints.
6. The selected-preset panel shows **Scenes & tags** and **Amp channels** together. The scene list uses two columns on wide windows and one on narrower windows, keeping the amp table alongside it where space permits. Amp channels uses a compact table: channels A–D down the rows and one column per present Amp block. Each scene row has separate read-only Amp columns showing the programmed channel and `off` for bypassed blocks; each cell's tooltip gives the model name. A divider separates these values from the scene tag controls. Nothing in this page changes the active preset or scene.
7. Search using text or tag chips. **All tags** means every selected tag appears on the preset or one of its scenes. **Any tag** matches at least one. Search and tag edits work offline.

The list uses the same empty-edge rule as Favorites → Select Preset: trim explicit `<EMPTY>` entries at the beginning and end, retain gaps inside that range, and retain unnamed presets. This changes the visible range without deleting cached slots or tags.

For FM9, sync first asks each slot for its current name. An exact `<EMPTY>` marker skips the full saved-preset dump. An unavailable/blank name, a failed name check or an ordinary name still triggers the full read. Every slot is checked; the first empty slot does not end the scan. Name-only entries are marked separately and have no invented content hash, Amp values or editable scenes. This follows the device's naming convention, not a separate protocol empty flag: a populated preset deliberately named `<EMPTY>` would also be skipped. FM3 and Axe-Fx III retain full reads until their name-check paths are validated.

## Profiles and device libraries

Use the **Assigned library** selector in Config, or **Manage library → Use for this profile**. A profile has one assignment, while scanned data can be shared by several profiles. Tags remain specific to the profile and library. Reassignment changes the index that this profile browses; it does not transfer tags, Favorites, cached names or MIDI mappings to different hardware. The assigned library's model must match the profile's device type.

**Manage library** follows the profile-management layout: select a library on the left, inspect its assigned profiles, saved-preset count and last scan on the right, then edit its name or firmware and choose **Save changes**. Creation is a separate action; saving again updates that same library. Creating or renaming to an existing name is rejected, ignoring case and surrounding spaces. Older duplicate names are retained and shown with distinct short references, allowing deliberate renaming or deletion without guessing which data to keep. Existing device models are read-only; create a different library for another model.

**Delete…** asks for confirmation, then removes the library's saved index and backup, historical references, preset/scene tags, review history and portable snapshots from all managed profiles. Libraries assigned to any profile cannot be deleted until those profiles use another library. Hardware presets are unaffected. There is no archive/restore workflow; libraries archived by the previous build appear in the normal list again. No libraries are automatically deleted. Existing external exports are unchanged.

Earlier files keep their last-selected library as the assignment. A single previously unselected library is adopted automatically; multiple unselected libraries require an explicit choice in Config. Previous library references, tags and review history are retained for recovery, copy and export, but are not additional active assignments. Reassigning the original library makes its unchanged tags available again. No device cache is deleted by reassignment.

The profile selector uses the existing profile-switch checks. Selecting a profile can query the connected device to check its model, editable name and cached preset/scene names; it does not automatically scan the full index. These checks are supporting evidence, not proof of a unique physical unit.

Profile rename, copy, delete, import and export retain the embedded index data. Copy creates independent tags with shared library links. Export includes decoded snapshots for offline use inside the existing settings/favorites ZIP pair. Import creates separate local device references; it cannot silently attach imported tags to similarly named hardware. Deleting a profile archives its tags with the settings file and leaves shared device caches intact.

## Checking the assigned library

**Config → Device library for this profile → Preset match threshold (%)** defaults to **99** and accepts whole percentages from **1–100**. It belongs to the profile, including copy/import/export; profiles sharing a library may use different thresholds. Existing profiles default to 99. Profile-management UX remains provisional.

Connecting runs a read-only check of up to 12 distinct populated preset fingerprints, distributed across the assigned library. **Check library** repeats it on demand, for example after changing the assignment or profile. The status explicitly says **Sample: 12 of 12 sampled presets match**, not that the whole library matched. At 99%, a 12-preset sample must match all 12. Failed or missing reads leave the check unconfirmed regardless of the threshold. Empty slots and duplicate fingerprints are excluded from sampling; there is currently no verified factory-preset database for preferentially selecting custom presets.

A full scan reports the actual fraction of matching populated slots against the previous complete baseline, including newly populated and cleared slots as differences. Matching compares exact saved-image SHA-256 hashes at the same slot; renames, moves, edits and firmware changes can reduce the percentage. Empty slots do not inflate the match. Resumed scans include previously checkpointed reads. Firmware comparisons use the entered library firmware, not an automatically detected version.

Model compatibility is required before reading. The device's editable name, when recorded in the baseline, is compared separately from the user-chosen **library name**. A changed/unavailable recorded name, a changed known firmware, an imported library, no populated evidence or a below-threshold full match requires confirmation before replacing the baseline. The observed device name is recorded only in the saved scan; connecting does not overwrite that baseline. Older libraries without a recorded device name can compare content and acquire the name on their next accepted sync.

Declining the update keeps the previous complete library and its tags. The completed candidate remains separately in `LastAttempt`; a later **Sync device** reads again. Cancelling or disconnecting before acceptance also leaves the previous baseline intact. Matching never chooses or reassigns another profile/library automatically. Identical backups can match on different physical devices: this is evidence of matching content, not a serial number or a probability of hardware identity.

## Changed presets and scenes

Exact unchanged saved content retains its tags. When saved bytes or their location change, existing tags are preserved under **Needs review** and excluded from the new occupant's search results. Choose the intended preset and destination numbers for tagged scenes, then explicitly attach the tags. Closing the review leaves them pending. **Undo last tag reassignment** reverses the latest decision without changing unrelated tags. If you subsequently edit the affected tags, undo is blocked to preserve those edits. This conservative first version also requires review for renames until a reliable name-independent fingerprint is implemented.

Amp grid coordinates are not indexed. An Amp is its block instance, channel models and scene settings. The complete saved-byte hash records a changed preset; it is not used as a claim of logical identity.

## Current limits

- The FM9 catalogue currently contains the ten model names observed at firmware 12.00. FM3 and Axe-Fx III catalogues start empty. Other models remain visible as **Unknown Amp model #…**, searchable by ID. Full catalogues and the separate Amps finder are the next milestone.
- FM9 saved reads have been exercised on hardware. FM3 and Axe-Fx III have distinct candidate decoders and synthetic tests, but have not been tested on attached hardware.
- Firmware is entered explicitly; the app has no verified MIDI firmware-version query. Saved scenes describe programmed state, not Scene Ignore's effective live behavior.
- A fresh sync checks every slot; FM9 explicit empty markers avoid full transfers. It can take substantially longer over DIN MIDI than USB; there is no validated cheap remote checksum query.
- The existing quick **Sync Preset Names**, Favorites, and live scene tracker retain their established behavior. Full index reads share their MIDI request gate and block profile changes during the scan.

## Storage and build

Device cache: `%APPDATA%\PresetMaestro\FractalIndex\<device-id>.json`, with `.bak` recovery files.

Tags and device links: the versioned `FractalIndex` object in `<profile>-settings.json`. No database installation or schema migration is required.

Build without the feature:

```powershell
dotnet build PresetMaestro/PresetMaestro.csproj -p:EnableFractalIndex=false
```

This removes the Index page, Config card and scanner initialization. Existing JSON and shared cache files are preserved for re-enabling the feature. Export while disabled preserves the opaque profile data, but does not refresh its portable snapshots.
