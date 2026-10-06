# Sync change detection: renames, moves, swaps and scene rearrangement

Plan, 2026-10-06. Status: **proposed; design decisions agreed** (see §12). It builds on the reconciliation principles in [fractal-index-data-design.md](fractal-index-data-design.md#rename-move-swap-and-copy-reconciliation) and turns them into an implementable design.

## 1. Problem

Users rename, reorder, copy and rework presets and scenes on the device and in FM9-Edit. Today a full sync compares each slot only with that same slot's previous contents. It uses a hash of the whole saved image (`ContentSha256`) or the bypass-ignored variant. As a result:

- **Renames look like edits.** The preset name (header offset `0x08`) and all eight scene names (body offset `4 + 32·i`) are inside both hashes, so renaming a preset or scene changes its fingerprint.
- **Swaps look like edits.** Scenes are compared by position. Scene 2 ↔ 3 shows as two scene-name changes plus channel differences. Two swapped presets show as two changed slots.
- **Any change breaks tags.** A `PresetAnnotation` is keyed by `slot + ContentSha256 + firmware`, and scene tags by scene number. A rename, a swap or a single saved tweak sends every tag on that preset to **Needs review**. The user then reattaches the tags and maps scene numbers by hand.
- **Favorites go stale silently.** They store only a displayed preset number and scene. After a move they send the user to whatever now occupies the old address.

## 2. Goals and principles

1. **Classify, don't just diff.** After a full sync, every changed slot is described as one of: renamed, moved, swapped, copied, scenes rearranged, changed, overwritten, new or cleared. Each classification carries its evidence and a confidence level.
2. **Certain changes are applied automatically** and listed in a post-sync summary with **Undo**. Uncertain changes ask the user, with the most likely interpretation pre-selected.
3. **Same slot and same name means same preset, however much it changed.** The same applies to a scene with the same number and name. Its tags are kept automatically.
4. **How much something changed is not evidence of identity.** A preset or scene can be reworked extensively and still be the same one. Identity comes from:
   - slot or scene-number continuity;
   - name continuity;
   - content fingerprints;
   - whether the content came from or went to another slot.

   The size of a change only shapes *wording* (§6.5). It is never used to conclude "different preset". Similarity is used for one thing only: finding candidates for a preset that may have moved.
5. **Only ask when user data is at stake.** A decision is required only when tags, scene tags or Favorites depend on it. Untagged inventory changes are reported for information and never block.
6. **Favorites are never retargeted silently.** Affected Favorites are flagged and a new preset/scene is proposed. The user approves each change individually or chooses **Update all**.
7. **Every decision is reversible**, at sync time and later from history.
8. **No regressions in safety.** Partial scans, imported libraries, firmware changes and unvalidated device models keep today's conservative behaviour.

## 3. Phase 0: hardware evidence (gate)

The new fingerprints are only trustworthy if we know exactly which bytes each user action changes. Capture before/after saved images on the FM9 with disposable test presets, then diff them. Store these as fixtures under `PresetMaestro.Tests/Fixtures/Fractal/`, following the existing privacy rule: test presets only, no personal names.

| # | Action | Question answered |
|---|---|---|
| 1 | Save an unchanged preset again | Is the saved image byte-stable? A save counter or timestamp would undermine every content match. |
| 2 | Rename the preset only | Do only header `0x08..0x27` and the CRC (`0x04`) change? |
| 3 | Rename one scene only | Do only body `4+32·i` and the CRC change? |
| 4 | Save preset to a second slot (copy) | Does the image embed its slot number? If so, exclude it from content fingerprints. |
| 5 | Move/swap presets in FM9-Edit's preset management | Do moves preserve bytes, apart from any slot field? |
| 6 | Use FM9-Edit's scene **Swap** button on two scenes with distinct names, channels, bypass states and (where available) scene levels/controllers. Also copy one scene onto another if the editor offers it. | Find **every** scene-indexed field, including undecoded ones such as scene output levels or per-scene controller values. Do scene names travel with the swap? |
| 7 | Change one scene's channel, then bypass, then save | Confirms the per-scene field offsets for all block types, not just Amp. |
| 8 | Firmware update (opportunistic) | Do unchanged presets re-encode on upgrade? |

Results go into `docs/fractal-protocol-research.md` with OBSERVED/UNKNOWN labels. Any action not validated on hardware keeps its fingerprint marked *unverified*, and the reconciler then never treats it as **Certain**.

## 4. New data stored per preset

Add optional fields to `IndexedPreset`. They are versioned and prefixed like the existing `gen3-no-bypass-v1:` scheme. Older libraries simply lack them.

**Identity ignores saved bypass state** (agreed). Players toggle blocks from footswitches and save, and that must not break continuity. Bypass is still recorded and shown as a change.

| Field | Contents | Used for |
|---|---|---|
| `ContentSha256` *(existing)* | Exact saved image | "Unchanged" |
| `BypassIgnoredSha256` *(existing)* | Bypass words zeroed | Same-slot library check (unchanged behaviour) |
| `IdentitySha256` | Header + decompressed body with bypass words, preset name, scene names, CRC and any slot field zeroed | **Rename** detection; cross-slot **move/swap/copy** matching |
| `SceneNeutralSha256` | Identity fingerprint, with every scene-indexed field (from Phase 0) also zeroed | Same preset with scenes rearranged, even after a move |
| `SceneSignatures[8]` | Per scene: hash of every placed block's (block ID, channel) plus other non-bypass scene-indexed values. Scene name excluded. | Scene matching inside a preset |
| `SceneBypassSignatures[8]` | The same with bypass states included | Tie-break only, when two scenes differ only in bypass |
| `BlockIds[]` | Effect IDs present in the grid (no coordinates) | Change descriptions; finding move candidates |
| `FingerprintVersion` | Decoder/layout version | Comparability guard |

Size: about 1–2 KB per preset, roughly 2 MB for a 1024-slot Axe-Fx III library. That is well inside the 32 MB cap.

**Self-verifying scene permutations.** Only hashes of the previous image are kept, not the image itself. To prove a scene rearrangement, apply the candidate permutation to the *new* body's scene-indexed fields and recompute `IdentitySha256`. If it equals the stored old value, the change is provably "the same preset with scenes reordered (and possibly renamed or bypass-toggled)". If an undecoded scene-indexed field exists, the check fails safely and confidence drops to *Likely*. Phase 0 step 6 (FM9-Edit **Swap**) is the reference case.

**Ship the fingerprints first.** A reconciler needs fingerprints on *both* sides of a sync. Releasing the fields early (Phase 1) means users' baselines already contain them when the reconciler ships. The first sync after the upgrade uses exact-hash and slot/name continuity only, and says so.

## 5. Stable local identities

Stored at **library** level, so one decision serves every profile sharing the library (agreed). Tags remain per profile.

- Each preset entry in a committed scan gets a `PresetUid` (GUID). Each scene gets a `SceneUid`.
- On sync, UIDs are carried forward according to the reconciliation result. Examples:
  - a moved preset keeps its UID at the new slot;
  - a copy gets a new UID;
  - an overwritten slot gets a new UID, and the old one becomes *retired*.
- `PresetAnnotation` binds to `PresetUid`. Scene tags bind to `SceneUid` instead of scene numbers. The displayed `S#` comes from the current scan, so tags follow a scene that moves.
- `Favorite` gains an optional binding (`LibraryId`, `PresetUid`, `SceneUid`). The send target is still the stored preset number and scene: the binding *detects* drift and never redirects silently.
- The library keeps a bounded **lineage log** of sync events. Each entry records: sync ID, time, event, old and new slot/scene and UID, confidence, and who decided (automatic or user). The log drives Undo, history, and Favorite checks for profiles that weren't active at sync time.

**Migration.** On first load, each existing annotation that matches the committed scan (current `Matches` rule) is bound to that preset's newly assigned UID. Scene-number tags map to that preset's scene UIDs. Unmatched annotations stay pending exactly as today. Favorites are bound when their preset/scene resolves in the assigned library; otherwise they stay unbound and behave as now.

## 6. Reconciliation engine

The engine is a pure, UI-free component in `PresetMaestro.FractalIndex/Reconciliation/`: `Reconcile(baseline, candidate, annotations, favorites) → ChangeSet`. It only runs when both scans are complete, use the same device model, and have comparable fingerprint versions.

It keeps two outputs separate for every item:

- **Identity verdict:** what this is, with a confidence level.
- **Change description:** what is different (§6.5). Magnitude lives here and never feeds back into the verdict.

### 6.1 Preset matching

1. **Exact pass.** Slots whose `ContentSha256` is unchanged → *Unchanged*, UID carried.
2. **Index the rest** by `IdentitySha256` on both sides. Record how many times each value occurs (templates and "init" presets often repeat).
3. **Same slot, identity equal** → *Renamed* and/or *bypass changed* (Certain).
4. **Unique cross-slot match.** The old content occurs once in the old scan and once in the new scan, at a different slot:
   - old slot now holds different content → *Moved* (Certain; *Moved + renamed* if names differ).
   - two moves that cross each other → grouped as a *Swap* (Certain).
   - longer chains, e.g. after inserting a preset → grouped as a *Reorder* (Certain).
5. **Content now occurs more often than before** while the original stays put:
   - at a previously empty slot → *Copied*. The copy gets a new UID (Certain).
   - over an existing preset → *Overwritten with a copy of 033*. The overwritten preset's UID is retired and the copy gets a new UID (Certain). If the overwritten preset was tagged, its tags go to review.
6. **Scene-neutral pass.** Repeat steps 3–5 with `SceneNeutralSha256` plus a matching scene-signature multiset. A hit means the same preset with scenes rearranged, possibly also moved (Certain if the permutation verifies, otherwise Likely).
7. **Continuity pass** for the remaining changed slots. Change magnitude is ignored here:
   - **Same slot, same name** → *Same preset, changed*. The UID is carried and the change is auto-applied, however extensive (agreed).
   - **Different slot, same name, old slot changed or empty, name unique on both sides** → *Moved and changed* (Likely, pre-selected "Same preset").
   - **Same slot, different name**, content not found elsewhere → *Renamed and changed?* (Possible). Pre-select "Same preset", because the slot is unchanged. Ask only if tagged or a Favorite targets it.
8. **Candidate search** for anything still unresolved, e.g. a tagged preset that vanished from its slot. Rank unmatched new presets by shared blocks, Amp models, scene names and preset name. These are offered only as a "Find it" list in review: never pre-selected and never automatic. A preset that was moved, renamed *and* heavily reworked may not be found; the user can still pick it from the full list, as today.
9. **Duplicates.** Where content is not unique, fall back to slot continuity: the copy at the original slot keeps the UID. Ask only if tags or Favorites are involved.

### 6.2 Scene matching (inside a preset whose continuity is established)

1. Same number, same signature and name → *Unchanged*.
2. Same number, same signature, different name → *Scene renamed* (Certain once Phase 0 validates the name offsets).
3. Look for a permutation that maps old signatures to new ones. Use names, then bypass signatures, to break ties. Verify it by reconstruction (§4) → *Scenes rearranged* (Certain), or *Likely* if the check cannot complete.
4. **Same number, same name, signature changed** → *Same scene, changed*. Kept automatically, however extensive.
5. **Identical scenes.** If several scenes have identical signatures *and* names, the choice between them doesn't change the sound. Ask only if those scenes carry *different* tags or Favorites; otherwise pick the lowest-number pairing.
6. **Same number, different name, signature changed**, with no permutation explaining it → *Scene renamed and changed?* (Possible). Pre-select "Same scene"; ask only if tagged or a Favorite targets it.

### 6.3 Confidence and default handling

| Confidence | Meaning | Default |
|---|---|---|
| **Certain** | Proven by a validated fingerprint, unique on both sides | Applied automatically; listed in summary; Undo available |
| **Continuity** | Same slot *and* name (preset), or same number *and* name (scene); content changed by any amount | Applied automatically; listed in summary as "changed"; Undo and **Change…** available |
| **Likely** | Strong but not proven, e.g. same name at a new slot | Pre-selected in review; asks only if tags or Favorites are affected |
| **Possible** | Slot continuity only, or plausible candidates | Asks if tags or Favorites are affected; slot continuity is pre-selected where it exists |
| **Unknown** | No candidate | No question. Old tags stay pending (today's behaviour); the new occupant gets a new identity |

### 6.4 Library match percentage

The percentage threshold was removed on 2026-10-06 (decision 8). Until the reconciler ships, a check is confirmed only when every compared preset matches, and any difference is shown for review. With the reconciler, a preset counts as *accounted for* when either:

- its identity content exists somewhere in the new scan, or
- it has slot-and-name continuity.

A check is then confirmed when every preset is accounted for, and the remainder (overwritten, unknown) is what the user reviews. Counts are shown, never percentages: "470 unchanged, 28 changed, 6 renamed, 4 moved · 2 need review".

### 6.5 Describing changes (wording)

Because a preset can change a lot and still be the same preset, wording must describe *what changed* without implying the preset was replaced.

- **List concrete differences** from decoded data:
  - preset or scene name;
  - blocks added or removed (`BlockIds`);
  - Amp model per channel;
  - channel and bypass per scene;
  - scenes moved.

  Where only the fingerprint differs: "other settings changed (not individually readable)".
- **Show magnitude as a plain phrase, not a score:** "small change" (one or two decoded differences), "several changes", or "extensively changed" (many differences, or blocks added/removed). Do not show similarity percentages; they read as a probability that it is the same preset.
- **Ask about identity as a question, not a verdict.** Say "Is this still *Brit Lead*?" rather than "Brit Lead was replaced".
  - Options: **Yes, same preset (changed)** / **No, it's a different preset**.
  - The words *replaced*, *deleted* and *new preset* are used only when the evidence shows it: content copied over from another slot, a cleared slot, or a previously empty slot.
- **Vocabulary:**

| Use | Avoid |
|---|---|
| changed, reworked, settings changed | edited (implies small), replaced (implies different) |
| moved to 045 | relocated, re-indexed |
| scenes swapped / rearranged | reordered by scene ID |
| tags kept / tags followed the preset | tags reattached, rebound, UID |
| overwritten with a copy of 033 | replaced |

### 6.6 Change-description data

Change descriptions need old and new decoded data. The committed baseline already keeps names, scene names and the Amp blocks with per-scene states. Phase 1 adds `BlockIds` and per-scene bypass for all blocks, which is enough for the descriptions above without storing raw images.

## 7. User experience

All new UI follows the project rule: consult the Microsoft Fluent 2 guidance for each pattern (dialogs, info bars, lists with inline choices, undo). Record the links and any deliberate deviations in a new `docs/sync-review-ux-research.md`.

### 7.1 Flow after a full sync

1. The scan completes, then `Reconcile` produces a `ChangeSet`.
2. **Nothing needs a decision.** The scan is committed, Certain and Continuity changes are applied, and a non-modal summary bar appears: *"Library updated · 28 changed · 3 renamed · 1 swap · scenes swapped in 2 presets (tags kept) · 2 Favorites to check — Review · Undo"*.
3. **Decisions needed.** The existing *Confirm library update* dialog becomes **Review changes**, with these groups:
   - **Needs your decision.** Expanded, each item with radio options and an evidence line.
   - **Favorites to update.** Proposed retargets with checkboxes.
   - **Applied automatically.** Collapsed; each item has **Change…**.
   - **Other changes.** Untagged, informational only.
   - Footer actions: **Apply** · **Decide later** (commit the scan; undecided tags stay pending under *Needs review*, as today) · **Cancel** (keep the previous library).
4. **Decide later** and the summary's **Review** both reopen the same view from the Preset Index page.

### 7.2 Options by change type

| Change | Evidence line, for example | Options (default first) |
|---|---|---|
| Preset renamed | "Settings unchanged; renamed 'Brit Lead' → 'JVM Lead'" | *Auto:* tags kept. **Change…** → It's a different preset |
| Same preset, changed | "Same slot and name; extensively changed: Amp 1 ch B model, Drive 1 and Delay 2 added" | *Auto:* tags kept. **Change…** → It's a different preset |
| Moved / swapped / reordered | "Same preset, now at 045" | *Auto:* tags followed. **Change…** → Leave tags on the old slot number |
| Copied | "Copy of 012 Brit Lead created at 087" | *Auto:* copy is untagged. Optional action: **Copy tags to the copy** |
| Overwritten with a copy | "012 now holds a copy of 033 Clean Verb" | *Auto:* 012's old tags go to review; copy untagged. Optional: **Copy tags from 033** |
| Scenes swapped / rearranged | "S2 ↔ S3 (FM9-Edit swap; verified)" | *Auto:* scene tags followed. **Change…** → Keep tags on scene numbers |
| Scene renamed / scene changed | "S4 renamed 'Solo' → 'Lead'" / "S4 several changes" | *Auto:* scene tags kept |
| Moved and changed (Likely) | "'Clean Verb' now at 020; several changes" | Yes, same preset (moved) · No, different presets · Later |
| Renamed and changed (Possible) | "012 renamed 'Brit Lead' → 'Plexi Crunch'; extensively changed. Is this still Brit Lead?" | Yes, same preset (changed) · No, it's a different preset · Later |
| Tagged preset not found | "'Hi Gain Rhythm' is no longer at 040" | Find it (ranked list, nothing pre-selected) · Later |
| Ambiguous duplicates | "Identical content at 100, 101, 102" | Choose which slot (list) · Copy tags to all · Later |
| Identical scenes with different tags | "S5 and S6 are identical" | Pick a mapping · Later |

The wording avoids internal terms like *UID* or *fingerprint*. The evidence line always says what was compared, so users can judge the suggestion.

### 7.3 Favorites

- For each Favorite bound to a UID that moved, the review proposes a change, for example *"'Solo' 012 S3 → 045 S2 (preset moved, scenes swapped)"*. Each change has a checkbox, and there is **Update all**. Nothing changes until the user approves.
- A Favorite whose preset or scene was only *changed* needs no action. Its target is still the same preset and scene, so it is not flagged.
- Favorites whose target was *overwritten* or is *uncertain* show **Keep target** · **Point to <candidate>** · **Clear**.
- Favorites in *other* profiles that share the library are checked against the lineage log when the profile is activated. The same review then opens, scoped to Favorites.
- Unbound (legacy) Favorites get a warning only when the content at their address changed identity, not merely its settings.

### 7.4 Undo and history

- **Undo** in the summary reverts that sync's automatic decisions: UID carry-forward, tag moves and Favorite retargets. It does not revert the scan itself.
- A **Sync history** view, reached from Preset Index, lists the lineage log. Any automatic or user decision can be changed later. This extends the current single-step `ReviewHistory`. As today, undo is blocked when later tag edits would be lost.

### 7.5 Preference

Config → Library adds *"Apply certain changes automatically"*, default **on**. When off, Certain and Continuity items appear pre-selected in **Review changes** instead.

## 8. Quick name sync and partial data

A quick name sync gets names only, so it cannot prove anything. It may show *hints*, such as "'Clean Verb' appears to have moved from 010 to 020 — run a full sync to confirm". Hints never move tags or Favorites. Partial or cancelled scans don't reconcile (unchanged).

## 9. Edge cases

- **Firmware change.** If many slots differ while names and decoded structure match, offer one bulk choice: *"Firmware 11 → 12 re-encoded 480 presets; they are the same presets"*. Slot-and-name continuity already keeps their tags; the bulk choice covers the rest.
- **Fingerprint version mismatch.** Fall back to exact-hash and continuity behaviour for those slots, and explain it in the summary.
- **Imported libraries** stay review-first. Automatic application is disabled until the first confirmed sync on real hardware.
- **FM3 / Axe-Fx III.** The reconciler is enabled per device definition only after Phase 0 has been repeated for that model. Until then: exact-hash and continuity behaviour with hints.
- **Empty ↔ populated** transitions are *New* / *Cleared*, never matched to each other.
- **Name reused for an unrelated preset at the same slot.** Continuity keeps the tags. This is the accepted trade-off of decision 1; the summary lists it as "changed" with **Change…**, so it is one click to correct.
- **Performance.** Hash maps make matching O(n). Candidate ranking runs only on unresolved tagged items, which are typically a handful.

## 10. Work breakdown

| Phase | Deliverable | Main files |
|---|---|---|
| 0 | Hardware diffs (including the FM9-Edit scene Swap), fixtures, protocol notes | `scripts/Test-Hardware.ps1`, `Tests/Fixtures/Fractal/`, `docs/fractal-protocol-research.md` |
| 1 | New fingerprints and block/scene data computed and stored; no UI change | `StoredPresetDecoder.cs` (name/slot offsets), new `ContentFingerprints.cs` (subsumes `BypassIgnoredFingerprint`), `SceneLayout.cs`, `Gen3PresetBodyDecoder.cs`, `PresetSnapshot.cs`, `PresetIndexReader.cs`, `IndexJson.Validate` |
| 2 | Reconciler engine, change describer, exhaustive unit tests | `Reconciliation/PresetReconciler.cs`, `SceneReconciler.cs`, `ChangeSet.cs`, `ChangeDescriber.cs`, `CandidateRanking.cs`; `LibraryMatch.cs` ("accounted for") |
| 3 | UIDs, lineage log, annotation/Favorite binding and migration; auto-apply Certain and Continuity; summary bar and Undo | `IndexLibrary.cs`, `Favorite.cs`, `FavoriteJsonConverter.cs`, `MainWindow.FractalIndex.cs` (around the commit at `cache.Committed = …`), new `MainWindow.SyncSummary.cs` |
| 4 | **Review changes** dialog for Likely/Possible items; Favorite retarget | `MainWindow.LibraryUpdate.cs` → new `MainWindow.SyncReview.cs`; `MainWindow.FractalIndexView.cs` (*Needs review* reuses it) |
| 5 | Sync history, preference, name-sync hints, firmware bulk action | `AppSettings.cs`, `MainWindow.SyncOptions.cs`, `MainWindow.LibraryMatch.cs` |
| 6 | Docs: usage guide, data design, user manual chapter | `docs/fractal-index-usage.md`, `docs/fractal-index-data-design.md` |

Phases 1 and 2 can ship together. Phase 1 should reach users as early as possible (§4).

## 11. Testing

- **Engine unit tests** with synthetic scans, one for every row in §6–7:
  - rename, move, swap, 3-cycle, insert-shift
  - copy, overwrite-with-copy, duplicate templates
  - move + rename, move + change
  - same slot/name with an *extensive* change (must keep identity), same slot with a new name
  - clear
  - scene swap, scene rotation, scene swap + bypass toggle
  - identical scenes with and without differing tags
  - firmware re-encode
- **Property tests:**
  - Random preset and scene permutations of a fixture are always *Certain* moves/rearrangements with the correct mapping.
  - Random bypass toggles never change identity.
  - Arbitrary amounts of change at the same slot and name always keep identity.
  - No random edit at a *different* slot is ever classified *Certain*.
- **Wording tests:** change descriptions never use *replaced*/*different* for Continuity items, and magnitude phrases match the number of decoded differences.
- **Fixture tests** from the Phase 0 captures, including an FM9-Edit scene Swap.
- **Migration tests:** legacy libraries and profiles bind annotations and Favorites correctly; no tag is lost or attached to a different preset.
- **UI regression:** follow the existing proportionate checks, and inspect light/dark at normal and narrow widths per `AGENTS.md`.
- **Hardware:** a manual script (rename, rework, swap presets, Swap scenes, then sync) added to `docs/hardware-tests.md`.

## 12. Decisions (2026-10-06)

1. **Same slot + same name keeps tags automatically**, regardless of how much the settings changed. The same rule applies to scenes (same number + same name). These items are listed in the summary with **Change…**.
2. **Decision scope:** identity decisions are made once per library and apply to every profile sharing it. Tags and Favorites remain per profile.
3. **Identity ignores saved bypass state.** Bypass-inclusive scene signatures are used only to break ties.
4. **Scene operations:** FM9-Edit offers a scene **Swap** button. Phase 0 must confirm which per-scene data it moves (names, channels, bypass, levels, controllers).
5. **Automation default:** Certain changes are applied automatically with a summary and Undo; there is a preference to review instead.
6. **Favorites** are never retargeted without approval.
7. **Large changes are not a different preset.** Change magnitude affects wording only. Identity questions are phrased as "Is this still …?", never as a verdict.
8. **No percentage threshold.** The configurable preset match threshold is removed (done 2026-10-06). Checks are confirmed only on an exact match; any difference is shown and the user decides. FM9 name differences no longer skip the content check. The reconciler later widens "match" to "accounted for" (§6.4), still without percentages.

### Remaining open

- Whether FM9-Edit's scene Swap also swaps any data held outside the scene-indexed block fields. If it does, scene swaps start as *Likely* until those fields are decoded (Phase 0).
