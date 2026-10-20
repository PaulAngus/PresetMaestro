# FM9 active preset and name-change investigation

## Scope and evidence

Observed on 20 October 2026 using FM9-Edit on a Windows client through MIDI Forwarder to an FM9. These are captured device transactions, not assumptions from the public third-party protocol. The device's firmware version was not independently established during this capture. Other models and firmware remain unverified.

The capture recorded both directions at the Windows server boundary, preserving and reconstructing UMP SysEx7. Raw messages and personal preset names stay in ignored `artifacts/fm9-investigation/` in the MIDI-Forwarder checkout and the user's MIDI Forwarder capture folder. They are not included in this document.

| Recording | Duration | Relay packets | Complete SysEx messages | Capture loss |
| --- | --- | --- | --- | --- |
| Idle baseline | 65.041 s | 61,578 | 4,334 | 0 |
| Unsaved editor rename | 192.973 s | 226,836 | 20,225 | 0 |
| Editor save | 71.253 s | 94,714 | 9,326 | 0 |
| Attempted inactive rename in Manage Presets | 35.318 s of traffic | 25,860 | 1,557 | 0 |
| Scene swap followed by editor save | 96.287 s | 124,486 | 12,241 | 0 |

All reconstructed SysEx checksums passed. There were no capture sequence gaps or unfinished SysEx messages at stop. The unsaved rename and scene-swap traces each begin with one response to a request preceding the recording boundary; subsequent responses matched requests. Every response in the save trace matched an editor request.

## Recurring current-state transaction

FM9-Edit sends this same 23-byte request approximately every 0.75 seconds:

```text
F0 00 01 74 12 01 2E 00 00 00 00 00 00 00 00 00 00 00 00 00 00 38 F7
```

The response is 755 bytes, with function `01` and subaction `2E`. Header bytes 19-20 specify a 640-byte payload in two low-first septets. Starting at byte 21, 732 MSB-first septets decode to those 640 bytes. Validate frame boundaries, manufacturer/model, function/subaction, length, septets and checksum before decoding.

Observed fields in the decoded payload:

| Offset | Width | Observation and current confidence |
| --- | --- | --- |
| 0 | 4 | Little-endian 253 matched the user's displayed preset 254. Another selected preset produced 169. Consistent with the current zero-based slot; confirm boundaries and display-offset independence. |
| 4 | 4 | Little-endian 0 before editing, 1 after renaming, 0 after saving. Strong candidate for the current preset's edited flag. Discard and hardware-edit behavior still need testing. |
| 8 | 4 | Stayed 0 while the user remained on scene 1. Candidate scene index; not yet validated by scene switching. |
| 16 | 32 | Current preset name, with space padding in these examples. |
| 48 + 32*n | 32 each | Eight current scene-name fields, including blank scenes; n = 0..7. |

The status reply contained an unsaved scene rename before the preset was saved. It also tracked a temporary preset-name edit and its restoration. The first changed scene-name reply arrived 0.441 seconds after the editor's scene-name write. This demonstrates a live/edit-buffer view of the active preset; it must not be treated as a saved library image.

The baseline also contained sixteen other recurring requests approximately every 0.5 seconds. These were repeated fixed request patterns, not a sequential saved-preset library scan. Additional requests appeared as the editor's working view changed; their full meanings have not been established.

## Rename and save transactions

Observed editor writes used function `01`:

- Subaction `28`: preset-name write, 60-byte request with a packed 32-byte name; followed by a 23-byte response.
- Subaction `2B`: scene-name write, 60-byte request with a packed 32-byte name; followed by a 23-byte response. Only scene 1 was renamed, so other-scene addressing remains unverified.
- Subaction `26`: save request, 23 bytes. The observed destination encoding included low/high septets `7D 01`, matching slot 253. It was followed by a device reply, then regular current-state replies showed the edited field cleared.

The first observed current-state reply with edited = 0 arrived about 0.686 seconds after the save request. The renamed scene name remained unchanged, and slot/scene candidate fields stayed at 253/0. This is consistent with successful saving, as independently reported by the user. No separate unsolicited save announcement was observed in this recording.

## Attempted inactive rename in Manage Presets

The intended test left displayed preset 254 loaded and renamed displayed preset 251 through FM9-Edit's Manage Presets. The user restarted capture before the action. Afterward, the user confirmed Manage Presets was still open and **251 was now loaded**. This recording therefore does **not** establish what happens when a renamed slot remains inactive throughout.

