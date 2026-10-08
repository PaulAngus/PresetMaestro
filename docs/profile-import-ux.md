# Profile import and device-check feedback

Reviewed 2026-10-06.

## Guidance applied

[Windows dialog guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) recommends a dialog when an action needs additional information, clear consequences and specific action labels, with a safe Cancel action. [Fluent 2 dialog guidance](https://fluent2.microsoft.design/components/web/react/core/dialog/usage) recommends a focused form, focus on its first interactive control, and returning focus to the invoking control on dismissal. The import flow applies these recommendations using the existing Avalonia Fluent controls and palette.

The file is chosen and validated before the name prompt opens. The prompt uses the name inside the export or matching JSON pair, rather than the ZIP filename. If the name already exists, the form explains what overwrite replaces and offers changing the name, Overwrite, or Cancel. Import is disabled while the name conflicts. Enter in a conflicting name field cannot overwrite; the user must choose Overwrite explicitly. Cancel, Escape and native window dismissal preserve existing files and restore focus. Invalid names show an inline field error.

[Fluent 2 message-bar guidance](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage) recommends placing page notifications below the command bar, wrapping their text, making completion messages concrete, and keeping relevant messages until resolved or dismissed. [Windows progress guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls) recommends an indeterminate progress bar when duration is unknown and other app interaction remains available, accompanied by explanatory text.

The profile notice sits below the app header, outside each page's scrolling content. It remains visible across navigation and theme changes. During validation it names the target profile, describes the current scene/preset read, and explains that saved names are being checked for a device match before switching favorites. An indeterminate progress bar indicates ongoing device reads. Cancel switch cancels validation and keeps the current profile and favorites, without treating intentional cancellation as device removal. Existing warnings for disconnected or mismatched devices still ask whether to continue.

Import completion names the profile, gives the favorite count, states which profile is active and provides Use profile when activation is needed. This action goes through the existing device validation and draft-edit confirmation. Errors offer Try again. Completed notices have a separately labelled Dismiss control; Escape dismisses a focused completed notice and returns focus to visible originating content, falling back to the Config navigation button. Notice buttons retain standard keyboard behavior even when preset-entry digits are pending. Accessible names identify the input, progress indicator and dismissal control; the notice title is a polite live region.

## Product choices and deviations

Importing a new or inactive profile continues to select it in Manage Profiles without activating it. The explicit Use profile action and persistent explanation make this choice visible. Overwriting the active profile validates the imported settings before replacing its files and immediately reloads its favorites, mapping and caches. Overwrite preserves the original file pair in `OverwrittenProfiles`; if replacement or catalog persistence fails, changed files are restored.

The global notice, its persistence, backup location and dialog dimensions are product choices, not Microsoft-prescribed values. Avalonia modal windows implement the Windows/Fluent interaction guidance instead of WinUI ContentDialog or React components. The notice cannot be dismissed during validation; Cancel switch ends the operation, and the resulting notice can then be dismissed. This deliberately keeps pending profile checks visible. The import form also includes the concurrent library-selection work documented in [Config UX research](config-ux-research.md).

## Verification

Regression checks exercise source-name prefilling, explicit name conflicts, Enter protection, rename/import, all three cancellation paths, focus restoration, active and inactive overwrite, backup recovery after locked-file failures, activation from Favorites, device-check cancellation across navigation, and keyboard/theme behavior. Rendered prompt, import-result and pending/cancelled notices are inspected in light and dark themes at 1200×640 and the supported 1000×640 minimum window size. The views retain readable labels, wrapping text, separate task/dismissal actions and accessible keyboard focus. Rendered inspection supplements these behavior checks.
