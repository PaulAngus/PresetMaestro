# Preset Name Sync Probe

A separate, read-only SysEx probe application for checking preset and scene-name retrieval without changing presets. For normal controller use, see the [main README](../README.md).

## Build and run

From the repository root:

```powershell
dotnet build .\PresetNameSyncProbe\PresetNameSyncProbe.csproj
dotnet run --project .\PresetNameSyncProbe\PresetNameSyncProbe.csproj
```

## Why device editor must be closed first

device editor can hold the MIDI ports exclusively. If the probe cannot open the selected MIDI input/output ports, close device editor and any other MIDI application, then reconnect.

## Read-only behavior

The preset-name query uses the documented read-only command:

`F0 00 01 74 12 0D pp pp cs F7`

It also supports read-only current scene-name queries and stored-preset dumps, described below. It does not send Program Change, Bank Select, scene-select CC, preset-store, rename, or any other write/select command.

## One-slot test before full scan

1. Select MIDI Input and MIDI Output ports.
2. Click **Connect**.
3. Set **Preset Slot** (for example slot 0).
4. Click **Query Preset Name** and verify a response appears in the table and raw log.
5. If that succeeds, click **Scan All Preset Names** for a full 0-511 scan.

## Verify scene names before a bulk sync

1. Close the controller and device editor so the probe can use the MIDI ports. Connect the device's input and output in the probe.
2. On the device, load a preset whose scene names you recognize. Click **Read current scene names**. This ignores the probe's Preset Slot field and queries the preset currently loaded on the device.
3. Check all eight names in the **Raw MIDI / SysEx Log** against the device. Scene results appear in this log, not the preset-name table or CSV export.
4. Set **Preset Slot (0–511)** to a known saved preset. If the device display starts at 001, subtract one from its displayed number.
5. Click **Read stored slot scene names**. Verify the logged names against the saved preset. The device should stay on its currently loaded preset throughout the read.
6. If testing the same preset with both buttons, saved and live names can differ when the device has unsaved edits. Do not interpret that alone as a decoder error.
7. Once a stored read is correct, disconnect/close the probe, reconnect in the controller, and try **Sync all stored scene names**.

**Cancel Scan** also cancels a scene read. A timeout or validation error is shown in the status/log. Capture the raw log and the device firmware version when reporting a failure.

These controls use the documented `0x0E` live query and the community-documented `0x03` stored-preset dump format. Automated tests pass, but physical device verification remains to be performed. See [scene-name implementation details](../SCENE-NAMES.md) for the exact format and limitations.
