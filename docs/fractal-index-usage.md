# Using Preset Index

1. Select your profile using the **Profile** control in the application header. Open **Config → Device library for this profile**. Choose its **Assigned library**, or open **Manage library**. On that screen, choose **+ Create…**, enter a unique name and, if disconnected, a device model/revision, then **Create library**. Choose **Use for this profile** to assign the new library. Each profile has one assigned library; several profiles can share one library.
2. Connect MIDI using the existing **MIDI Connection** controls. On a new installation, connecting also creates and assigns an unused **Default** library, so manual library creation is optional. See **First connection and required library setup** below.
3. Open **Preset Index**. The header shows the assigned **Library** as information beside the **Profile** selector. Switching profiles restores that profile's library and tags. **Sync device** reads saved presets from the connected matching model. The first sync asks you to establish the library's baseline. Subsequent complete scans compare against that baseline before replacing it; a low or uncertain match requires confirmation.
4. The sync notice explains that reading can take several minutes or longer over slower MIDI connections. A prominent bar shows the percentage, checked slots, downloaded presets, empty slots skipped and failures together. **Cancel** retains completed reads. **Resume** checks a sample of previously saved reads before reusing them, continues missing slots and rechecks previously skipped empty slots. If that sample cannot confirm the previous reads, it starts a fresh scan. A previous complete index remains visible until a replacement is accepted; the first incomplete scan is explicitly marked partial.
5. Click a preset to open its details in a separate scrollable pane. The preset list keeps its full height. **+ preset tag / Edit preset tags** edits profile-local preset tags; each scene's **Tags…** button edits that scene's tags. Press Enter to add a tag, or type it and choose **Save tags**. Scene tags appear beside preset tags in the preset list as **S1–S8** hints.
6. Details show eight readable **Scenes**, with two columns on wide panes and one on narrower panes. Each row shows the Fractal amp model selected by that scene and its saved channel, for example **Plexi 50W Jumped · channel A**. Two Amp blocks appear on separate lines, and bypassed blocks are marked **off**. Rows grow to fit long names. **Amp models ↓** jumps to the channel A–D table below; it shows the real amp reference, when mapped, above the exact Fractal model name. **Back to scenes ↑**, beside Amp models in the fixed header, returns to the top and stays visible while scrolling. These are saved settings, which can differ from unsaved device edits.
7. Search using text or tag chips. **All tags** means every selected tag appears on the preset or one of its scenes. **Any tag** matches at least one. Search and tag edits work offline.

The list uses the same empty-edge rule as Favorites → Select Preset: trim explicit `<EMPTY>` entries at the beginning and end, retain gaps inside that range, and retain unnamed presets. This changes the visible range without deleting cached slots or tags.

When a completed sync needs confirmation, its modal **Confirm library update** window stays above **Sync with connected device**. It shows how many presets do not match. **View differences** opens the preset names and available before/after settings; **Back** returns to the decision. **Update library** replaces the saved copy with the newly read device data. **Cancel** keeps the saved copy. Reviewing does not change either copy or read the device again. When the library stores only a fingerprint for a changed setting, the review states that the exact change is unavailable.

**× Close details** is at the top-right of the details header and stays visible while scenes and amp models scroll. You can also press **Escape** while focus is inside the pane. Closing returns focus to the preset list and keeps your search and filters.

Single-click selects a preset or scene. **Go to preset**, a scene's **Go to** button, the right-click menu, or double-click loads that selection on the connected Fractal. Pressing Enter on a focused scene row also loads that scene. A scene action loads its preset first if needed; when that preset is already active, only its scene is changed. Go to uses the library's MIDI channel, display offset and Scene CC. It is unavailable offline, while reads/profile changes are running, during required first-connection setup, or when the library and connected device models differ. Browsing, filtering and editing tags send no program or scene change.

The Favorites preset and scene pickers also have **Go to** buttons and double-click audition. They stay open while auditioning. **Select Preset / Select Scene** applies the choice to the favorite's draft; saving the favorite remains a separate action. Tag review provides Go to actions for its selected destination preset and scenes. Favorites also offers **Go to preset & scene** in its right-click menu.

For FM9, sync first asks each slot for its current name. An exact `<EMPTY>` marker skips the full saved-preset dump. An unavailable/blank name, a failed name check or an ordinary name still triggers the full read. Every slot is checked; the first empty slot does not end the scan. Name-only entries are marked separately and have no invented content hash, Amp values or editable scenes. This follows the device's naming convention, not a separate protocol empty flag: a populated preset deliberately named `<EMPTY>` would also be skipped. FM3 and Axe-Fx III retain full reads until their name-check paths are validated.

