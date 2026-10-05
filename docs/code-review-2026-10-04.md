# Code review — 4 October 2026

## Assessment and scope

Reviewed the current working tree, including the accumulated UI and library changes, rather than only the last commit. The review covered project boundaries, persistence and migration, imports/exports, MIDI routing and lifetime, sync cancellation and recovery, catalogue lookup costs, UI feedback, dependency advisories, analyzers, and automated coverage. Existing unrelated work was preserved.

The application has useful domain boundaries and substantial behavioral tests, but it is not free of architectural debt. The largest concern is the concentration of presentation, device orchestration and mutable state in the `MainWindow` partial class. Passing tests and high coverage are evidence, not certification that all hardware behavior or failure combinations are correct.

## Findings corrected

| Priority | Trigger and previous behavior | Correction and regression evidence |
| --- | --- | --- |
| P1 | A MIDI callback enumerated the thru-input dictionary while the UI added or removed ports. Output disposal could also overlap a send. These races could interrupt routing or raise background exceptions. | `MidiManager` now uses a concurrent input registry, snapshots exposed port names, and serializes output replacement/disposal with sends. Late callbacks from detached main inputs are ignored. Tests exercise the production routing class with injected NAudio endpoints, including callback/refresh overlap and closing during a three-message favorite send. Port lifecycle commands remain owned by the UI thread. |
| P1 | Favorite create, edit, reorder, clear and delete changed the live collection before persistence succeeded. A storage exception could escape the event handler and leave unsaved values presented as current. | A shared mutation/save boundary restores values, order and object identities on failure. The editor remains open with the user's draft, an actionable warning is shown, and retry works. Five operation-specific regression cases verify rollback and successful retry. |
| P2 | A preset-name save exception in sync cleanup prevented the busy state from clearing. Successful reads also saved the same result twice. | Save once, restore the in-memory name cache on failure, and release busy state in a nested `finally`. Tests cover both completed and interrupted reads followed by failed storage and successful retry. |
| P2 | Closing the window could propagate a storage exception before orderly device cleanup. | Failed persistence cancels closing and explains how to retry. Successful closing unsubscribes MIDI handlers and disposes the transport. A regression verifies that failed closing leaves the window and connection available, then succeeds after storage recovers. |
| P2 | Saving settings wrote the profile before machine settings. A locked machine settings file therefore left a partially applied save. | Retain the original profile contents and restore them if the second write fails. A real locked-file test checks both files, retry, and temporary-file cleanup. Rollback failure is reported with both underlying errors. This is recoverable failure handling, not a crash-atomic transaction across two files. |
| P2 | Loading a legacy `NoteMap` rewrote the original settings file before the profile migration backup existed. | Migrate in memory and let normal persistence perform the write. The test verifies original bytes remain unchanged after reading and are preserved in the migration backup. |
| P2 | Exporting a historical library reference without a saved cache fabricated an empty portable snapshot. Import could make that missing library appear to exist again. | Export only actual saved or existing portable snapshots, retaining historical references and annotations. A round-trip test distinguishes the real saved library from the historical reference. |
| P2 | ZIP imports had a size check, but raw JSON-pair imports used unbounded `ReadAllText`. ZIP text reading also trusted archive metadata. | Share a bounded reader across both import paths. Reject raw files above 32 MiB and cap decoded text at 32 Mi characters, including decompressed ZIP text. Existing ZIP byte-size checks remain. A large raw-file regression verifies rejection before profile creation or alteration. |
| P3 | Amp-family usage repeatedly searched all preset blocks/channels for every family; manufacturer groups were rebuilt for every displayed row. | Build model-to-preset usage once and group manufacturer rows once. The usage regression includes multiple family variants, duplicate channels and two blocks while counting each preset only once. Two unreferenced legacy favorite event handlers were also removed. |

Relevant implementations: `Midi/MidiManager.cs`, `Core/FavoritesManager.cs`, `MainWindow.Favorites.cs`, `MainWindow.PresetNames.cs`, `MainWindow.Logic.cs`, `Core/ProfileStore.cs`, `Core/SettingsManager.cs`, `Core/ProfileStore.FractalIndex.cs`, and `PresetMaestro.FractalIndex/AmpBrowser.cs`.

## Structure and efficiency

