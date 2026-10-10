# Preset Maestro

A desktop controller for selecting presets and scenes, saving favorites, and reading preset/scene names from the device. Built with .NET 10 and Avalonia. The established Windows build targets Windows 10 version 2004 or later. macOS preparation targets Apple Silicon (including M2) on macOS 15 or later, with native launch and MIDI hardware acceptance still pending. Windows and Mac packages include only their own desktop and MIDI backends; Linux is not a target.

Windows MIDI input uses NAudio's WinRT backend so long-running systems are not exposed to the legacy WinMM signed timestamp overflow; output continues to use WinMM. The Mac backend uses CoreMIDI through DryWetMIDI. See [Windows and macOS development](docs/windows-macos-development.md) for builds, Mac setup, signing and the remaining acceptance checks.

FM9 preset and scene name reads are supported by the current protocol code. FM3 and Axe-Fx III can be identified and used for numeric preset sending, but name reads and scene-state tracking are disabled for those models until their device transactions are verified. The Axe-Fx III picker still offers all 1024 numeric preset slots; names for those slots cannot currently be synchronized.

## Start here when you have forgotten how it works

1. Connect your device over USB, or connect both MIDI directions through a MIDI interface.
2. Open **Config**, select the MIDI input and output, and click **Connect**. If a port is busy, close device editor or another MIDI application using it.
3. Open **Config → Device for this profile → Manage devices**. Select your device, check **MIDI Channel**, **Display Offset**, and **Scene CC#**, then **Save changes**. These settings are shared by profiles assigned to that device. Scene CC defaults to **34**; the device's Scene Select assignment must match it. Incoming note assignments are under **Config → Entry Options → MIDI note mapping**.
4. Open **Preset Sender** and select a preset. Under **Config → Scenes**, the panel shows all eight scenes. Cached names appear first, then the app reads the current names from the device. Preset Sender retains its original display/keypad layout.
5. Click a scene button to select that scene within the current preset.

You do **not** need to scan all presets before using scenes. For everyday use, just connect and select a preset.

**Controller block toggles:** under **Config → MIDI Connection → Thru In(s)**, select one input route per controller. If the same controller is connected through both USB and a MIDI interface, checking both forwards each press twice. With Fractal's Effect Bypass mode set to Toggle, the second CC reverses the first. Leave only USB or only the interface checked for that controller. Different controllers can still use separate checked inputs. Thru messages retain their original channel and values; the device's receive channel and MIDI/Remote CC assignments must match. Diagnostics show the source and output ports for each forwarded message.

## Preset Index

The optional **Preset Index** page browses locally cached saved presets, Amp channels and scenes, with profile-owned preset and scene tags. Add or select a device under **Config → Device for this profile**, then use **Sync device** from the Index page. Scans support progress, cancellation and resume. Profile export includes decoded snapshots for offline browsing. See [the setup and usage guide](docs/fractal-index-usage.md) for device support, tag review and storage details.

## Profiles

Use **Config → Profiles** to select the active profile or rescan for profiles. **Manage Profiles** opens a compact profile list and details screen, with **Back to Config** to return. Selecting a row only inspects it; **Use profile** explicitly activates it. The separate **Active** indicator identifies the profile currently in use. **Copy…**, **Rename…**, **Delete…** and **Export…** apply to the selected profile, including inactive profiles. Copy duplicates its favorites, device assignment and cached names and activates the copy. Profiles assigned to the same device share its preset mapping. Copying or exporting the active profile includes its current data. Deleting an inactive profile leaves the active profile unchanged; deleting the active profile safely selects another readable profile first. At least one profile must remain. **Create…** asks for a name and activates a new profile with empty favorites and caches; assign its device in Config. Switching saves the current profile and clears the current Favorites selection. Finish or cancel a name sync before changing profiles.

The selected profile details show both the device model and its device name. Connecting records the device name in the active profile, so it remains visible when disconnected and travels with copies and exports. Older profiles show **Not recorded** until connected; devices with a blank name show **Unnamed device**.