## Profiles and device libraries

Use the **Assigned library** selector in Config, or **Manage library → Use for this profile**. A profile has one assignment, while scanned data can be shared by several profiles. Tags remain specific to the profile and library. Reassignment changes the index that this profile browses; it adopts that library's preset mapping while retaining the profile's tags, Favorites and cached names. The assigned library's model must match the profile's device type.

**Manage library** follows the profile-management layout: select a library on the left, inspect its assigned profiles, saved-preset count and last scan on the right, then edit its name or **Preset Mapping** and choose **Save changes**. MIDI channel, display offset and Scene CC belong to this library and are shared by all assigned profiles. Selecting or editing a different library leaves the active profile's mapping unchanged until that library is assigned. Incoming MIDI note assignments remain global under **Config → Entry Options → MIDI note mapping**. Creation is a separate action; saving again updates that same library. Creating or renaming to an existing name is rejected, ignoring case and surrounding spaces. Older duplicate names are retained and shown with distinct short references, allowing deliberate renaming or deletion without guessing which data to keep. Model and firmware are stored in the library and displayed read-only. When connected, the model family and firmware come from the device; Axe-Fx III still requires a revision choice because its identity reply does not establish slot capacity. Create a different library for another model.

**Delete…** asks for confirmation, then removes the library's saved index and backup, historical references, preset/scene tags, review history and portable snapshots from all managed profiles. Libraries assigned to any profile cannot be deleted until those profiles use another library. Hardware presets are unaffected. There is no archive/restore workflow; libraries archived by the previous build appear in the normal list again. No libraries are automatically deleted. Existing external exports are unchanged.

Earlier files keep their last-selected library as the assignment. A single previously unselected library is adopted automatically; multiple unselected libraries require an explicit choice in Config. Previous library references, tags and review history are retained for recovery, copy and export, but are not additional active assignments. Reassigning the original library makes its unchanged tags available again. No device cache is deleted by reassignment.

The profile selector uses the existing profile-switch checks. Selecting a profile can query the connected device to check its model, editable name and cached preset/scene names; it does not automatically scan the full index. These checks are supporting evidence, not proof of a unique physical unit.

Profile rename, copy, delete, import and export retain the embedded index data. Copy creates independent tags with shared library links. Export includes decoded snapshots for offline use inside the existing settings/favorites ZIP pair. Import creates separate local device references; it cannot silently attach imported tags to similarly named hardware. Deleting a profile archives its tags with the settings file and leaves shared device caches intact.

## Checking the assigned library

Library checks have no adjustable tolerance. A check is confirmed only when every compared preset matches; any difference is listed for review and the user decides what to do. The former **Preset match threshold (%)** setting was removed on 2026-10-06; a value saved by an earlier version is ignored and dropped the next time the profile is saved. Smarter recognition of renames, moves and swaps is planned in [sync-change-detection-plan.md](sync-change-detection-plan.md). Profile-management UX remains provisional.

When a complete local library is assigned for the connected model, connecting explains that the library is PresetMaestro's saved copy of presets, scenes and amp settings. Two side-by-side choices compare that copy with the connected device: **Start quick check** (recommended) checks up to 16 random presets and all their saved scenes; **Start full check** checks every slot and all saved scenes and may take several minutes. **Sync library** refreshes the saved copy, while **Skip for now** continues using saved data.

### First connection and required library setup

If the profile has no assigned library, or its library is imported, incomplete or for another model, the connection dialog requires a complete library sync. Cached preset names do not establish a device library. Names-only and favorite-scene refreshes, library checks and profile/library management are unavailable until setup succeeds.

An unused **Default** library is created and assigned automatically. An empty Default placeholder can be reused when its model/revision matches and no other profile uses it. Any saved scan, including a partial attempt, counts as existing data. A Default assigned to another profile also requires an explicit choice. Choose **Create new library & sync**, or **Overwrite Default & sync** and confirm the affected profiles. Axe-Fx III requires a hardware revision choice so the scan uses the correct slot capacity.

Overwrite scans are checkpointed separately under `FractalIndex/ConnectionSetup`; they do not modify the occupied library during reads. Only a complete, error-free scan replaces it and updates its profile references. Existing tags are retained for review when content changes. Cancellation, a failed read or declined confirmation preserves the original library. **Resume** can continue an incomplete read during setup. Closing the dialog or choosing **Disconnect** closes the MIDI connection instead of bypassing setup. Successful setup refreshes the profile's preset and scene names and opens Preset Index automatically.