The capture contained these transactions, with timestamps in UTC:

| Time | Observed transaction |
| --- | --- |
| 11:49:40.485–11:49:54.020 | Nineteen current-state replies, all reporting slot 253, edited candidate 0, scene candidate 0, and the previously saved active names. |
| 11:50:04.804 | Editor requests a saved dump of zero-based slot 250 (`03`). The FM9 returns `77`/eight `78` frames/`79`. |
| 11:50:14.901 | Editor sends private subaction `27` addressed to slot 250, consistent with the preset-load action observed earlier and the user's final loaded-preset report. |
| 11:50:15.173–11:50:15.447 | Editor uploads the same complete preset image to buffer address `7F 7F`; the device acknowledges the header, eight chunks and footer with `64` frames. |
| 11:50:15.451 | Editor writes the new preset name with subaction `28`, addressed to slot 250. |
| 11:50:15.460–11:50:15.797 | Editor saves to slot 250 with subaction `26`; the FM9 replies. |
| 11:50:15.799–11:50:15.800 | Editor queries the name using subaction `2A`, addressed to slot 250. The reply contains the new name. |

The downloaded and uploaded images were byte-identical, each 16,384 bytes; image CRCs, dump-footer checksums and every SysEx frame checksum passed. All function `01` replies matched editor requests, with none pending at the end. The other replies were the requested dump and upload acknowledgements. No additional unsolicited message was observed in this interval.

Crucially, ordinary current-state polling stopped **before** the saved read and rename. There were no current-state replies after the operation, and no MIDI traffic after the final name reply before capture stopped at 11:51:14.091. Consequently, neither the active-state payload after renaming nor another recurring status payload's reaction to the rename can be assessed from this trace. The last pre-operation status must not be presented as evidence that preset 254 stayed loaded.

Fractal's [official Manage Presets guidance](https://support.fractalaudio.com/en-US/installing-factory-presets-1410465), checked on 20 October 2026, explains that changes can be previewed and committed with Save. It does not establish a way to commit this rename while preserving a different loaded preset. Do not prescribe another such procedure without verifying its behavior.

This confirms an editor-driven read/load/upload/rename/save/readback workflow and a successful new-name readback. It provides no new proof of a library-wide change notification and does not answer inactive-only rename detection. It also does not prove that every Manage Presets rename follows this exact workflow.

## Scene swap followed by save

The user was asked to swap scenes 1 and 2 of displayed preset 254 in FM9-Edit and subsequently reported saving it. The trace captured both the unsaved and saved phases, so a separate unsaved recording was not necessary for this observation. It is still an editor action, not a front-panel swap test.

| UTC time | Observation |
| --- | --- |
| 12:04:13.871 | Current-state reply reports slot 253, edited candidate 0, scene candidate 0, and the previous scene-name order. |
| 12:04:21.135 | Editor sends a 26-byte function `01`, subaction `15` request. This coincides with the requested swap; its argument encoding is not yet established for arbitrary scene pairs. |
| 12:04:21.523 | Current-state reply has scene-name fields 1 and 2 exchanged, edited candidate 1. The other six names, active slot and scene candidate are unchanged. |
| 12:04:25.184 | Editor sends subaction `26` Save addressed to slot 253. |
| 12:04:25.654 | FM9 replies to Save. |
| 12:04:25.757 | Current-state reply retains the exchanged scene names and has edited candidate 0. |

The first observed swapped-name reply arrived about 0.388 seconds after the candidate swap request. The first clean reply arrived about 0.572 seconds after the Save request. The capture contains 129 complete current-state replies. All function `01` responses after the initial boundary response matched editor requests, with none pending at stop. No separate unsolicited swap or save announcement was observed.

This confirms that the same current-state query exposes an unsaved exchange of two distinct scene names, followed by the edited-to-clean transition on saving. The trace contains no saved-preset dump after the swap, so it does not independently validate whole-scene fingerprints, all swapped sound settings, or tag movement. The subsequent polling implementation and its separate verification are described below.

### Save-only update requirement

The user explicitly confirmed on 20 October 2026 that automatic changes to browsing names and tag assignments should happen **only after saving**. An unsaved rename or swap must not permanently change the saved library or move its tags. Navigating away and discarding edits must preserve the prior saved names and assignments.

