# Using Preset Index

1. Select your profile using the **Profile** control in the application header. Open **Config → Device library for this profile**. Choose its **Assigned library**, or open **Manage library**. On that screen, choose **+ Create…**, enter a unique name and, if disconnected, a device model/revision, then **Create library**. Choose **Use for this profile** to assign the new library. Each profile has one assigned library; several profiles can share one library.
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

**Manage library** follows the profile-management layout: select a library on the left, inspect its assigned profiles, saved-preset count and last scan on the right, then edit its name and choose **Save changes**. Creation is a separate action; saving again updates that same library. Creating or renaming to an existing name is rejected, ignoring case and surrounding spaces. Older duplicate names are retained and shown with distinct short references, allowing deliberate renaming or deletion without guessing which data to keep. Model and firmware are stored in the library and displayed read-only. When connected, the model family and firmware come from the device; Axe-Fx III still requires a revision choice because its identity reply does not establish slot capacity. Create a different library for another model.

**Delete…** asks for confirmation, then removes the library's saved index and backup, historical references, preset/scene tags, review history and portable snapshots from all managed profiles. Libraries assigned to any profile cannot be deleted until those profiles use another library. Hardware presets are unaffected. There is no archive/restore workflow; libraries archived by the previous build appear in the normal list again. No libraries are automatically deleted. Existing external exports are unchanged.

Earlier files keep their last-selected library as the assignment. A single previously unselected library is adopted automatically; multiple unselected libraries require an explicit choice in Config. Previous library references, tags and review history are retained for recovery, copy and export, but are not additional active assignments. Reassigning the original library makes its unchanged tags available again. No device cache is deleted by reassignment.

The profile selector uses the existing profile-switch checks. Selecting a profile can query the connected device to check its model, editable name and cached preset/scene names; it does not automatically scan the full index. These checks are supporting evidence, not proof of a unique physical unit.

Profile rename, copy, delete, import and export retain the embedded index data. Copy creates independent tags with shared library links. Export includes decoded snapshots for offline use inside the existing settings/favorites ZIP pair. Import creates separate local device references; it cannot silently attach imported tags to similarly named hardware. Deleting a profile archives its tags with the settings file and leaves shared device caches intact.

## Checking the assigned library

**Config → Device library for this profile → Preset match threshold (%)** defaults to **99** and accepts whole percentages from **1–100**. It belongs to the profile, including copy/import/export; profiles sharing a library may use different thresholds. Existing profiles default to 99. Profile-management UX remains provisional.

Connecting explains that the library is PresetMaestro's saved copy of presets, scenes and amp settings. Two side-by-side choices compare that copy with the connected device: **Start quick check** (recommended) checks up to 16 random presets and all their saved scenes; **Start full check** checks every slot and all saved scenes and may take several minutes. **Sync library** refreshes the saved copy, while **Skip for now** continues using saved data.

On FM9, both choices first read all 512 preset names using the same fast request path as Config → Preset Name Sync. Names are compared at the same slots using the profile's match threshold; newly populated, renamed and cleared slots count as differences, while empty slots do not inflate the percentage. An incomplete read or below-threshold name match stops the longer preset-and-scene check. Matching names alone cannot establish a content match: the selected quick/full check must still pass. After it passes, the freshly read name list is saved automatically in the active profile. Failed or cancelled checks keep the previous cached names. FM3 and Axe-Fx III continue using their supported saved-preset paths until their fast name-query paths are validated.

Quick match selects 16 random populated slots without replacement, or all populated slots if fewer than 16 are saved. Each selected preset's full saved content, including all eight scenes, is compared at the same slot. The result explicitly reports the sample count, for example **16 of 16 sampled presets match**. At 99%, a 16-preset sample must match all 16. Empty slots are excluded from sampling. **Check library** on Preset Index repeats a quick check; changing the assigned library also runs a quick check.

Full match checks every device slot, including previously empty slots so newly added presets can be detected. Neither check replaces the library's saved baseline or refreshes its preset data; successful FM9 checks additionally refresh the profile's preset-name list. Failed or missing reads leave the match unconfirmed regardless of the threshold. Three consecutive failed reads stop a check. Both options show checked counts and can be cancelled. Full match can take several minutes.