Before activating a different profile, the app checks its device type, device name, and saved preset and scene names against the connected device. Name checks use read-only queries and do not select a preset. An **OK/Cancel** warning lists any mismatches or checks that could not be completed, including missing cached names and name reads unsupported by the device. If disconnected, a warning explains that the profile cannot be checked. **OK** continues switching; **Cancel** keeps the current profile and unsaved favorite edits. This also applies when creating or copying a profile, or choosing a replacement for a deleted active profile.

Each profile has two files in `%APPDATA%\PresetMaestro` on Windows, or `~/Library/Application Support/PresetMaestro` on macOS:

- `<profile>-favorites.json`: favorite slots, names, preset/scene mappings, and tags.
- `<profile>-settings.json`: device model and name, device assignment, and cached preset/scene names. Legacy mapping values are retained for compatibility; an assigned device's saved mapping takes precedence.

MIDI channel, display offset and Scene CC are saved in `FractalIndex/<device-id>.json` with the saved device data. Existing devices adopt the active profile's mapping when first opened. Copies sharing a device share its mapping; exported device snapshots include it.

`settings.json` keeps computer settings: MIDI input/output and thru ports, debug mode, entry options and note mappings, appearance, the available profile names (`Profiles`), and the last selected profile (`ActiveProfile`). Startup restores that profile. If its files are missing or unreadable, the app reports this and loads another readable profile, creating a new default profile if needed while preserving the original files.

If `settings.json` itself contains invalid JSON, startup preserves it as `settings.json.unreadable.<id>.bak`, discovers readable profile pairs, and starts with default computer settings.

**Scan profile files** checks `%APPDATA%\PresetMaestro` for matching `<name>-settings.json` / `<name>-favorites.json` pairs and refreshes the saved profile list. It scans saved files on this PC; **Sync device** reads device presets. The button shows “Scanning…” while file discovery runs in the background, with a progress bar for scans lasting longer than one second. A completion notification reports saved profiles found, newly discovered profiles, and incomplete or unreadable pairs skipped, including when there are no new profiles. The notification remains until dismissed. Adding files manually requires a scan; creating, copying, renaming, deleting and importing through the app update the list immediately.

**Export…** saves the selected profile as a ZIP containing its two JSON files, including cached names and favorites. **Import…** accepts that ZIP, or either file of a matching JSON pair in the same folder. Enter an optional new name in the import dialog; otherwise the source name is used. A number is appended when the name already exists, so existing profiles are preserved. Import selects the added profile for inspection without activating it; click **Use profile** when ready. Computer settings are never included in exports or replaced by imports.

Existing data migrates automatically to **Default** on first launch. The original settings are retained as `settings.json.pre-profiles.bak`; the original `favorites.json` is also retained. Deleted profile file pairs are recoverable from the `DeletedProfiles` subfolder.

## What happens automatically?

- After connecting, the app checks which preset and scene are active.
- Loading a preset through the app refreshes its eight scene names. Until the read finishes, cached names or `Scene 1`–`Scene 8` are shown.
- Hardware preset-change notifications trigger a refresh. A two-second polling timer also checks for changes when no notification arrives; reads can take longer and polling pauses during scans.
- Switching only the scene updates the highlighted button without downloading all names again.
- Successfully read names are saved for later sessions. Rapid preset changes cancel older reads so their names are not applied to the new selection.

For prompt notification of front-panel/footswitch preset changes over DIN MIDI, enable **Send MIDI PC** on the device. Both MIDI directions are required to request and receive names.

## Buttons you will use

The connection dialog requires a complete device sync when this profile has no complete saved scan for the selected device and connected model. Cached preset names alone do not satisfy setup. An unused **Default** device is assigned automatically. If Default already contains scan data or is assigned to another profile, choose **Add new device & sync** or confirm **Overwrite Default & sync**. Overwriting preserves the existing saved device data until every slot has been checked successfully. Cancelling or failing a read keeps setup open with **Resume**; closing setup disconnects. A successful complete sync opens Preset Index. Axe-Fx III capacity is established during the first sync.

