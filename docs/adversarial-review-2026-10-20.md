# Adversarial workflow review — 20 October 2026

## Assessment

The existing green suite did not establish complete outcome coverage. The baseline passed **916 tests**, with five physical-device tests skipped, yet eight new adversarial cases reproduced three user-facing defects. Those eight cases were run against the original implementation before applying fixes and then passed after the fixes.

This review used the current working tree, including existing uncommitted changes. It focused on preset/favorite sending, asynchronous current-state reads, connection changes, profile persistence and imports, interrupted/resumed scans, and scene-tag continuity. It is not a claim that every possible input or hardware timing combination has been tested. No physical MIDI commands or live profile-data changes were made.

## Corrected findings

| Priority | User action and incorrect outcome | Correction and evidence |
| --- | --- | --- |
| P1 | In Preset Sender on a 1024-slot device, type `1023` (offset 0) or `1024` (offset 1) and send. The fourth digit discarded the preceding three; both examples actually sent internal preset **3**, not **1023**. Auto-send could also fire after the incomplete three-digit prefix. | Determine preset-entry length from device capacity on both preset pages. Keyboard tests inspect the exact bank/program/channel command. Auto-send tests verify that three digits send nothing, the fourth sends bank 7/program 127, and Clear cancels a pending send. Corrected the Config label and accessible help. |
| P1 | Click **Use Current**, then open another favorite, cancel/reopen the same favorite, start a new draft, change the draft's preset/scene, or disconnect before the reply arrives. The old reply overwrote the newer draft; disconnect also incorrectly re-enabled the command. | Give the operation a cancellation lifetime, invalidate it on editor changes/disconnect, check connection/profile context and unchanged draft fields before applying results, and restore button availability from actual connection state. Five delayed-reply cases failed before the fix and pass afterwards. A positive test verifies that Use Current changes only the draft and Save commits the selected preset/scene. |
| P2 | While a current preset/scene snapshot is being read, change away from a preset and back. Matching preset numbers before/after the read were incorrectly treated as proof of a stable snapshot. | Check the existing preset-change revision as well as the returned slot. The regression emits change notifications while returning the same slot, requires rejection, then verifies a stable retry succeeds using read-only messages. |

Relevant production files: `MainWindow.Logic.cs`, `MainWindow.Scenes.cs`, `MainWindow.Favorites.cs`, `MainWindow.ApprovedLayout.cs`, and `Midi/PresetNameClient.cs` under `PresetMaestro`.

## Tests whose assertions needed strengthening

- Three assertions searched `DeviceMidi.Sent` for program changes, but that collection records only SysEx. They could never detect bank/program/scene commands. They now inspect `DeviceMidi.Writes`: initial sync requires no writes; the capacity test that deliberately sends once requires exactly that one expected command after another sync.
- The favorite name-sync test had an unbounded polling loop. A regression could hang instead of failing. It now has a ten-second deadline.
- Added a real note-event emitter to the simulated MIDI transport. The channel-filter tests traverse the application's event subscription and inspect outgoing commands, rather than only asserting that a settings property changed.
- Added the new outcome tests to the explicit OfflineWindows source list, so the packaged offline test configuration does not silently omit them.

The new cases are in `PresetMaestro.Tests/AdversarialOutcomeTests.cs` and `SceneClientTests.cs`. Construction/layout assertions elsewhere remain useful for presentation checks, but they are not counted below as proof of behavior.

## Outcome criteria and evidence

“Covered” here means exercised with simulated MIDI, headless Avalonia, captured replay data, or isolated files, as appropriate. It does not mean physically verified on every supported device.