In Config, the Preset Name Sync card shows **Connecting…** while device information is read. After connecting, preset-name sync is ready immediately. If you choose either library match, the card shows **Checking library…** until that check finishes or is cancelled. **Cancel** stops the check. Cached names remain usable; refreshing them is optional on each connection.

After a successful connection, **Sync with connected device** opens automatically.
It identifies the device, active profile and assigned library, then shows the quick/full
check choices. After a check, the large choice cards disappear; **Check library again**
can bring them back. The result says what was compared and what was refreshed.
**Use saved library** is the main action after a successful check or accepted full sync.
The refresh actions and management buttons are visible without an accordion:

- **Sync library** refreshes Preset Index, Amps, and the active profile's preset and scene names. Use it after editing device presets or to establish the first library. An accepted complete scan supplies all these names without extra MIDI queries; partial or declined scans do not replace profile name caches. Other profiles sharing the library retain their own caches. Live edit-buffer scene labels remain intact.
- **Sync names** refreshes only the profile's preset list used by the Favorites picker. Use it after renaming or moving presets when a full library refresh is unnecessary; it is automatic after a successful FM9 library check.
- **Sync scenes** refreshes scene names for this profile's favorite presets. Use it after renaming scenes when you do not need a full library sync.
- **Resume** continues an incomplete library scan. **Manage profiles** and **Manage libraries** open the existing management screens.

Options without the required data or supported device path are
disabled. A prominent panel below the connection details shows the active operation
and progress. It turns green on success or amber when a read is cancelled, fails,
or leaves the library match unconfirmed. The result remains visible with a next
step in a separate neutral **Next step** panel below the result, including whether to retry, review the library assignment, refresh data, or
continue. A successful sample check reports how many sampled presets matched,
whether all preset names were refreshed, and that library data was not refreshed. Preset-name and favorite
scene-name results report the completed reads. Reopening the dialog retains the
last result for the current connection, profile and library.
Device information is read on connection. Manage profiles and Manage
libraries open the existing management screens. **Skip for now** continues without a sync;
**Config → Sync options…** reopens the dialog. Library scans keep progress in the dialog
and use the existing confirmation flow. Favorite scene reads are deduplicated
by preset and save completed results in the profile without selecting presets.

The dialog follows Microsoft Fluent's guidance to keep task-critical information visible ([accordions](https://fluent2.microsoft.design/components/web/react/core/accordion/usage)) and make completion feedback specific and actionable ([message bars](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage)). A quick check does not automatically start a minutes-long library scan.

A full scan reports the actual fraction of matching populated slots against the previous complete baseline, including newly populated and cleared slots as differences. Matching compares exact saved-image SHA-256 hashes at the same slot; renames, moves, edits and firmware changes can reduce the percentage. Empty slots do not inflate the match. Resumed scans include previously checkpointed reads. Firmware comparisons and new scans use the version reported by the current connection, never an assumed saved version. Each scan retains its original firmware.

Model compatibility is required before reading. The device's editable name, when recorded in the baseline, is compared separately from the user-chosen **library name**. A changed/unavailable recorded name, a changed or unavailable firmware, an imported library, no populated evidence or a below-threshold full match requires confirmation before replacing the baseline. The observed device name is recorded only in the saved scan; connecting does not overwrite that baseline. Older libraries without a recorded device name can compare content and acquire the name on their next accepted sync.

Declining the update keeps the previous complete library and its tags. The completed candidate remains separately in `LastAttempt`; a later **Sync device** reads again. Cancelling or disconnecting before acceptance also leaves the previous baseline intact. Matching never chooses or reassigns another profile/library automatically. Identical backups can match on different physical devices: this is evidence of matching content, not a serial number or a probability of hardware identity.

## Changed presets and scenes