On FM9, both choices first read all 512 preset names using the same fast request path as **Sync names** in the sync dialog. Names are compared at the same slots; newly populated, renamed and cleared slots count as differences, while empty slots are ignored. Name differences never stop the preset-and-scene check: it always runs, and any name differences outside the compared slots are listed with its result and prevent confirmation. Only an incomplete name read stops the check. Matching names alone cannot establish a content match: the selected quick/full check must still pass. After it passes, the freshly read name list is saved automatically in the active profile. Failed or cancelled checks keep the previous cached names. FM3 and Axe-Fx III continue using their supported saved-preset paths until their fast name-query paths are validated.

Quick match selects 16 random populated slots without replacement, or all populated slots if fewer than 16 are saved. Each selected preset's full saved content, including all eight scenes, is compared at the same slot. The result explicitly reports the sample count, for example **16 of 16 sampled presets match**. Every sampled preset must match. Empty slots are excluded from sampling. **Check library** on Preset Index repeats a quick check; changing the assigned library also runs a quick check.

Checks compare with the saved library's scan date, not the previous connection.
A successful check does not refresh that baseline. Because each quick check chooses
a fresh sample, it can find a difference an earlier sample missed. **What differs**
lists affected preset numbers and names, before/after values for decoded names,
amp models and scene channel selections, device/firmware differences, and failed
reads. It states when other parameters cannot be compared individually. The last
completed check's evidence is retained separately from the library.

New library scans also store comparison data that excludes saved effect bypass
states. Bypass-only changes then do not count as differences; scene channel
selections, models and other saved settings still count. Older libraries have only
the original image fingerprint, which includes bypass. A review explains this
limitation. After reviewing differences, **Sync library** once to establish the
new comparison data; a check never silently replaces the existing baseline.

Full match checks every device slot, including previously empty slots so newly added presets can be detected. Neither check replaces the library's saved baseline or refreshes its preset data; successful FM9 checks additionally refresh the profile's preset-name list. Failed or missing reads leave the match unconfirmed. Three consecutive failed reads stop a check. Both options show checked counts and can be cancelled. Full match can take several minutes.

In **Config → MIDI Connection**, **Sync options…** opens the shared sync dialog after device information has been read. During a read, the button becomes **Sync progress…** and can reopen the dialog to check progress or cancel. Progress is shown only while a read is active; completed, cancelled and failed reads leave their result and next step visible. After required library setup, refreshing cached names is optional on each connection. The Favorites editor's **↻ Sync** shortcut starts a names-only refresh in the same dialog.

After a successful connection, **Sync with connected device** opens automatically.
It identifies the device, active profile and assigned library, then shows required setup or the quick/full
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
libraries open the existing management screens after required setup. **Skip for now** continues without a sync when a complete local library is already assigned;
**Config → MIDI Connection → Sync options…** reopens the dialog. Library scans keep progress in the dialog
and use the existing confirmation flow. Favorite scene reads are deduplicated
by preset and save completed results in the profile without selecting presets.

The dialog follows Microsoft Fluent's guidance to keep task-critical information visible ([accordions](https://fluent2.microsoft.design/components/web/react/core/accordion/usage)) and make completion feedback specific and actionable ([message bars](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage)). A quick check does not automatically start a minutes-long library scan.

A full scan reports how many populated slots match the previous complete baseline, for example **99 of 100 populated presets match**, including newly populated and cleared slots as differences. Matching compares saved-image fingerprints at the same slot; renames, moves, edits and firmware changes currently appear as differences. Empty slots are not counted. Resumed scans include previously checkpointed reads. Firmware comparisons and new scans use the version reported by the current connection, never an assumed saved version. Each scan retains its original firmware.

Model compatibility is required before reading. The device's editable name, when recorded in the baseline, is compared separately from the user-chosen **library name**. A changed/unavailable recorded name, a changed or unavailable firmware, an imported library, no populated evidence or any differing preset requires confirmation before replacing the baseline. The observed device name is recorded only in the saved scan; connecting does not overwrite that baseline. Older libraries without a recorded device name can compare content and acquire the name on their next accepted sync.

Declining the update keeps the previous complete library and its tags. The completed candidate remains separately in `LastAttempt`; a later **Sync device** reads again. Cancelling or disconnecting before acceptance also leaves the previous baseline intact. Matching never chooses or reassigns another profile/library automatically. Identical backups can match on different physical devices: this is evidence of matching content, not a serial number or a probability of hardware identity.

## Changed presets and scenes