| Where | Control | What it does |
|---|---|---|
| Config → Scenes | Scene buttons 1–8 | Changes the active scene using your configured Scene CC. Does not reload the preset. |
| Config → Scenes | Refresh current scene names | Re-reads the loaded preset's names. Use after renaming scenes externally or after a failed read. |
| Config → MIDI Connection → Sync options… | Sync device | Reads presets, scenes and amps from the connected device and saves them on this PC for the selected device. An accepted complete scan also refreshes this profile's preset and scene names. |
| Config → MIDI Connection → Sync options… | Sync scenes | Refreshes scene names for the presets used by this profile's favorites. |
| Sync with connected device | Cancel read | Stops the active read; completed entries remain cached. |
| Config → Scenes | Clear scene-name cache | Removes this MIDI port pair's locally cached scene names. Does not change the device or delete favorites. |
| Config → MIDI Connection → Sync options… | Sync names | Reads preset titles only. Favorites also has a quick Sync shortcut beside the preset picker. |
| Favorites → Edit Favorite | Named scene picker | Chooses a scene number for the favorite using cached labels. Saving the favorite does not select it on the device. |
| Favorites → Edit Favorite | Read names for this preset | Fetches scene labels for that favorite's preset without loading it on the device. |

## Favorites tables and search

The app opens on Config at its minimum width of 1000 logical pixels. Preset Sender, Favorites and Go to/double-click navigation from Preset Index or Amps use the same send confirmation near the top, centred just below navigation. It shows the sent selection without moving page content or keyboard focus. Successful confirmations disappear after seven seconds; hovering pauses that timer.

Favorites uses the full content width with adjacent bordered tables. Every table repeats **Slot | Name | Preset | Scene**. Tag pills follow each favorite title directly within the Name field, sharing the available space; hover for the full title or tag list. There are always at least two tables; wider windows add more. Favorites fill down each table in balanced groups, after filtering and global sorting. Resizing and filtering preserve the real slot numbers. All tables share one selection and one vertical scrollbar; their headings scroll together. Drag a heading separator to resize that field across every table.

Type in **Add tag or search term�** and press **Enter** to add a chip. An exact existing tag match (ignoring case) becomes a tag chip; any other phrase becomes a **Text:** chip. Spaces stay within the same chip. Suggestions include an explicit **Search text ���** choice for searching a tag name as ordinary text. Use arrow keys to select a suggestion, Enter to accept it, or Escape to dismiss the suggestions. Backspace in an empty input removes the last chip, and each chip has its own remove button.

**All** requires every tag chip; **Any** requires at least one. Text chips always combine with AND and search favorite names, tags, displayed numbers, and cached preset/scene names. The uncommitted input previews the same matching rule as Enter. **Clear search** removes every chip and restores All. Search never edits favorites or sends MIDI. Switching profiles resets this transient search state and uses the new profile's tags.

The details button switches between selected-row details and details for all favorites with cached names. Detail strips remain neutral and wrap long cached names within their table. Hover over a truncated favorite name or tag area for the full text. Selection uses the light blue/theme fill; the red edge independently marks the preset and scene reported by the hardware. Double-click sends the clicked nonempty favorite once. Empty slots remain editable. **Clear Slot** keeps its number; **Remove Slot and Shift Up�** removes it and shifts later slots after confirmation.

Up/Down moves within a table, Left/Right moves to the neighboring table and clamps to its last available row, Home/End moves to the first/last favorite, and Page Up/Down moves within the current table. Drag/drop across tables reorders the underlying slots only when search is clear and no header sort is active. Clicking a sorted heading cycles ascending, descending, and default slot order. The minimum window width is 1000 logical pixels; opening the editor raises it to 1320 to retain two readable tables and the editor.

### Legacy collection migration

Collections are retired. When old JSON is read, each nonblank `Category` becomes one ordinary tag: surrounding whitespace is trimmed, internal spaces are retained, and case-insensitive duplicates are avoided while preserving existing tag spelling and order. IDs, slots, names, and preset/scene values remain unchanged. Blank categories add nothing. This also applies to legacy initialization, profile loading, and ZIP or folder imports. New saves and exports omit `Category` and `CategoryOrder`.

Before an existing legacy JSON file is first rewritten, its original bytes are retained alongside it as `<filename>.pre-tags.bak`. Writes use temporary files and replacement; invalid legacy data prevents replacement. Imports leave their source files intact. Keep these backups if you might need to recover old collection information. Migration is idempotent across repeated saves, exports/imports, and profile switches.

