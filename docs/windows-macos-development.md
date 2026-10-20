# Windows and macOS development

Preparation status: refreshed 11 October 2026 for the current 1.2.13 working tree, including the newer preset/scene views, search and connection/sync behavior. The macOS implementation can be cross-built and its managed logic tested on Windows. Native launch, CoreMIDI and Apple signing still require a Mac run; the M2 acceptance checklist below is a release gate. Linux is not an application target. Start with the [first-day guide](mac-first-day.md) and the current source snapshot so uncommitted features reach the Mac.

## Architecture and dependency isolation

One UI, profile format and device-protocol implementation serve both systems. `build/DesktopPlatform.props` selects one platform before NuGet restore. A supplied runtime identifier takes precedence over the host; ordinary builds choose the host. Separate `obj/Windows`, `obj/macOS`, `bin/Windows` and `bin/macOS` paths prevent restore and generated-code collisions when switching targets. `.gitattributes` makes C# line endings match the existing formatter on both Windows and Mac checkouts.

| Build | Framework | UI backend | MIDI backend | Release |
|---|---|---|---|---|
| Windows x64 | .NET 10 / Windows 10 API contract 19041 | Avalonia.Win32 | NAudio WinRT input / WinMM output | Compressed, self-contained EXE with ReadyToRun |
| Apple Silicon | .NET 10, `osx-arm64` | Avalonia.Native | DryWetMIDI 8.0.3 / CoreMIDI | Self-contained `.app` in ZIP |
| Intel Mac | .NET 10, `osx-x64` | Avalonia.Native | Same Mac backend | Separate `.app` in ZIP |

The Mac bundle declares macOS 26 minimum. The product support policy is macOS 26 and 27, Windows 10 and Windows 11; the framework's broader compatibility table does not define the application's supported OS policy. Clean-host qualification remains incomplete. Windows 10 virtual MIDI presentation is unsupported.

Platform source files and package references are conditional. Neither release references `Avalonia.Desktop`, the NAudio umbrella package, or a Linux UI backend. Windows does not reference DryWetMIDI or Avalonia.Native; macOS does not reference NAudio, Avalonia.Win32 or the Windows SDK projection. Some shared NuGet packages contain assets for multiple systems; the SDK selects only the requested RID for publication. Downloaded package contents are not the shipped payload.

DryWetMIDI 8.0.3's default build target copies both its Windows DLLs and Mac dylib. We exclude that target and explicitly copy only `Melanchall_DryWetMidi_Native64.dylib`. Its published binary contains ARM64 and x64 slices. Native Mac packaging thins universal libraries to the requested architecture before signing. Cross-packaging on Windows retains universal Mac libraries until that native packaging step; it never includes Windows MIDI libraries. The new library's MIT license accompanies the Mac app.