Exact unchanged saved content retains its tags. When saved bytes or their location change, existing tags are preserved under **Needs review** and excluded from the new occupant's search results. Choose the intended preset and destination numbers for tagged scenes, then explicitly attach the tags. Closing the review leaves them pending. **Undo last tag reassignment** reverses the latest decision without changing unrelated tags. If you subsequently edit the affected tags, undo is blocked to preserve those edits. This conservative first version also requires review for renames until a reliable name-independent fingerprint is implemented.

Amp grid coordinates are not indexed. An Amp is its block instance, channel models and scene settings. The complete saved-byte hash records a changed preset; it is not used as a claim of logical identity.

## Amp Browser

Open **Amps** to browse the assigned library's catalogue by manufacturer and real
amp family. Search also matches Fractal model names and aliases. Each directory
entry shows the real amp family first, with smaller Fractal model names beneath it.
In family details, each selectable variant pairs its specific
real amp reference with the exact **Fractal:** model name to choose on the device.
Revision/channel details and qualifications such as **inferred** remain visible.
The usage selector
offers **All amps**, **Used in my presets**, and **Not used in my presets**. All amps
is the default. Not used lists families whose variants do not appear in any saved
amp channel, including channels no scene selects. It requires a complete library
scan for the current device software and an available model catalogue. A partial
scan can show known usage but cannot prove an amp is unused; a device with only
observed model identities cannot supply unused catalogue models. Search and
manufacturer filters work with each option. Open a
family, choose all its variants or one exact Fractal model, then select **Find
presets containing this amp**. The selected amp view shows its name and matching
presets, with compact summaries such as **Amp 1 · channels A–D**. Select a preset
for its scenes and exact amp details. **Back to Amps** restores your browsing
position, search and selected variants. Search and matching options are not
repeated in this view. Open **Preset Index** directly for general preset/tag search;
its previous search is preserved separately. Browsing does not send MIDI commands.

Family details include a small Wiki heading beneath the preset count, with
links to the relevant Amp models sections. Detailed catalogue notes and source
evidence remain preserved for a future Config view. Existing personal reference
links remain available and accompany profile exports. The pane has no control
for adding links.

See [implementation and catalogue evidence](amp-browser-implementation.md).

## Current limits

- Amps ships 284 candidate FM9 12.00 roster entries plus the observed ID 271 correction. Only ten names have device evidence. Coverage is explicitly **incomplete**, and real-amp mappings are marked probable where based on historical names. FM3, Axe-Fx III and other firmware retain their own observed identities without FM9 fallback. Complete firmware rosters still need validation.
- FM9 saved reads have been exercised on hardware. FM3 and Axe-Fx III have distinct candidate decoders and synthetic tests, but have not been tested on attached hardware.
- Firmware discovery uses the source-verified Gen-3 query; local hardware verification remains outstanding. If the version cannot be read it stays unknown, with no manual override. Saved scenes describe programmed state, not Scene Ignore's effective live behavior.
- A fresh sync checks every slot; FM9 explicit empty markers avoid full transfers. It can take substantially longer over DIN MIDI than USB; there is no validated cheap remote checksum query.
- The quick names-only refresh remains available through **Sync names** and the Favorites editor's **↻ Sync** shortcut. Full index reads share their MIDI request gate and block profile changes during the scan.

After a successful check in the dialog opened on initial connection, the dialog closes and **Preset Index** opens automatically. A compact, dismissible summary shows the check result and what was refreshed. An accepted complete **Sync library** takes the same route. Optional refreshes do not hold up browsing. If reads are incomplete, the library differs, saving fails or device information is still needed, the dialog stays open with a visible **Next step**. Sync options opened manually from Config remain open after completion.

## Storage and build

Older libraries that did not save the device's software version are repaired automatically after an accepted Quick or Full check. This makes saved amp usage and **Find presets** available immediately and when reopening offline; no full library sync is needed for this repair. The original preset data, scan dates and tags are preserved. Separate confirmation metadata records the detected version, check date, sample scope and match counts (older confirmations may also contain the retired threshold value, which is ignored). Failed, cancelled or unconfirmed checks do not add that confirmation. A later software-version change still requires fresh library data.

Device cache: `%APPDATA%\PresetMaestro\FractalIndex\<device-id>.json`, with `.bak` recovery files.

Tags and device links: the versioned `FractalIndex` object in `<profile>-settings.json`. No database installation or schema migration is required.

Build without the feature:

```powershell
dotnet build PresetMaestro/PresetMaestro.csproj -p:EnableFractalIndex=false
```

This removes the Index page, Config card and scanner initialization. Existing JSON and shared cache files are preserved for re-enabling the feature. Export while disabled preserves the opaque profile data, but does not refresh its portable snapshots.
