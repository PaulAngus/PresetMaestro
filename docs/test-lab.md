# PresetMaestro Test Lab adapter

The physical Mac at `192.168.254.39` must never receive additional virtual MIDI
ports, IAC buses or MIDI components. The default Native/Desktop bundles below use
virtual peers and are for the development VM. Build the separate physical profile
with `--config packaging/test-lab-physical-mac.json` for that host. Its Native stage
uses only the existing real FM9 input/output selected in `physical-midi.json`;
its eight desktop scenarios disable MIDI in the application process. Virtual-peer
source files are excluded from that driver. The physical host's initial control
and recording grants apply to `Maestro Physical Desktop Tests.app`. Keep its
signed native launcher unchanged at the approved stable path for repeated runs.

Build on a development PC with .NET 10 and Python:

```
python packaging/build_test_lab.py --rid win-x64 --output artifacts/test-lab
python packaging/build_test_lab.py --rid osx-arm64 --output artifacts/test-lab
```

The build uses the pinned agent in `dependencies/test-lab`, and produces a portable ZIP, SHA-256, immutable file manifest and source identity. Each extracted bundle is independent of this checkout. Test machines need no SDK, separately installed runtime, Git or Python. The optional `--connection` argument includes only the public dashboard connection JSON. Use a fresh output folder for each build. Test-bundle builds retain the recorded product version using an isolated version file.

On the development Mac, extract locally and run `bash Prepare-on-Mac-DEV.command` to sign every native component and application bundle, verify, reseal and check the bundle before distributing it. This is ad hoc signing; Developer ID/notarization remains separate. Native execution still needs validation on each target OS.

Copy the matching bundle to each machine, copy `dashboard-connection.json` into its root, open Pair-with-Dashboard, enter a dashboard code, then Start-Agent. Pairing is per application and OS user; upgrading a bundle retains it. The application ID is `preset-maestro`. The runner accepts `run --root ROOT --suite Functional --result-directory ROOT/results/RUN --no-open` and the standard All, Native and Desktop suites.

Functional wraps the existing offline .NET tests and retains case counts and logs. Hardware-category tests are excluded even if hardware opt-in environment variables are present. Skipped or empty coverage cannot pass.

Native uses a dedicated, temporary virtual MIDI peer and exercises discovery, bank/program boundaries, channel routing, favorite/scene sends, exact SysEx, native input and port close/reopen behavior. Windows requires Windows MIDI Services with virtual-port support; Mac uses CoreMIDI virtual endpoints. An unavailable native prerequisite produces an incomplete result.

Desktop launches the packaged application with a private `PRESET_MAESTRO_DATA_DIRECTORY`, a synthetic FM3 peer and a seeded saved library. It tests connection and dismissal of the sync prompt, real keyboard preset entry, navigation, favorite bank/program/scene messages, narrow/normal window sizes, disconnect and clean shutdown in both light and dark themes. Windows uses UI Automation and native keyboard input. Mac uses public Accessibility and CoreGraphics APIs and launches `bin/Maestro Desktop Tests.app` through LaunchServices so its own permissions apply. The Mac pilot watches its wrapper process and stops the owned application if that wrapper exits.

Keep the test desktop unlocked and idle. On Mac, add **Maestro Desktop Tests.app** to System Settings > Privacy & Security > **Device Control and Data Access** (macOS 27; **Accessibility** on earlier versions) and **Screen & System Audio Recording**. Select the `.app` bundle, rather than its executable. A missing permission produces an incomplete result before desktop scenarios begin. Ad hoc signing binds these permissions to the exact signed app; replacing a development build may require removing and adding its permission entry again. The renamed control permission is documented in [Microsoft's macOS 27 guidance](https://learn.microsoft.com/en-us/purview/endpoint-dlp-macos-27-changes). See [Apple's screen recording settings guide](https://support.apple.com/en-gb/guide/mac-help/mchld6aa7d23/mac).

All requires Functional, Native and Desktop to pass. These synthetic-peer scenarios do not establish physical Fractal-device interoperability, preset-library sync qualification or installation/upgrade qualification.

All runners, including direct local runs, hold a machine-wide resource lease throughout test execution. Other applications wait and their agents continue heartbeats. Upgrade Preset Sender and the dashboard to agent 0.2.0 before mixing applications. Permission errors accessing the global lease stop the run; they never bypass it.