The protocol assembly is independent of Avalonia. The index assembly contains decoding, matching, catalogue and storage behavior; the application owns presentation and hardware integration. Nullable annotations, recommended .NET analyzers and warnings-as-errors are enabled. Persistence generally uses temporary files and replacement. Protocol parsing and queues already have bounds, and the existing suite exercises corrupt packets, cancellation, timeouts, migrations, shared libraries and navigation.

The MIDI tests use the existing NAudio interfaces and a small internal factory seam. No production package or general-purpose dependency-injection framework was added. Favorite rollback allocates a snapshot only for an actual edit/reorder, with linear cost in the favorite count. Catalogue usage now indexes saved channels once instead of repeating the channel traversal for every family; no persistent cache or invalidation machinery was introduced. These are reductions in repeated work, not a measured claim about end-to-end device latency.

The standalone executable includes the .NET runtime, Windows UI/rendering dependencies and native assets. Its file size should not be treated as equivalent to application-source bloat. The project already selects Windows-specific Avalonia/NAudio packages rather than their broader platform bundles. Trimming reflection-dependent UI code without dedicated validation would create release risk.

## Follow-up implementation, 5 October 2026

The five follow-ups below have now been implemented, with these boundaries:

1. **Asynchronous discovery.** `MidiPortDiscovery` coalesces requests; production WinRT enumeration runs off the dispatcher. Refresh and connection monitoring await it, with a timeout and connection-generation checks. Cancellation cannot start overlapping native enumeration. Tests hold discovery pending while dispatcher work continues and reject an old result after reconnect. Explicit port opening still uses the existing synchronous native open path. Physical close/reopen is tested; cable unplug/replug and a real driver hang were not performed.
2. **Incremental scan checkpoints.** Each completed slot appends a compact, flushed journal record instead of rewriting both full scans. Startup/resume recovers completed records, ignores an unterminated final record, rejects corrupt current-generation records, and ignores old scan generations. Final/cancelled/failed scans compact back to the existing atomic JSON format. Tests cover recovery using a new reader, errors replaced by successful reads, compaction, invalid appends and stale journals. This tests process-crash-style recovery, not a hardware power-loss guarantee.
3. **Smaller orchestration responsibilities.** Extracted `DeviceConnectionSession`, `PresetNameReadSession`, `FavoriteCommands` and `ProfileLibraryCoordinator`. The window retains controls, dialog lifetime and UI feedback; connection identity/cancellation, read progress, favorite edit rollback and library/profile persistence decisions now have independently testable owners. Existing UI interaction/rollback tests remain. This is a targeted reduction in coupling, not a claim that the large window has become a complete MVVM implementation.
4. **Explicit persistence failures.** Parameterless settings/favorite wrappers now propagate the existing storage exceptions instead of silently swallowing them. The normal profile-store path and UI error handling remain in place.
5. **Metadata reuse.** Library lists and mapping lookups use immutable summaries cached by actual file identity/stamps/length. Save/delete invalidate the cache, list enumeration removes absent entries, and tests cover external replacement, rename/mapping change, corruption and deletion. Full scan loads still read current content. Deliberately replacing a file while preserving every stamp and its length is outside this cache contract.

Measured with a captured full FM9 library and an existing committed baseline, isolated from MIDI latency:

| Operation | Previous full-file approach | Current approach |
| --- | ---: | ---: |
| 512 checkpoint operations | 2,834 ms | 199 ms |
| Serialized checkpoint payload | 896,575,211 bytes | 536,469 bytes |
| 100 metadata lookups | 1,181 ms | 1 ms (warm cache) |
| Allocations for those 100 lookups | 1,168,435,664 bytes | 60,128 bytes |

These local microbenchmarks exclude backup copies/filesystem overhead from the payload count and do not imply the same improvement in device transfer time. Reproducible measurement source and results are retained under ignored `build_test/storage-benchmark`. A new dependency/framework was not introduced.

[Physical test documentation](hardware-tests.md) describes the FM9 read-only suite, replay fixture, cross-model compatibility reports and explicitly enabled preset/scene navigation. FM9 12.00 is the physically exercised model/firmware; other models remain candidates until their own captures and runs establish compatibility. All 336 device amp names matched the scoped catalogue. A full baseline scan passed; a later scan decoded all 512 slots without errors but detected an active scene change and correctly failed its state-preservation assertion. The trace contains only read requests from this harness; that observation is retained rather than converted into a pass. A subsequent full rerun passed all 512 slots (254 populated, zero errors), preserving the active preset and scene in 161.895 seconds. The earlier scene change remains unexplained; its failed evidence is retained.