`build/PlatformPublish.targets` checks both `ResolvedFileToPublish` and `FilesToBundle` before single-file generation, and also checks ordinary folder publication. Wrong-platform UI/MIDI libraries or Linux native libraries fail the publish. The pre-bundle file list is saved as `publish-payload.txt` under the app's platform/configuration/RID intermediate directory. Trimming and Native AOT are not enabled: they require additional compatibility work and are unnecessary for this port. This follows [Avalonia's platform-boundary guidance](https://docs.avaloniaui.net/docs/app-development/native-interop) and [.NET's single-file deployment model](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

## MIDI boundary

`IMidiManager` remains the UI/protocol boundary. `MidiManager` owns port lifetime, output serialization, bank/program/scene encoding, note notifications, stale-callback rejection and MIDI thru. The small internal input/output-port interfaces carry complete MIDI 1.0 messages without a platform-library type in their signatures.

The Windows adapter keeps WinRT input and WinMM output. The Mac adapter uses the maintained library's native CoreMIDI implementation, rather than introducing application-owned unmanaged callbacks and packet-layout definitions. It disposes enumeration handles, waits for complete SysEx, uses **device** byte format (not MIDI-file framing), catches exceptions at callback boundaries, and serializes conversion/disposal. Shared routing continues to ignore clock/active-sensing traffic and does not forward SysEx from controller thru inputs. Input parser errors reach the MIDI log.

Primary library references: [device ownership and disposal](https://melanchall.github.io/drywetmidi/articles/devices/Overview.html), [input lifecycle](https://melanchall.github.io/drywetmidi/articles/devices/Input-device.html), [wire conversion](https://melanchall.github.io/drywetmidi/api/Melanchall.DryWetMidi.Core.BytesFormat.html), and [the exact package's build rules](https://github.com/melanchall/drywetmidi/blob/v8.0.3/DryWetMidi/Melanchall.DryWetMidi.csproj).

## Build and verify on Windows

Run validation locally from the repository root with the .NET 10 SDK and PowerShell 7. GitHub Actions is disabled for this repository and no hosted workflows are used.

```powershell
dotnet build PresetMaestro.slnx -c Release
dotnet test PresetMaestro.slnx -c Release --no-build --filter 'Category!=Hardware&Category!=Compatibility' --collect:"XPlat Code Coverage" --logger trx --results-directory TestResults
dotnet format PresetMaestro.slnx --verify-no-changes --no-restore
./scripts/Test-PublishVersion.ps1
./scripts/Test-PlatformBuilds.ps1
dotnet build PresetMaestro/PresetMaestro.csproj -c Release -p:EnableFractalIndex=false --artifacts-path build_test/no-index
```

Test results and coverage stay in `TestResults`; build and packaging checks use local output directories. Run the commands individually and stop if one fails. Physical MIDI tests are separate: follow [hardware testing](hardware-tests.md) with an explicitly selected device, MIDI channel and Scene CC.

The platform script publishes real Windows and Mac artifacts into an isolated test directory, checks architecture and ZIP permissions, deliberately injects wrong-platform libraries to prove both publish guards fail, and checks unsupported/mismatched targets. It preserves the source version and never invokes the local Windows distribution helper.

To exercise the Mac-managed dependency graph on Windows, without executing CoreMIDI:

```powershell
dotnet test PresetMaestro.slnx -c Release -p:MaestroPlatform=macOS -p:PlatformTarget=AnyCPU
```

`AnyCPU` here lets Windows execute the managed test code; it does not validate Mac native binaries. The native CoreMIDI smoke test explicitly skips on Windows. Native Mac builds need no such override. Tests based on Windows mandatory file-sharing locks explicitly skip on macOS, whose file-replacement semantics differ; other shared persistence and rollback checks run normally.

On the Apple Silicon Mac, run `pwsh ./scripts/Test-MacReadiness.ps1` locally. It builds, tests, loads/enumerates CoreMIDI without sending to hardware, formats, packages, locally signs and verifies signatures. Test results, coverage, the readiness log and the testing ZIP remain on that Mac. Physical MIDI test categories are explicitly excluded even if diagnostic environment variables are set. Also run the optional-module build locally: `dotnet build PresetMaestro/PresetMaestro.csproj -c Release -p:EnableFractalIndex=false --artifacts-path build_test/no-index`.

## First setup on the M2

1. On the development/signing Mac, use macOS 26 or 27 and install the [.NET 10 Arm64 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/macos). These are build tools; application users do not install them.
2. Install Apple's command-line tools with `xcode-select --install`, and [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-macos). PowerShell is only a developer packaging tool; app users do not need it.
3. Extract the current source snapshot (or clone a commit that includes all the latest changes), open Terminal in it and run:

```sh
pwsh ./scripts/Test-MacReadiness.ps1
open "publish-macos/osx-arm64/1.2.13/Preset Maestro.app"
```

The readiness check is for Apple Silicon. For packaging alone, `Publish-Mac.ps1` defaults to Apple Silicon; `-Architecture x64` prepares a separate Intel app that needs Intel/Rosetta validation before being advertised as supported. Outputs are `publish-macos/osx-arm64/<version>/Preset Maestro.app` and the matching ZIP. Windows continues to publish `publish/PresetMaestro.exe`, preserving the existing local copy helper. Each Mac publish starts in a fresh staging directory, so stale binaries cannot leak into a release.

`Publish-Mac.ps1` reuses `PresetMaestro/Version.txt`, or an explicit `-Version 1.2.3`, through a temporary version file. It never advances the source version: use the same explicit release number when preparing both OS releases. Direct `dotnet publish` retains the existing version-increment behaviour, so use the wrapper for Mac releases.

For just the directly runnable `.app`, add `-SkipArchive`. This also removes the matching ZIP from an earlier generated release. It works with Developer ID signing and `-NotaryProfile`; notarization uses a temporary upload ZIP that is removed when publishing finishes.

The app-only publish path passed `scripts/Test-PlatformBuilds.ps1`, including replacing an archived release, removing its stale ZIP, cleaning staging and preserving Windows publish validation (`build_test/macos-app-only-validation.log`). The Developer ID identity is now valid after installing Apple's G2 intermediate, but the first signing attempt returned `errSecInternalComponent` (`build_test/macos-developer-id-signing.log`). The previous local ad-hoc app remains in place. Follow [Apple's signing access troubleshooting](https://developer.apple.com/forums/thread/712005): run the publish command from Terminal and authorize `codesign` to use the private key when prompted. Successful Developer ID signing and notarization are still pending.

On macOS, a normal package is ad-hoc signed for local testing. Public download releases use:

```sh
pwsh ./Publish-Mac.ps1 -Version 1.2.3 \
  -SigningIdentity 'Developer ID Application: YOUR TEAM (TEAMID)' \
  -NotaryProfile 'presetmaestro-notary'
```

Create the Developer ID certificate and store notarytool credentials in the Mac keychain first; `NotaryProfile` is the keychain profile name, never a password. The script signs native files individually, signs the enclosing app last, verifies it, submits with `notarytool --wait`, staples and validates the ticket, assesses the app and recreates the ZIP. `--deep` is used only for **verification**, never signing. Only the JIT entitlement required by .NET/Avalonia is requested. This is direct-download distribution, not a sandboxed Mac App Store submission. See [Avalonia macOS packaging](https://docs.avaloniaui.net/docs/deployment/macos) and [Apple Developer ID/notarization](https://developer.apple.com/developer-id/).

Windows can also run the packaging script to produce an unsigned preparation ZIP. ZIP Unix permissions preserve the executable bit; native signing, launch and hardware checks are still pending. Prefer rebuilding and signing on the M2 for its first run.

## Refresh verification — 11 October 2026

The current 1.2.13 working tree, including uncommitted preset/scene/search changes, was tested with .NET SDK 10.0.303 on Windows:

- Windows Release: **829 passed, 5 physical tests skipped, 0 failed**.
- Mac-managed Release (`MaestroPlatform=macOS`, `PlatformTarget=AnyCPU`): **837 passed, 6 skipped, 0 failed**. The native CoreMIDI check and five physical tests require the Mac/hardware.
- Both dependency graphs pass formatting; both targets build without Preset Index with zero warnings/errors.
- Actual Windows and Apple Silicon publishes pass payload isolation. Injected other-OS files, Linux and mismatched targets, wrong native architecture, a Windows native MIDI DLL in the Mac app, and missing ZIP execute permissions are rejected.
- The current Windows EXE is **76,189,566 bytes**, the same size as the existing current Windows release. The Mac ZIP is **45,076,248 bytes**. Neither dependency graph contains the other OS's MIDI/UI packages or the separate Preset Sender utility's Windows MIDI Services preview package.
- Inspected current Mac-managed headless preset/scene views on Windows at 1000/1440 logical pixels in Light/Dark, including selected scenes, details and sync actions. Actual Mac fonts, window chrome, Retina behavior and hardware remain pending.
- The new readiness script parses successfully, rejects a Windows host, and its test filter excludes physical tests even when opt-in hardware environment variables are set. Native Mac execution remained pending at this preparation stage; validation now runs locally.
- Source version and the existing Windows release retain their original hashes. Results, screenshots and the starting-worktree record are under `build_test/macos-refresh-20261011/`.

The transfer includes a source snapshot of the current working tree, rather than only Git HEAD. Its `HANDOFF.json` records the base commit, changed/untracked main-app paths and hashes of the included files. See [the first-day guide](mac-first-day.md).

## Original preparation verification — 8 October 2026

Performed on Windows with .NET SDK 10.0.303:

- Windows Release suite: **782 passed, 5 hardware tests skipped, 0 failed**. Baseline before the port: 767 passed and 5 skipped.
- Mac-managed Release suite using the `AnyCPU` command above: **790 passed, 6 skipped, 0 failed**. Skips are the five hardware tests and native CoreMIDI loading. This is not a native Mac run.
- Both dependency graphs pass formatter/code-style verification. Both app targets build with Preset Index disabled, with zero warnings/errors and isolated outputs even when sharing one `--artifacts-path` directory.
- Publish-version regression checks pass, including failed publication and distribution-helper behavior. The source version remains **1.2.11**; existing distribution copies were not replaced.
- Real Windows and Apple Silicon production publishes pass platform isolation checks. Injected wrong-OS files fail both publishes. Linux/mismatched targets, wrong native architecture, a Windows DLL inside the Mac bundle and a ZIP without executable permissions are rejected.
- Apple Silicon and separate Intel Mac bundles pass version, dependency, native architecture and ZIP permission checks. The M2 preparation archive is `publish-macos/osx-arm64/1.2.11/PresetMaestro-1.2.11-osx-arm64.zip` (45,065,970 bytes). Native signing and launch remain pending.
- The Windows restored dependency set exactly matches the original. The new production EXE is **76,137,517 bytes**, versus the existing 76,131,558-byte release: **5,959 bytes larger**, with no Mac platform dependencies.
- Inspected Windows headless renders in Light/Dark at 1000/1440 logical pixels, including Config, numeric input, Favorites confirmation and the preset picker. No clipping or font regression was observed in those views. Images and TRX results are under `build_test/macos-prep/`.

Native Mac validation had not run in this preparation session. Run the local Mac readiness command for current evidence. No physical MIDI tests were enabled.

## M2 acceptance checklist — pending

- Launch the `.app` from Finder and Terminal, then from a fresh extracted ZIP. Confirm the icon, version, native window controls, default menu/quit behaviour and clean shutdown.
- Inspect light and dark themes at 1000-pixel minimum width, normal width and the wider favorite editor. Check actual Mac fonts, Retina scaling, text clipping, file dialogs, accessibility names, VoiceOver, keyboard focus and dismissal/focus return. See [cross-platform UX decisions](cross-platform-ux.md).
- Verify data is saved under `~/Library/Application Support/PresetMaestro`, persists across restarts and never writes inside the app bundle. Import/export a Windows profile with non-ASCII text; repeat on a case-sensitive APFS volume if available.
- Discover FM9 input/output; connect, identify firmware/device name, read preset/scene names, scan/cancel/resume, navigate presets/scenes and confirm reported state. Confirm original device data is not modified by read operations.
- Test controller thru, channels, note mappings, concurrent replies/forwarding, repeated disconnect/reconnect, USB unplug/replug, sleep/wake and an occupied/unavailable endpoint. Compare MIDI traces to Windows.
- Run the opt-in hardware suite using the [hardware test guide](hardware-tests.md); the PowerShell scripts work with `pwsh` on the Mac and select the native backend automatically.
- For a public release, use real Developer ID/notarization credentials and test a **downloaded** ZIP on a clean Mac account. A local code-signature check does not reproduce Gatekeeper's download/quarantine path.

Passing cross-build and managed tests is preparation evidence, not native macOS or hardware acceptance.

## Native signing correction — 12 October 2026

The first Mac package failed with `code object is not signed at all` for `System.Xml.XPath.XDocument.dll`. The script signed only Mach-O binaries while leaving unsigned managed assemblies in `Contents/MacOS`, a directory Apple treats as nested code. Native packaging now moves managed assemblies and runtime metadata to `Contents/Resources/Managed`, with relative links in MacOS preserving the published .NET paths. Native libraries remain signed in MacOS, with links back from Managed: the .NET host resolves the main assembly's real path and needs its self-contained runtime libraries available there. The first runtime probe caught that requirement; the final signed apphost successfully loads .NET, CoreMIDI and native Avalonia.

This follows [Apple TN2206's code/resource placement and inside-out signing rules](https://developer.apple.com/library/archive/technotes/tn2206/_index.html). Native components are thinned and signed before the enclosing app; the final app signature applies the JIT entitlement to the main executable. `--deep` is used only for verification. Data is sealed as resources, avoiding the extended-attribute signatures Apple discourages for non-Mach-O files. Relative links stay entirely inside the bundle. The unsigned Windows-created preparation package keeps its existing flat layout; native publication creates the signed resource layout. Windows application publication and dependencies are unchanged.

`Test-MacBundle.ps1 -VerifySignature` checks the app with `codesign --verify --deep --strict`, extracts its ZIP with ditto, and checks the extracted app as well. It checks the links required for self-contained bootstrap. `Test-PlatformBuilds.ps1` adds Mac-only negative checks that modify a managed resource in an isolated app and ZIP; both signatures must be rejected. It also verifies that hidden staging directories are removed. `Get-Item -Force` corrects the cleanup failure on hidden `.build-*` directories, preserving the useful signing error instead of masking it.

Verified on Apple Silicon, macOS 27.0.1, .NET SDK 10.0.401 and PowerShell 7.6.6: local ad-hoc signing and strict verification pass, including ZIP extraction. A temporary startup hook initializes the published app's native Avalonia and enumerates CoreMIDI, then exits before starting the UI or opening profiles. This checks the packaged runtime paths without altering user data. The source version remains 1.2.13. The testing artifacts are `publish-macos/osx-arm64/1.2.13/Preset Maestro.app` and its ZIP. Public Developer ID signing, notarization and downloaded/quarantined acceptance still require release credentials and a separate run; local ad-hoc validation does not establish those results.

The final platform publishing regression script passed Windows and Mac production publishes, both platform payload guards, Linux/mismatched-target rejection, wrong-architecture/library checks, ZIP execute permissions, signed-resource/ZIP tampering rejection and staging cleanup. Evidence: `build_test/macos-signing-final.log`. No Developer ID identity or notarization credentials were used.

## MacBook Air through MIDI Forwarder — 20 October 2026

Verified the real Preset Maestro workflow on the Apple Silicon MacBook Air,
macOS 27.0.1, using `Network RTP Session 1` for both MIDI directions through the
running MIDI Forwarder client and its Windows server to **FM9 · Paul's**, firmware
**12.00**. The app's normal Connect, Sync device, Sync names and Resync Scenes
button handlers were exercised by an in-process .NET startup-hook harness against
the published application, with native Avalonia renders and structured sync logs.
This did not use a synthetic MIDI peer or replace the application's MIDI backend.

The first run exposed a cache-scoping defect: when MIDI ports were selected after
launch, initial library sync preceded scene tracking and cached scene labels under
the old port pair. The complete device library was saved correctly, but Favorites
could lack labels for the selected ports. Connection now establishes the scene
cache key immediately after device validation, before any initial sync. The new
first-connection persistence regression fails on the previous code and passes with
the fix. All 48 focused connection, scene-client and scene-search checks pass on
both Windows and the Mac-managed dependency graph.

Version **1.2.29** passed the repeated native workflow: **512 slots checked,
254 populated presets, 258 empty slots skipped and 2,032 scene entries**, with all
eight scene entries present for each populated preset. The full read took about
**115 seconds**, with no sync errors, MIDI timeouts/failures or dropped diagnostic
records. All 512 preset names refreshed separately, and the selected preset's
eight saved scenes refreshed through Resync Scenes. Disk verification confirms
the complete library and all 512 scene-cache records under the active relay port
pair. The hardware stayed at slot 169, scene index 0 throughout these reads.

The corrected Windows EXE and ready-to-run Mac `.app` are published to the
MightBeUseful network directory. The Mac bundle uses the SDK-selected matching
self-contained runtime and ARM64-compatible native libraries; its managed-resource
links and local signatures were prepared and verified on the Air. Universal
third-party native libraries were retained rather than thinned. The network app
is checked again after publication by reconnecting and comparing a sample of
saved presets and scenes. Local ad-hoc signing is used for this network-folder
deployment; no archive or developer-tool installation is required to run it.

Evidence, the narrowly scoped regression logs, workflow harness and native UI
renders are under `build_test/macbook-sync-20261020/`. The original Air profile
was backed up before testing. The application assembly and transport were real;
physical mouse/keyboard input and OS Accessibility interaction were outside this
in-process workflow check.