Treat live replies as change candidates. Edited-to-clean is a reason to check saved content, not proof of a save: discard/reload behavior remains untested. Before committing, reread just the affected stored preset and compare it with the previous saved snapshot. Reuse the existing scene tag-move rules: automatic movement requires an exact, unambiguous permutation; name-only evidence can require the existing confirmation, and blank/duplicate names alone must not guess a move. Failed or cancelled reads keep the prior names and tags.

When navigation interrupts an observed edit, retain the affected slot as a candidate and check that slot's stored content rather than committing its last live names or treating navigation as a save. This can distinguish a discarded edit from a saved change after leaving the preset, provided the edit was observed. A change and navigation entirely between polls can be missed; this investigation does not establish comprehensive history tracking. Integrated hardware discard/navigation tests and a verified saved swap comparison remain pending; the implementation has deterministic regression coverage for those flows.

## Save-aware implementation (20 October 2026)

`Fm9ActivePreset` validates and decodes the observed fixed-size FM9 reply. `PresetNameClient.ActivePresetAsync` uses the existing serialized request path and cancellation/quarantine behavior. FM9 polling uses its slot, edited flag and names; scene selection continues using the existing `0C` query rather than relying on the unvalidated private scene-index field. Unsupported extended queries fall back to normal selection reads, retaining saved names/tags and retrying after 30 seconds. Other models do not use this query.

`SavedSceneChangeTracker` retains edit candidates across navigation. Clean-name differences also catch a rename saved between observations. Only a successful stored read can update browsing names. The indexed path compares the previous saved preset with the newly decoded image, reuses `SceneTagMoves`, and atomically saves library/profile/Favorites through `SaveIndexRefresh`. Late reads cannot publish after cancellation, a profile change, disconnect or a changed library. Transient stored-read failures back off while preserving prior data. Live/manual picker refreshes no longer persist unsaved active names.

An exact, unambiguous saved permutation moves scene tags automatically. A unique name-only move uses the existing confirmation, waits for app focus and other dialogs/editors to close, restores focus afterward, and verifies the stored image again after the decision. Cancel preserves prior data and suppresses the same prompt until another live observation or explicit resync. Other profiles and Favorite destination numbers keep their existing policies.

The production parser was replayed offline against all 568 current-state replies in the baseline, unsaved-rename, save and scene-swap recordings; all were accepted and reproduced the previously analyzed state transitions. Regression coverage includes malformed replies, unsaved edits, saved swaps, missed-dirty renames, discarded/saved navigation, cancellation, persistence failure, retry, profile/disconnect rejection and name-only decisions. These checks do not send to hardware. The recorded FM9 transactions validate the protocol shape; a simultaneous live FM9-Edit/updated Preset Maestro acceptance run remains separate work.

Verification: the full Windows Release run passed 915 tests with 3 skipped; after the final dialog cancellation changes, all 89 relevant protocol, polling, tag and resync cases passed. Normal Windows builds, the index-disabled build, and the Mac build passed without source-library overrides. The Windows test EXE is under `TestResults/saved-scene-polling/app/`; publication used a temporary version file and did not replace the installed application. Light/Dark confirmation renders and the full/focused TRX results are retained in ignored TestResults folders. No hardware writes or live preset edits were made during implementation verification.

## Implications for Preset Maestro

There is now a concrete FM9 transaction that can reveal the active preset's current name and all eight scene names without reading the entire library. Save-aware polling now uses it to schedule stored-preset verification.

Live names and the saved browsing/index data must remain distinct. An unsaved name should not silently replace a stored snapshot or prove that saved amp/scene information is current. A validated edited-to-clean transition could trigger a bounded refresh of that active saved preset. Scene-tag reassignment still requires the existing identity/reconciliation rules; names alone do not establish scene identity after swaps.

This does not establish a library-wide change notification. Renames, swaps, replacements, additions and clears affecting inactive slots remain open. Polling the active state cannot be assumed to identify them. Nor does this trace prove the FM9 never sends unsolicited messages under other conditions.

Remaining hardware qualification includes active scene changes, hardware edits, discard/reload, front-panel saving, other-scene renames, renamed stored reads, maximum-length/non-ASCII names and simultaneous editor/reader operation. Confirm firmware scope and measure the cost on DIN as well as USB. Other devices remain disabled until validated on their own hardware. Malformed/late replies and cancellation have automated regression coverage. Other recurring private status/hash requests seen in the editor traffic are not yet decoded and must not be assigned meanings by guesswork.

The manual's safe advice remains to refresh saved browsing information after editing the device. It should not justify that advice with an absolute claim that Fractal devices never report changes.
