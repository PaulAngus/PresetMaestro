# Library sync and recovery — 6 October 2026

## Repeat-sync actions — 17 October 2026

After a complete sync, the status action previously remained blue and labelled “Sync device”, competing with “Use saved data” and suggesting another read was required.

[Fluent 2 buttons](https://fluent2.microsoft.design/components/web/react/core/button/usage/) recommends one primary action per layout, neutral appearances for secondary actions, and brief labels describing the next action. [Windows buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons) recommends clear purpose and state, with concise, specific action text. These are the documented principles; the following state mapping is a PresetMaestro product choice applied to existing Avalonia buttons and theme brushes:

- With no complete saved scan, use “Sync device”. With a complete saved scan for the selected device, use “Resync device”, including reopened dialogs, imported data, direct sync under comparison choices, and retries after declining an update. Matching explanatory copy uses the same label, including names-only completion, legacy fingerprint guidance and amp-data notices.
- After a successful full sync or successful device comparison in the current connection/profile/device context, render “Resync device” with the neutral surface and text brushes. “Use saved data” remains the sole highlighted action. Resync stays enabled and keyboard accessible.
- Before confirmation or when review is needed, the full-read action retains its existing emphasis. Existing incomplete-read actions remain “Resume sync” and “Start over”; these describe recovery more precisely. Names-only and favorite-scene actions retain their scope-specific labels because they do not establish a complete device scan.

There is no new layout deviation. Existing native dismissal, Escape, focus return, scrollable content and fixed footer remain in place. Regression coverage exercises first reads, successful comparisons, full sync completion, reopening and declined replacement. Rendered Avalonia output is inspected in light/dark at 650- and 480-pixel dialog widths within normal and supported narrow main windows.

Validation: the Release test suite passed 847 tests; five opt-in hardware tests were skipped. Release builds with and without Fractal Index passed without warnings. Light/dark completion renders at both dialog widths showed readable wrapping and a neutral Resync action beside the highlighted continuation action.

The connection dialog previously displayed setup choices, general refresh actions and recovery together. An imported assigned library with a partial scan could simultaneously show “Resume to retry missing presets” and disable Resume because the occupied Default library had not been chosen again. This conflated choosing a destination with verifying imported data.

The observed FM9 scan saved 511 of 512 slots. Internal slot 58 (displayed preset 59 with offset 1) failed with a stored-preset query timeout. The current evidence establishes the timeout, rather than a permanent hang or its underlying hardware cause.

## Primary guidance

- [Fluent 2 message bars](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage) recommends concise, specific status messages and actions that directly resolve warnings and errors. Content reflows without truncation. We apply this to a single status area with its recovery action alongside it.
- [Windows progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls) recommends determinate progress where completion can be measured and explanatory text when activity alone is insufficient. We show saved/read counts, identify retries and show the verification phase before resuming.
- [Windows dialogs](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) recommends clear, specific actions and separating dismissal from task actions. Existing native window dismissal and Escape remain available; Stop sync is a task action. Focus returns to the originating control on dismissal and to an available sync/recovery action after an update decision.

## Product choices and implementation

| State | Explanation and main action |
| --- | --- |
| Destination needed | Explain that the device's presets, scenes and amps need a library. Label the library-name field; emphasize Create new library & sync. Overwriting Default remains an explicit secondary choice with its existing confirmation. |
| Destination ready | Name the target and explain what will be read. Offer Sync library. |
| Reading or verifying | Show the target, progress and current activity. Hide setup and extra refresh actions. Stop sync retains checkpoints. |
| Incomplete | Show the saved count and remaining slots, with Resume sync in the same status area. Retain the assigned destination, including imported libraries. |
| Complete | Keep the existing update review when required. Publish only after a complete scan and acceptance; then open Preset Index. |

Other sync options groups names-only, favorite-scene and management tasks. For incomplete scans it offers Start over as a secondary action. For saved-library check choices it permits a direct full sync instead. No duplicate “Next step” card appears during library setup, scanning or recovery. A saved library requiring initial setup still disconnects when that setup dialog is dismissed; its footer makes this explicit.

The status title has a polite accessibility live setting, without announcing every per-slot counter update. Name and hardware fields and progress have accessible names. The dialog uses Avalonia's existing controls and light/dark theme brushes, a scrollable body and a fixed footer.

These are product choices rather than mandatory Fluent layouts. The status area remains visible as the central explanation instead of gaining its own dismiss control; the dialog itself can be dismissed. We retain the existing two saved-library comparison choices and the confirmation for replacing a library.

## Read recovery and saved-data guarantees

A full stored-preset timeout gets one retry of the same slot through the serialized MIDI source. Its existing late-reply quarantine, queue draining, fresh decoder and slot verification remain in force. Names are not queried again for that retry. Invalid data and closed-transport errors are not automatically retried. Cancellation stops before a retry request. Three consecutively failed slots still stop a scan.

Resume verifies a sample of saved populated reads against the connected device before reusing them. If verification fails, the scan starts afresh; name-only empty slots are rechecked. Imported data is still reviewed before publishing a complete update. Partial reads never replace a previously completed library merely to make the progress indicator look finished.

## Validation

Regression coverage exercises transient recovery, bounded failures, cancellation, retained baselines, resume after a permanent slot failure, imported/non-imported assigned partial scans, reconnecting, keyboard activation and existing overwrite safeguards. Actual headless Avalonia renders cover setup, reading and recovery in light/dark themes, including a 480-pixel dialog within the supported narrow main window. The Release solution suite passed 642 tests with five opt-in hardware tests skipped. The build without Fractal Index also passed without warnings. Hardware verification requires a subsequent run in the updated app; the existing running app and its saved library files are not altered during diagnosis.
