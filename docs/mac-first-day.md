# First day with the M2 Mac

Prepared 11 October 2026 for the current Preset Maestro 1.2.13 working tree. The supplied app includes the latest preset/scene views, scene search, search-entry behavior and connection/sync changes. Native Mac launch, visual checks and physical MIDI acceptance remain pending.

## Get the current source onto the Mac

Transfer both archives from this preparation session:

- `PresetMaestro-1.2.13-osx-arm64.zip`: the self-contained application, cross-built on Windows.
- `PresetMaestro-1.2.13-source.zip`: a snapshot of the actual current main application source, including local changes and new files that have not been committed. It contains `PresetMaestro/HANDOFF.json` with file hashes and provenance.

Double-click the source ZIP in Finder to extract the `PresetMaestro` folder, for example into Downloads. This source snapshot is the simplest starting point for tomorrow: cloning the current Git commit alone omits the newer local features. The independent Preset Sender utility and its Windows MIDI Services preview package are outside this main-app handoff.

## Install the developer tools

1. Use macOS 15 or newer. Install the **macOS Arm64 .NET 10 SDK** using the [Microsoft installer](https://learn.microsoft.com/en-us/dotnet/core/install/macos). Select SDK, rather than Runtime.
2. Install a stable **Arm64 PowerShell 7** package compatible with the Mac's installed OS, following [Microsoft's instructions](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-macos).
3. Open Terminal (press Command+Space, type Terminal, press Return) and install [Apple's Command Line Tools](https://developer.apple.com/documentation/xcode/installing-the-command-line-tools/):

```sh
xcode-select --install
```

Accept the installation dialog and let it finish. The command-line package supplies the tools needed for this .NET/Avalonia app and Git. Reopen Terminal after installing .NET and PowerShell, then check:

```sh
dotnet --version
pwsh --version
git --version
```

## Run the native readiness check

In Terminal, change into the extracted source folder. For a folder in Downloads:

```sh
cd ~/Downloads/PresetMaestro
pwsh ./scripts/Test-MacReadiness.ps1
```

This checks the OS, native Arm64 tools and SDK, builds the current solution, runs regression tests including native CoreMIDI loading/enumeration, verifies formatting, and creates and checks a locally signed `.app` and ZIP. Physical MIDI tests are excluded regardless of inherited diagnostic environment variables. The app's recorded version is preserved.

Results and a transcript are written beneath `build_test/mac-readiness-<timestamp>/`. A successful run writes `readiness.json`. If a step fails, keep that folder so the exact error can be investigated on the Mac. Application libraries are restored automatically during the build; app users receive those libraries and the .NET runtime in the finished bundle.

Open the freshly rebuilt app:

```sh
open "publish-macos/osx-arm64/1.2.13/Preset Maestro.app"
```

The Windows-built app ZIP is useful as a transfer/package check. Rebuilding locally is the preferred first run because the Mac script signs the app with Apple's native tools. Public download signing/notarization is a later release step; follow [the release guide](windows-macos-development.md).

## Check the application before connecting hardware

- Confirm version 1.2.13, the icon, launch from Finder, normal quit and relaunch.
- Check Config, Preset Sender, Favorites, Preset Index's **Presets and Scenes** views, scene details, search chips and Amps in Light and Dark. Check the supported minimum window size and normal size, Retina text, scrolling and long labels.
- Use Command+F in the preset picker, Command+Up/Down for first/last preset, Escape to dismiss temporary UI, and keyboard navigation between results. Confirm closing details returns focus appropriately.
- Switch between Presets and Scenes with different searches and selections. Confirm each view restores its own search, selection and scroll state and browsing does not send a preset change.
- Import a Windows profile and check its favorites, device assignment, tags and cached names. Confirm restart restores data under `~/Library/Application Support/PresetMaestro`, outside the app bundle.

## Connect and check the FM9

Close other MIDI editors initially, connect the FM9 by USB, refresh ports in Config, choose the matching input/output and connect. Check identity, preset/scene-name reads and the connection/sync workflow. Check scene/preset browsing and explicit Go to/Send actions, then cancel/resume a scan. Finally check controller thru, disconnect/reconnect, USB unplug/replug and sleep/wake.

The detailed [hardware guide](hardware-tests.md) has optional diagnostic scripts. Run physical tests deliberately after ordinary launch and port discovery work; navigation tests change the device's active preset/scene. Record any failure with the readiness results, the action taken and a screenshot or MIDI log.