Follow-up validation: **587 passed, 0 failed, 5 opt-in hardware cases skipped** in the ordinary suite. Line/branch coverage is **90.80% / 79.50%** for the application, **97.82% / 86.77%** for FractalIndex and **93.56% / 90.69%** for the protocol assembly. Formatting verification and the separate build with `EnableFractalIndex=false` pass. Evidence: `build_test/physical-followup-final.log`, its coverage XML, `build_test/physical-followup-format.log`, and `build_test/fm9-no-index.log`. Physical results are recorded separately; skipped physical tests do not contribute hardware assurance.

## Validation and lasting checks

Baseline: **544 passing tests**. Initial line/branch coverage was **87.97% / 76.76%** for the application, **97.65% / 86.04%** for FractalIndex, and **92.98% / 90.11%** for the protocol assembly. The production MIDI manager had only about **2.5% line coverage** because most application tests substitute an `IMidiManager` fake.

Initial review suite: **567 passed, 0 failed, 0 skipped** (23 additional cases). Release build/test compilation and formatting verification pass. The separate Release build with `EnableFractalIndex=false` also passes with zero warnings/errors.

| Assembly | Final line coverage | Final branch coverage |
| --- | ---: | ---: |
| PresetMaestro | 90.63% | 79.43% |
| PresetMaestro.FractalIndex | 97.67% | 86.10% |
| PresetNameSync.Core | 92.98% | 90.11% |

The production `MidiManager` class now has **90.04% line / 78.19% branch coverage**. The largest remaining holes there are Windows driver discovery/construction and uncommon driver failure paths. Coverage includes compiler-generated classes reported by Coverlet and is not a manual accessibility or hardware-quality score. Evidence: `build_test/code-review-verified.log`, `build_test/code-review-verified/*/coverage.cobertura.xml`, `build_test/code-review-format.log`, `build_test/code-review-no-index.log`, and `build_test/code-review-dependencies.log`.

The new `.github/workflows/validate.yml` runs a Windows Release build with analyzers, the full test suite with coverage, formatting verification, and a build with Preset Index disabled. It retains TRX and coverage artifacts. The commands were exercised locally; the hosted workflow still needs its first run after these changes are committed and pushed. Publishing is separate from CI.

Error feedback was rendered and inspected in Light/Dark at the existing favorite-editor minimum of **1320×850** and at **1440×850**. The narrow-start test begins at 1000 pixels; opening the editor deliberately raises the existing minimum to 1320. The tests check that a failed save itself moves neither content nor keyboard focus. Images are under `build_test/code-review-screenshots`. Actual Windows screen-reader announcements were not tested.

The dependency audit (`dotnet list PresetMaestro.slnx package --vulnerable --include-transitive`) reported **no known vulnerable packages** from NuGet on this date. This does not substitute for checking future advisories.

The initial 4 October review used no physical device commands or live user-data edits. The separately documented 5 October follow-up used explicitly authorized read-only device requests. MIDI adapter tests use fake NAudio endpoints; storage tests use temporary directories and a real Windows file lock. Scene polling, legacy fallback wrappers, startup, some exceptional recovery branches and actual WinRT/WinMM device discovery still have less coverage than the domain code.

## Guidance used

- [Microsoft .NET design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/): useful design principles, not a mandatory architecture or certification checklist.
- [Microsoft unit testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices): behavior-focused, isolated regression tests.
- [Microsoft asynchronous programming scenarios](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios): avoid blocking UI work on asynchronous I/O.
- [Microsoft thread-safe collections](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/): concurrent access needs an appropriate collection or synchronization.
- [Fluent message bars](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage) and [Windows InfoBar guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar): actionable error feedback, preserving task context and avoiding disruptive content movement. The application's existing overlay is an Avalonia product adaptation; see `send-feedback-ux-research.md`.
- [GitHub .NET build/test guidance](https://docs.github.com/en/actions/tutorials/build-and-test-code/net): repeatable CI execution with a selected SDK and retained test results.