Exact unchanged saved content retains its tags. When saved bytes or their location change, existing tags are preserved under **Needs review** and excluded from the new occupant's search results. Choose the intended preset and destination numbers for tagged scenes, then explicitly attach the tags. Closing the review leaves them pending. **Undo last tag reassignment** reverses the latest decision without changing unrelated tags. If you subsequently edit the affected tags, undo is blocked to preserve those edits. This conservative first version also requires review for renames until a reliable name-independent fingerprint is implemented.

Amp grid coordinates are not indexed. An Amp is its block instance, channel models and scene settings. The complete saved-byte hash records a changed preset; it is not used as a claim of logical identity.

## Amp Browser

Open **Amps** to browse the assigned library's catalogue by manufacturer and real
amp family. Search also matches Fractal model names and aliases. The usage selector
offers **All amps**, **Used in my presets**, and **Not used in my presets**. All amps
is the default. Not used lists families whose variants do not appear in any saved
amp channel, including channels no scene selects. It requires a complete library
scan for the current device software and an available model catalogue. A partial
scan can show known usage but cannot prove an amp is unused; a device with only
observed model identities cannot supply unused catalogue models. Search and
manufacturer filters work with each option. Open a
family, choose all its variants or one exact Fractal model, then select **Find
presets containing this amp**. Preset Index shows an explicit Contains filter,
matching block/channel names, and optional saved-scene selection/engagement
filters. **Back to Amps** restores your browsing position; **Clear amp filter**
is independent of **Clear search**. Browsing does not send MIDI commands.

Family details include mapping confidence, expandable source evidence, and
Wiki/References actions. **Add Wiki link…** saves personal reference links shared
across profiles; relevant links accompany profile exports.

See [implementation and catalogue evidence](amp-browser-implementation.md).

## Current limits

- Amps ships 284 candidate FM9 12.00 roster entries plus the observed ID 271 correction. Only ten names have device evidence. Coverage is explicitly **incomplete**, and real-amp mappings are marked probable where based on historical names. FM3, Axe-Fx III and other firmware retain their own observed identities without FM9 fallback. Complete firmware rosters still need validation.
- FM9 saved reads have been exercised on hardware. FM3 and Axe-Fx III have distinct candidate decoders and synthetic tests, but have not been tested on attached hardware.
- Firmware discovery uses the source-verified Gen-3 query; local hardware verification remains outstanding. If the version cannot be read it stays unknown, with no manual override. Saved scenes describe programmed state, not Scene Ignore's effective live behavior.
- A fresh sync checks every slot; FM9 explicit empty markers avoid full transfers. It can take substantially longer over DIN MIDI than USB; there is no validated cheap remote checksum query.
- The existing quick **Sync Preset Names**, Favorites, and live scene tracker retain their established behavior. Full index reads share their MIDI request gate and block profile changes during the scan.

After a successful check in the dialog opened on initial connection, the dialog closes and **Preset Index** opens automatically. A compact, dismissible summary shows the check result and what was refreshed. An accepted complete **Sync library** takes the same route. Optional refreshes do not hold up browsing. If reads are incomplete, the library differs, saving fails or device information is still needed, the dialog stays open with a visible **Next step**. Sync options opened manually from Config remain open after completion.

## Storage and build

Older libraries that did not save the device's software version are repaired automatically after an accepted Quick or Full check. This makes saved amp usage and **Find presets** available immediately and when reopening offline; no full library sync is needed for this repair. The original preset data, scan dates and tags are preserved. Separate confirmation metadata records the detected version, check date, sample scope, match counts and threshold. Failed, cancelled or unconfirmed checks do not add that confirmation. A later software-version change still requires fresh library data.

Device cache: `%APPDATA%\PresetMaestro\FractalIndex\<device-id>.json`, with `.bak` recovery files.

Tags and device links: the versioned `FractalIndex` object in `<profile>-settings.json`. No database installation or schema migration is required.

Build without the feature:

```powershell
dotnet build PresetMaestro/PresetMaestro.csproj -p:EnableFractalIndex=false
```

This removes the Index page, Config card and scanner initialization. Existing JSON and shared cache files are preserved for re-enabling the feature. Export while disabled preserves the opaque profile data, but does not refresh its portable snapshots.