Developer notes and the verification procedure are in [FAVORITES.md](FAVORITES.md).

## Choosing a preset for a Favorite

1. Open **Favorites** and add a new Favorite or edit an existing one.
2. Click **Select Preset…** beside its preset number. This opens a separate, resizable dialog.
3. Search by preset number or name, or browse the grid. It always lists device slots **000–511**, left-to-right across each row, then top-to-bottom. Filtering keeps that same order.
4. Click a preset and **Select Preset**, or press **Enter**, to use it in the Favorite draft. **Go to preset** or double-click auditions it on the connected device while keeping the picker open. **Escape** or **Cancel** leaves the Favorite draft unchanged.
5. Choose the scene and other Favorite details, then use the editor's existing **Save** button.

The dialog has five columns at its default size, fewer when narrowed, and a vertical scrollbar. Arrow keys navigate the grid; Up/Down from the search box moves focus into the grid. Home/End move within a row, Ctrl+Home/Ctrl+End go to the first/last result, Page Up/Down move by a page, and Ctrl+F returns to search. On Mac, use Command+Up/Command+Down for the first/last result and Command+F for search. The blue highlight and left-edge marker identify the selected preset.

Names come from the existing preset-name cache. Before syncing, unknown slots show `(name unavailable)` but can still be selected by number. On FM9, use **Config → MIDI Connection → Sync options… → Sync names**, or the editor's **↻ Sync** shortcut, to populate names, then reopen the picker. Both show progress and results in the shared sync dialog. A successful device check or accepted complete device sync also supplies preset names.

The picker returns the device slot and shows numbers using **Display Offset**: slot `000` is displayed as `001` when the offset is 1. **Select Preset** fills the Favorite draft without sending a program change or saving the Favorite. Explicit **Go to** actions and double-click send the chosen preset or scene using its configured mapping. The scene picker uses cached names for the draft's preset and offers **Go to scene**; going to a scene on another preset loads that preset first.

## Bulk sync versus everyday use

**Everyday use:** select a preset, then use its populated scene buttons. This uses small name queries and refreshes the loaded preset's names.

**Prepare everything upfront:** choose **Sync device** in the connection dialog, or **Sync device** on Preset Index. This reads presets, scenes and amps from the connected device into its saved data on this PC, and an accepted complete scan also refreshes the active profile's preset and scene names. A successful FM9 quick/full device check automatically refreshes all preset names using the shared fast name-read path; device content stays unchanged until you sync it. See [Using Preset Index](docs/fractal-index-usage.md) for progress, cancellation and resume.

A cancelled device scan retains completed reads. Choose **Resume** to continue an incomplete scan.

## Things worth remembering

- Every preset has **eight scenes**. An empty name does not mean the scene is unavailable.
- **Live names** come from the loaded preset and may include unsaved edits. **Stored names** come from saved preset data. Bulk sync does not save edits to the device.
- Renaming a scene while staying on the same preset is not automatically detected. Click **Refresh current scene names**.
- Protocol reads use internal slots **0–511** on FM9. With Display Offset 1, the controller displays **1–512**; for example, displayed preset 153 corresponds to internal slot 152.
- Scene names are cached per MIDI input/output port pair. If you swap between two devices with identical port names, clear the cache to avoid seeing the previous unit's labels.
- If a refresh fails, cached names remain visible and may be out of date. Check the message above the scene buttons and use Refresh after correcting the connection.

## Saved data

Computer settings are in `%APPDATA%\PresetMaestro\settings.json` on Windows or `~/Library/Application Support/PresetMaestro/settings.json` on Mac. Each `<profile>-settings.json` contains that profile's preset-name and scene-name caches, including when and how each scene-name entry was read. The app does not automatically expire old scene names.

## Build, run, and test

Run these Windows commands from the repository root with the .NET 10 SDK installed:

```powershell
dotnet run --project .\PresetMaestro\PresetMaestro.csproj
dotnet build .\PresetMaestro.slnx --configuration Release
dotnet test .\PresetMaestro.slnx --configuration Release
dotnet format .\PresetMaestro.slnx --verify-no-changes --no-restore
.\Publish-App.ps1
```

