# Preset Maestro

A Windows desktop controller for selecting presets and scenes, saving favorites, and reading preset/scene names from the device. Built with .NET 10, Avalonia, and NAudio.

## Start here when you have forgotten how it works

1. Connect your device over USB, or connect both MIDI directions through a MIDI interface.
2. Open **Config**, select the MIDI input and output, and click **Connect**. If a port is busy, close device editor, the probe, or another MIDI application using it.
3. Check **MIDI Channel**, **Display Offset**, and **Scene CC#**. Scene CC defaults to **34** in this app; the device's Scene Select assignment must match it.
4. Open **Preset Sender** and select a preset. Under **Config → Scenes**, the panel shows all eight scenes. Cached names appear first, then the app reads the current names from the device. Preset Sender retains its original display/keypad layout.
5. Click a scene button to select that scene within the current preset.

You do **not** need to scan all presets before using scenes. For everyday use, just connect and select a preset.

## What happens automatically?

- After connecting, the app checks which preset and scene are active.
- Loading a preset through the app refreshes its eight scene names. Until the read finishes, cached names or `Scene 1`–`Scene 8` are shown.
- Hardware preset-change notifications trigger a refresh. A two-second polling timer also checks for changes when no notification arrives; reads can take longer and polling pauses during scans.
- Switching only the scene updates the highlighted button without downloading all names again.
- Successfully read names are saved for later sessions. Rapid preset changes cancel older reads so their names are not applied to the new selection.

For prompt notification of front-panel/footswitch preset changes over DIN MIDI, enable **Send MIDI PC** on the device. Both MIDI directions are required to request and receive names.

## Buttons you will use

| Where | Control | What it does |
|---|---|---|
| Config → Scenes | Scene buttons 1–8 | Changes the active scene using your configured Scene CC. Does not reload the preset. |
| Config → Scenes | Refresh current scene names | Re-reads the loaded preset's names. Use after renaming scenes externally or after a failed read. |
| Config → Scenes | Sync all stored scene names | Reads saved scene names for all 512 presets, without selecting those presets. Also collects their preset names. |
| Config → Scenes | Cancel scene sync | Stops the bulk scene scan; completed entries remain cached. |
| Config → Scenes | Clear scene-name cache | Removes this MIDI port pair's locally cached scene names. Does not change the device or delete favorites. |
| Config → Preset Name Sync | Sync Preset Names | Reads preset titles only. This is separate from scene-name sync. |
| Favorites → Edit Favorite | Named scene picker | Chooses a scene number for the favorite using cached labels. Saving the favorite does not select it on the device. |
| Favorites → Edit Favorite | Read names for this preset | Fetches scene labels for that favorite's preset without loading it on the device. |

## Choosing a preset for a Favorite

1. Open **Favorites** and add a new Favorite or edit an existing one.
2. Click **Select Preset…** beside its preset number. This opens a separate, resizable dialog.
3. Search by preset number or name, or browse the grid. It always lists device slots **000–511**, left-to-right across each row, then top-to-bottom. Filtering keeps that same order.
4. Click a preset and **Select Preset**, double-click an entry, or press **Enter**. **Escape** or **Cancel** leaves the Favorite unchanged.
5. Choose the scene and other Favorite details, then use the editor's existing **Save** button.

The dialog has five columns at its default size, fewer when narrowed, and a vertical scrollbar. Arrow keys navigate the grid; Up/Down from the search box moves focus into the grid. Home/End move within a row, Ctrl+Home/Ctrl+End go to the first/last result, Page Up/Down move by a page, and Ctrl+F returns to search. The blue highlight and left-edge marker identify the selected preset.

Names come from the existing preset-name cache. Before syncing, unknown slots show `(name unavailable)` but can still be selected by number. Use **Config → Preset Name Sync → Sync Preset Names** to populate names, then reopen the picker. A completed stored-scene scan also supplies preset names.

The picker uses device numbering even when **Display Offset** is 1: selecting `000` fills the Favorite's displayed preset field with `1`. Choosing a preset here does **not** send MIDI, change the device's current preset, or save the Favorite automatically. The scene picker refreshes from cached names for the chosen preset.

## Bulk sync versus everyday use

**Everyday use:** select a preset, then use its populated scene buttons. This uses small name queries and refreshes the loaded preset's names.

**Prepare everything upfront:** first verify one stored-preset read in the [probe](PresetNameSyncProbe/README.md#verify-scene-names-before-a-bulk-sync), then click **Sync all stored scene names**. This reads full saved presets to extract their names. It transfers roughly 12.6 MB and can take over an hour over DIN MIDI; USB timing depends on the device. You can cancel at any time.

A preset change interrupts the bulk scan. A timeout or invalid dump stops it and leaves completed entries cached. Clicking sync again starts at slot 0; automatic resume is not implemented.

## Things worth remembering

- Every preset has **eight scenes**. An empty name does not mean the scene is unavailable.
- **Live names** come from the loaded preset and may include unsaved edits. **Stored names** come from saved preset data. Bulk sync does not save edits to the device.
- Renaming a scene while staying on the same preset is not automatically detected. Click **Refresh current scene names**.
- The probe always uses internal slots **0–511**. With Display Offset 1, the controller displays **1–512**; for example, displayed preset 153 corresponds to probe slot 152.
- Scene names are cached per MIDI input/output port pair. If you swap between two devices with identical port names, clear the cache to avoid seeing the previous unit's labels.
- If a refresh fails, cached names remain visible and may be out of date. Check the message above the scene buttons and use Refresh after correcting the connection.

## Saved data

The settings file is `%APPDATA%\PresetMaestro\settings.json`. It contains the preset-name cache and scene-name caches, including when and how each scene-name entry was read. The app does not automatically expire old scene names.

## Build, run, and test

Run these commands from the repository root with the .NET 10 SDK installed:

```powershell
dotnet run --project .\PresetMaestro\PresetMaestro.csproj
dotnet build .\PresetMaestro.slnx --configuration Release
dotnet test .\PresetMaestro.slnx --configuration Release
dotnet format .\PresetMaestro.slnx --verify-no-changes --no-restore
dotnet publish .\PresetMaestro\PresetMaestro.csproj --configuration Release
```

Publishing creates the single-file executable `.\publish\PresetMaestro.exe`.

The solution-level test command runs both the controller and probe suites. The controller suite includes preset-catalog and headless Avalonia dialog tests for ordering, filtering, resizing, selection, and keyboard interaction. Scene tests use simulated MIDI and synthetic compressed presets; **physical device scene-name verification is still outstanding**. Builds enforce the repository's `.editorconfig`, Microsoft recommended .NET analyzers, nullable reference types, and warnings-as-errors.

## Further reading

- [Scene-name behavior, troubleshooting, and protocol details](SCENE-NAMES.md)
- [Read-only hardware verification with the probe](PresetNameSyncProbe/README.md)
- [Shared protocol code and integration rules](PresetNameSync.Core/README.md)