| If I… | I expect… | Automated evidence |
| --- | --- | --- |
| Enter the highest supported preset, with either display offset | The exact last bank/program is sent once | New keyboard cases; existing `PresetIndexSendingTests`, `PresetTranslationTests`, `MidiManagerTests` |
| Enable auto-send, type an incomplete number, then complete or clear it | No premature command; completion sends once; Clear cancels | New sender auto-send case; existing index auto-send case |
| Press Next/Previous at either boundary, then Last | Correct wrapping and return to the preceding successful selection | Four new model/offset cases |
| Try a preset or favorite send that fails | No false success or replacement of the last successful selection; subsequent navigation still works | Two new failure/recovery cases; existing failed Go to test |
| Use Current while editing, then Save | Only the draft changes until Save; no preset/scene write is sent | New positive draft/save test |
| Change or close the editor, edit its selection, or disconnect during Use Current | A late reply cannot overwrite newer work; disconnected action stays disabled | Five new controlled delayed-reply cases |
| Change presets away and back during a snapshot read | Reject an inconsistent snapshot, then permit a stable retry | New `SceneClientTests` case; existing live-name snapshot tests |
| Send mapped controller notes on allowed/disallowed channels | Respect the input port's filter; use the independently configured transmit channel | Four new note-event cases, including MIDI entry disabled and explicit debug bypass |
| Cancel profile switching or import | Preserve the active profile, draft, files and focus | `ProfileValidationTests`, `ProfileImportInteractionTests` |
| Import/overwrite and encounter a storage failure | Preserve previous files and catalog; successful retry remains possible | `ProfileOverwriteTests`, `ProfileLibraryImportTests`, `ProfileStoreTests` |
| Save, reorder, clear or remove a favorite with unwritable storage | Roll back the collection and keep the draft available for retry | `PersistenceFailureTests` |
| Cancel a scan, lose replies, resume, or lose the connection during a read | Keep committed data, retain recoverable progress, reject stale completions | `PresetReadRecoveryTests`, `IndexCheckpointTests`, `IndexResyncTests`, `ConnectionLibrarySetupTests` |
| Edit or swap scenes, with or without saving on the device | Do not commit unsaved live names; preserve/reconcile tags according to the documented decisions | `SavedScenePollingTests`, `SceneTagMoveTests`, `SceneTagContinuityTests` |
| Browse/filter/select without explicitly sending | No MIDI command changes the device | Existing picker, search, index and favorite interaction tests; strengthened sync write assertions |
| Toggle auto-send using the keyboard | The option changes, focus remains, no MIDI is sent merely by toggling it | Four new Light/Dark and normal/narrow cases; rendered Config inspection |

Debug mode intentionally bypasses the channel filter: the UI explicitly labels that behavior. It was treated as an existing product choice, not silently changed into logging-only mode. The new tests make that distinction explicit.

## Validation and limits

Initial review Release run: **940 passed, 0 failed, 5 skipped** (945 total), adding **24 passing outcome cases**. The run collected coverage and enabled a two-minute per-test hang timeout. No hang was detected.

| Assembly | Baseline line / branch | Final line / branch |
| --- | --- | --- |
| PresetMaestro | 93.05% / 81.92% | 93.65% / 82.55% |
| PresetMaestro.FractalIndex | 97.56% / 86.79% | 97.56% / 86.79% |
| PresetNameSync.Core | 95.63% / 91.74% | 95.63% / 91.74% |

These are Coverlet assembly totals, including compiler-generated code. They do not establish complete outcome coverage. All reviewed acceptance cases in the matrix pass, within their stated test boundaries.

The no-index Release build passes with zero warnings/errors. The OfflineWindows configuration compiles and passes all 24 new cases. The focused workflow run passed 68 cases before the four additional checkbox keyboard/render cases were added. Modified C# files pass `dotnet format --verify-no-changes --no-restore --include ...`; `git diff --check` passes. The four Config renders were visually inspected at 1000×850 and 1440×850 in Light and Dark: the corrected label is fully visible and has clear separation from the delay control. Keyboard focus/toggling tests pass.

Full-suite reproduction: `dotnet test PresetMaestro.slnx -c Release --collect:"XPlat Code Coverage" --blame-hang-timeout 2m`. The optional rendering evidence uses `PRESET_MAESTRO_SCREENSHOT_DIR`.

The full repository formatting check reports pre-existing CRLF violations in nine files outside this review's edits: `SceneContentFingerprint.cs`, `SceneTagMoves.cs`, `AmpCompatibilityRow.cs`, `IndexActiveRowTests.cs`, `MidiCapabilityRefreshTests.cs`, `MidiDisabledProcessTests.cs`, `SceneTagContinuityTests.cs`, `MainWindow.IndexActiveRows.cs`, and `SceneNameTagsPanel.cs`. These were not mass-reformatted as part of the logic review. Modified C# files are checked separately.

Remaining validation boundaries:

- The five physical-device cases are opt-in and were skipped. Device acknowledgement, audible results, USB/DIN latency, cable removal and driver behavior need the hardware suites. A successful transport write proves the command was handed to MIDI, not that the instrument reached the requested state.
- Native macOS/CoreMIDI execution and real screen-reader announcements were not exercised on this Windows host.
- A before/after query plus received change notifications cannot prove atomic hardware state when an external device changes without sending notifications. The new revision check closes the observable away-and-back race; it does not invent evidence the protocol does not provide.
- Line and branch coverage identify unexecuted code, not missing user requirements. The matrix is the acceptance record for reviewed workflows; arbitrary malformed imports, all shared-device concurrency combinations, and every exceptional storage/driver failure are not exhaustively enumerated.

Local evidence is retained under `tmp/adversarial-review-2026-10-20`: baseline/final TRX and Cobertura reports, `red/red.trx`, `green/green.trx`, `targeted/targeted.trx`, `offline/offline.trx`, formatting logs, and rendered screenshots. These generated results have not been added to version control. Re-run the maintained tests to reproduce them.

The label correction's Microsoft primary sources, product choices and visual checks are documented in [Config UX research](config-ux-research.md#auto-send-number-length-2026-10-20).

## Follow-up: application-to-FM9 acceptance

The five skipped diagnostics did not cover two central production commands: the hardware harness rejected `SendFavorite` and `SendScene`, while the compatibility navigation test selected scenes through SysEx. This left configured Scene CC behavior unverified despite passing offline outcome tests.

Added five application hardware scenarios covering typed preset bank boundaries, both display offsets, favorite preset-plus-scene selection, all eight scenes through the configured CC, scene-only selection without reloading the preset, Next/Previous/Last, and rapid selection. They use the production application handlers and MIDI transport, with device readback as the success criterion. They refuse an edited starting preset, attempt restoration and compare saved snapshots. Full scope, invocation and remaining physical gaps are in [Hardware acceptance](hardware-tests.md#application-command-acceptance-added-20-october-2026).

Follow-up Release regression: **948 passed, 0 failed, 10 skipped**. All 19 harness tests also pass under OfflineWindows; eight new cases cover explicit Scene CC permissions, input bounds and the edited-state query allowlist. The PowerShell entry point parses and rejects navigation without an explicit Scene CC before launching tests. Evidence is under `tmp/hardware-acceptance-2026-10-20`. At this stage the new physical scenarios were still unverified; the subsequent live acceptance below supersedes that limitation for the tested FM9/firmware/route.

## Physical acceptance using the saved profile

The user authorized navigation with edits saved. Read the active `Default` profile: MIDI channel 1 and Scene CC 34. After closing the physical Mac's Forwarder client as requested, used the saved Windows Forwarder route to the real FM9 on firmware 12.00.

The first connected run exposed a real coordination defect: cancelling a background stored-preset dump imposed a 15-second quarantine on every subsequent state/name read, even when the remaining valid dump arrived promptly. This failed typed presets, favorites and Next/Previous/Last in hardware acceptance. `PresetNameClient` now drains that cancelled dump with the same decoder and releases quarantine when the complete image, checksums and CRC validate. Missing/corrupt completion retains quarantine; cancelled data is never returned as a fresh result. Four new positive regression cases failed before the correction. All six new cases now pass, including two negative completion cases.

After the correction, **all ten physical cases passed, with zero failures or skips**. The application matrix recorded 100 matching readbacks, including ten restoration checks; every touched saved-preset comparison passed. The 512-slot scan found 254 populated and 258 explicitly empty presets with zero errors, taking 115.074 seconds for its scan phase. Compatibility verified the full 336-model roster and 48 scene selections. The FM9 was restored to zero-based slot 2, displayed scene 1. The temporary Windows relay was stopped.

Final software Release regression: **954 passed, 0 failed, 10 hardware cases skipped** (964 total). Those ten cases passed in the separate physical runs above. All 39 focused read/recovery/diagnostic cases also pass under OfflineWindows; the no-index build and formatting checks for changed C# pass. No hang was detected.

The exact app assembly hash, TRX files and trace/report paths are recorded in `build_test/fm9-live-20261020/acceptance-summary.json`; red/green and final software reports are alongside it. See [Hardware acceptance evidence](hardware-tests.md#forwarded-fm9-acceptance-and-correction-20-october-2026) for route, scope and reproduction. These physical results establish the listed preset/scene/read outcomes on this FM9; the manual and other-platform boundaries remain as documented.
