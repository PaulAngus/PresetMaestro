# Quick-start verification note

**5 October 2026 · PresetMaestro 1.0.4 · Quick-start draft awaiting user review**

## Build

Republished the current working tree successfully in Release configuration, as requested. Used `publish/PresetMaestro.exe`: product version **1.0.4**, file version **1.0.4.0**, SHA-256 `07A06F5E41E000F677EB915D553CB92C678AD17A0847379E813DC66DEC4BB3AA`. Publishing reused up-to-date outputs. Base commit: `711f5f4`, with pre-existing uncommitted changes; version alone does not identify this build. The [GitHub releases page](https://github.com/PaulAngus/PresetMaestro/releases) listed no public releases. No application code was changed for this task or executable copied to optional distribution destinations.

## Checked

- Walked the actual published UI: fresh launch, USB port selection, connection, required sync, preset/scene pickers and **Go to** actions, naming/saving **Clean intro**, and double-click recall. Also inspected existing-library **Skip for now** and library mapping controls.
- Physical device: **FM9, firmware 12.00**, `FM9 MIDI In` / `FM9 MIDI Out`. Fresh sync completed in **192.5 seconds**: **512 slots**, **254 populated presets**, **zero errors**, then automatically opened Preset Index. The initial full scan really is required in this build.
- Saved **Clean intro** as preset **000**, scene **7**. After loading preset 001 through Preset Sender, recalled the favourite. Checked **SENT**, the hardware-reported active marker, **Use Current** readback of 000/7, and the saved favourite's numbers.
- Restored the pre-navigation selection (**009 Plexi 100W**, scene **3 — 1970**) and confirmed readback. Restored all **11 original local data files**, verifying hashes. Test data remains under `build_test/quick-start-verification/test-data`; the example was not left in the original profile.
- **179/179 existing regression tests passed** for first-library setup, favourite editing, pickers, double-click and navigation. These use simulated devices. TRX: `build_test/quick-start-verification/quick-start-verification.trx`.
- Compared existing documents with current source and UI. Removed obsolete Config → Scenes instructions, outdated picker labels and the blanket no-scan claim. Five unaltered screenshots show the real published UI. Inspected Light Config/library controls at 1000-pixel width and the Dark first-use workflow, including the editor widening to 1320 pixels.
- Checked driver requirements and hardware settings against the [FM9 downloads page](https://www.fractalaudio.com/fm9-downloads/) and [FM9 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf). The manufacturer's linked manual covers older firmware; physical firmware-12 setup menus were not inspected.

## Limits and next review

FM9 USB is the verified path. FM3 and Axe-Fx III identification/numeric sending and candidate library decoding exist, but production live name reads and scene tracking are disabled; their amp catalogues are unpopulated and hardware was not tested here. Axe-Fx III library capacity depends on the selected revision. Preserve these distinctions in the manual; see `docs/hardware-tests.md`.

Driver installation, cable reconnection, physical front-panel actions and listening remain user checks. Recall was tested after an app-driven change, not a front-panel change. Failure/recovery and occupied-Default branches were checked against source/tests rather than induced on hardware.

The revised quick-start starts with a sound already selected on the FM9, uses the previously checked **Use Current** action to capture its preset and scene, then saves and recalls the named favourite. It defines terms and includes prerequisites. All four linked screenshots resolve. Its approximate two-page target uses text length with screenshots linked separately; printed pagination is not fixed. User review of the complete quick-start is pending before creating `docs/user-manual.md`.