CI is configured for separate Windows and Apple Silicon Mac jobs, including builds, regression tests, formatting, optional-feature builds and retained coverage. Windows additionally verifies cross-platform package isolation; Mac builds a locally signed testing archive. The Mac job still needs its first native run. For local coverage, run `dotnet test .\PresetMaestro.slnx --configuration Release --collect:"XPlat Code Coverage"`. The [4 October 2026 code review](docs/code-review-2026-10-04.md) records earlier corrected failure paths and coverage evidence.

On the M2 Mac, install the Arm64 .NET 10 SDK and PowerShell 7, then run `dotnet build PresetMaestro.slnx -c Release`, `dotnet test PresetMaestro.slnx -c Release`, and `pwsh ./Publish-Mac.ps1`. This produces a self-contained `.app` and ZIP under `publish-macos/osx-arm64/<version>/`, reusing the recorded release version without incrementing it. Building on Windows with `./Publish-Mac.ps1` prepares the same bundle structure, but native launch, signing and hardware validation require macOS. Follow the [Mac preparation and release guide](docs/windows-macos-development.md) before distributing it.

Publishing automatically increments the patch version recorded in `PresetMaestro\Version.txt`: `1.2` → `1.2.1` → `1.2.2`. Ordinary builds and tests do not increment it. The app's displayed version and executable metadata are stamped before compilation, and the version file is updated only after publishing succeeds. This applies to both `Publish-App.ps1` and direct `dotnet publish` commands. Publishing with `--no-build` is rejected because an existing executable cannot be stamped with the new version.

To choose a specific version, run `.\Publish-App.ps1 -Version 1.3.0` or `dotnet publish .\PresetMaestro\PresetMaestro.csproj --configuration Release -p:PublishVersion=1.3.0` (`-p:Version=1.3.0` is also supported). A successful explicit publish becomes the starting point for the next automatic increment, so the next publish after `1.3.0` is `1.3.1`. Use three numeric components for explicit versions. These hooks follow [Microsoft's target ordering guidance](https://learn.microsoft.com/en-us/visualstudio/msbuild/target-build-order); the [dotnet publish documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) describes its build step and MSBuild property overrides.

`Publish-App.ps1` is tracked with the repository. It publishes the single-file executable `.\publish\PresetMaestro.exe`, then optionally hands off to the local, untracked `Copy-PublishedExe.ps1`. Use `-Distribute` to copy without a prompt, or `-NoDistribute` to publish only. A failed publish stops before distribution.

Run `.\scripts\Test-PublishVersion.ps1` to check automatic increments, explicit overrides, embedded version metadata and failure handling using isolated version files and output folders. These checks also run in Windows CI and do not alter the real version file or distribution copies. Direct `dotnet msbuild -target:Publish` is rejected; use `dotnet publish` so version preparation occurs before compilation.

The Windows x64 executable includes its .NET runtime and needs no separate .NET installation. Its build references only Avalonia's Windows and Skia backends and NAudio's MIDI/WinMM packages, avoiding unused desktop platforms, audio playback packages, and the WinForms runtime. Single-file compression reduces distribution size; ReadyToRun remains enabled and code trimming is not used. Compressed assemblies are unpacked on startup, so the executable's size is smaller than its extracted runtime payload. Mac builds have their own Avalonia.Native/CoreMIDI dependencies and a separate output directory. Publish checks reject native libraries or desktop backends belonging to the other OS or Linux.

The solution-level test command runs the maintained application and shared-protocol regression tests. The suite includes preset-catalog and headless Avalonia dialog tests for ordering, filtering, resizing, selection, and keyboard interaction. Scene tests use simulated MIDI, synthetic compressed presets and a captured FM9 replay fixture. Opt-in [physical device suites](docs/hardware-tests.md) cover read-only FM9 checks, multi-model compatibility reports and explicitly enabled preset/scene navigation. Normal tests and CI never send to hardware. Builds enforce the repository's `.editorconfig`, Microsoft recommended .NET analyzers, nullable reference types, and warnings-as-errors.

## Further reading

- [Scene-name behavior, troubleshooting, and protocol details](SCENE-NAMES.md)
- [Shared protocol code and integration rules](PresetNameSync.Core/README.md)
