# Windows and macOS development

Preparation status: the macOS implementation can be cross-built and its managed logic tested on Windows. Native launch, CoreMIDI and Apple signing still require a Mac run; the M2 acceptance checklist below is a release gate. Linux is not an application target.

## Architecture and dependency isolation

One UI, profile format and device-protocol implementation serve both systems. `build/DesktopPlatform.props` selects one platform before NuGet restore. A supplied runtime identifier takes precedence over the host; ordinary builds choose the host. Separate `obj/Windows`, `obj/macOS`, `bin/Windows` and `bin/macOS` paths prevent restore and generated-code collisions when switching targets. `.gitattributes` makes C# line endings match the existing formatter on both Windows and Mac checkouts.

| Build | Framework | UI backend | MIDI backend | Release |
|---|---|---|---|---|
| Windows x64 | .NET 10 / Windows 10 API contract 19041 | Avalonia.Win32 | NAudio WinRT input / WinMM output | Compressed, self-contained EXE with ReadyToRun |
| Apple Silicon | .NET 10, `osx-arm64` | Avalonia.Native | DryWetMIDI 8.0.3 / CoreMIDI | Self-contained `.app` in ZIP |
| Intel Mac | .NET 10, `osx-x64` | Avalonia.Native | Same Mac backend | Separate `.app` in ZIP |

The Mac bundle declares macOS 15 minimum, matching the oldest currently supported macOS in [.NET 10's support table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md), reviewed 8 October 2026. Actual compatibility remains to be validated. The Windows API minimum is unchanged; use a Windows release supported by Microsoft for deployment.

Platform source files and package references are conditional. Neither release references `Avalonia.Desktop`, the NAudio umbrella package, or a Linux UI backend. Windows does not reference DryWetMIDI or Avalonia.Native; macOS does not reference NAudio, Avalonia.Win32 or the Windows SDK projection. Some shared NuGet packages contain assets for multiple systems; the SDK selects only the requested RID for publication. Downloaded package contents are not the shipped payload.

DryWetMIDI 8.0.3's default build target copies both its Windows DLLs and Mac dylib. We exclude that target and explicitly copy only `Melanchall_DryWetMidi_Native64.dylib`. Its published binary contains ARM64 and x64 slices. Native Mac packaging thins universal libraries to the requested architecture before signing. Cross-packaging on Windows retains universal Mac libraries until that native packaging step; it never includes Windows MIDI libraries. The new library's MIT license accompanies the Mac app.

`build/PlatformPublish.targets` checks both `ResolvedFileToPublish` and `FilesToBundle` before single-file generation, and also checks ordinary folder publication. Wrong-platform UI/MIDI libraries or Linux native libraries fail the publish. The pre-bundle file list is saved as `publish-payload.txt` under the app's platform/configuration/RID intermediate directory. Trimming and Native AOT are not enabled: they require additional compatibility work and are unnecessary for this port. This follows [Avalonia's platform-boundary guidance](https://docs.avaloniaui.net/docs/app-development/native-interop) and [.NET's single-file deployment model](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

## MIDI boundary

`IMidiManager` remains the UI/protocol boundary. `MidiManager` owns port lifetime, output serialization, bank/program/scene encoding, note notifications, stale-callback rejection and MIDI thru. The small internal input/output-port interfaces carry complete MIDI 1.0 messages without a platform-library type in their signatures.

The Windows adapter keeps WinRT input and WinMM output. The Mac adapter uses the maintained library's native CoreMIDI implementation, rather than introducing application-owned unmanaged callbacks and packet-layout definitions. It disposes enumeration handles, waits for complete SysEx, uses **device** byte format (not MIDI-file framing), catches exceptions at callback boundaries, and serializes conversion/disposal. Shared routing continues to ignore clock/active-sensing traffic and does not forward SysEx from controller thru inputs. Input parser errors reach the MIDI log.

Primary library references: [device ownership and disposal](https://melanchall.github.io/drywetmidi/articles/devices/Overview.html), [input lifecycle](https://melanchall.github.io/drywetmidi/articles/devices/Input-device.html), [wire conversion](https://melanchall.github.io/drywetmidi/api/Melanchall.DryWetMidi.Core.BytesFormat.html), and [the exact package's build rules](https://github.com/melanchall/drywetmidi/blob/v8.0.3/DryWetMidi/Melanchall.DryWetMidi.csproj).

## Build and verify on Windows

```powershell
dotnet build PresetMaestro.slnx -c Release
dotnet test PresetMaestro.slnx -c Release
dotnet format PresetMaestro.slnx --verify-no-changes --no-restore
./scripts/Test-PublishVersion.ps1
./scripts/Test-PlatformBuilds.ps1
```

The platform script publishes real Windows and Mac artifacts into an isolated test directory, checks architecture and ZIP permissions, deliberately injects wrong-platform libraries to prove both publish guards fail, and checks unsupported/mismatched targets. It preserves the source version and never invokes the local Windows distribution helper.

To exercise the Mac-managed dependency graph on Windows, without executing CoreMIDI:

```powershell
dotnet test PresetMaestro.slnx -c Release -p:MaestroPlatform=macOS -p:PlatformTarget=AnyCPU
```

`AnyCPU` here lets Windows execute the managed test code; it does not validate Mac native binaries. The native CoreMIDI smoke test explicitly skips on Windows. Native Mac builds need no such override. Tests based on Windows mandatory file-sharing locks explicitly skip on macOS, whose file-replacement semantics differ; other shared persistence and rollback checks run normally.

The GitHub workflow contains separate Windows and Apple Silicon `macos-15` jobs. The Mac job builds, tests, loads/enumerates CoreMIDI without sending to hardware, formats, packages, locally signs, verifies signatures and retains the testing ZIP. It requires a pushed commit/PR to execute. Runner selection follows [GitHub's current architecture table](https://docs.github.com/en/actions/reference/runners/github-hosted-runners). Physical hardware tests remain opt-in.

## First setup on the M2

1. Use macOS 15 or newer and install the [.NET 10 Arm64 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/macos).
2. Install Apple's command-line tools with `xcode-select --install`, and [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-macos). PowerShell is only a developer packaging tool; app users do not need it.
3. Clone this repository, open Terminal in it and run:

```sh
dotnet build PresetMaestro.slnx -c Release
dotnet test PresetMaestro.slnx -c Release
dotnet run --project PresetMaestro/PresetMaestro.csproj
pwsh ./Publish-Mac.ps1
```

The script defaults to Apple Silicon. `-Architecture x64` prepares a separate Intel app; it needs Intel/Rosetta validation before being advertised as supported. Outputs are `publish-macos/osx-arm64/<version>/Preset Maestro.app` and the matching ZIP. Windows continues to publish `publish/PresetMaestro.exe`, preserving the existing local copy helper. Each Mac publish starts in a fresh staging directory, so stale binaries cannot leak into a release.

`Publish-Mac.ps1` reuses `PresetMaestro/Version.txt`, or an explicit `-Version 1.2.3`, through a temporary version file. It never advances the source version: use the same explicit release number when preparing both OS releases. Direct `dotnet publish` retains the existing version-increment behaviour, so use the wrapper for Mac releases.

On macOS, a normal package is ad-hoc signed for local testing. Public download releases use:

```sh
pwsh ./Publish-Mac.ps1 -Version 1.2.3 \
  -SigningIdentity 'Developer ID Application: YOUR TEAM (TEAMID)' \
  -NotaryProfile 'presetmaestro-notary'
```

Create the Developer ID certificate and store notarytool credentials in the Mac keychain first; `NotaryProfile` is the keychain profile name, never a password. The script signs native files individually, signs the enclosing app last, verifies it, submits with `notarytool --wait`, staples and validates the ticket, assesses the app and recreates the ZIP. `--deep` is used only for **verification**, never signing. Only the JIT entitlement required by .NET/Avalonia is requested. This is direct-download distribution, not a sandboxed Mac App Store submission. See [Avalonia macOS packaging](https://docs.avaloniaui.net/docs/deployment/macos) and [Apple Developer ID/notarization](https://developer.apple.com/developer-id/).

Windows can also run the packaging script to produce an unsigned preparation ZIP. ZIP Unix permissions preserve the executable bit; native signing, launch and hardware checks are still pending. Prefer rebuilding and signing on the M2 for its first run.

## Local verification — 8 October 2026

Performed on Windows with .NET SDK 10.0.303:

- Windows Release suite: **782 passed, 5 hardware tests skipped, 0 failed**. Baseline before the port: 767 passed and 5 skipped.
- Mac-managed Release suite using the `AnyCPU` command above: **790 passed, 6 skipped, 0 failed**. Skips are the five hardware tests and native CoreMIDI loading. This is not a native Mac run.
- Both dependency graphs pass formatter/code-style verification. Both app targets build with Preset Index disabled, with zero warnings/errors and isolated outputs even when sharing one `--artifacts-path` directory.
- Publish-version regression checks pass, including failed publication and distribution-helper behavior. The source version remains **1.2.11**; existing distribution copies were not replaced.
- Real Windows and Apple Silicon production publishes pass platform isolation checks. Injected wrong-OS files fail both publishes. Linux/mismatched targets, wrong native architecture, a Windows DLL inside the Mac bundle and a ZIP without executable permissions are rejected.
- Apple Silicon and separate Intel Mac bundles pass version, dependency, native architecture and ZIP permission checks. The M2 preparation archive is `publish-macos/osx-arm64/1.2.11/PresetMaestro-1.2.11-osx-arm64.zip` (45,065,970 bytes). Native signing and launch remain pending.
- The Windows restored dependency set exactly matches the original. The new production EXE is **76,137,517 bytes**, versus the existing 76,131,558-byte release: **5,959 bytes larger**, with no Mac platform dependencies.
- Inspected Windows headless renders in Light/Dark at 1000/1440 logical pixels, including Config, numeric input, Favorites confirmation and the preset picker. No clipping or font regression was observed in those views. Images and TRX results are under `build_test/macos-prep/`.

The native Mac CI job is configured but has not run in this local preparation session. No physical MIDI tests were enabled.

## M2 acceptance checklist — pending

- Launch the `.app` from Finder and Terminal, then from a fresh extracted ZIP. Confirm the icon, version, native window controls, default menu/quit behaviour and clean shutdown.
- Inspect light and dark themes at 1000-pixel minimum width, normal width and the wider favorite editor. Check actual Mac fonts, Retina scaling, text clipping, file dialogs, accessibility names, VoiceOver, keyboard focus and dismissal/focus return. See [cross-platform UX decisions](cross-platform-ux.md).
- Verify data is saved under `~/Library/Application Support/PresetMaestro`, persists across restarts and never writes inside the app bundle. Import/export a Windows profile with non-ASCII text; repeat on a case-sensitive APFS volume if available.
- Discover FM9 input/output; connect, identify firmware/device name, read preset/scene names, scan/cancel/resume, navigate presets/scenes and confirm reported state. Confirm original device data is not modified by read operations.
- Test controller thru, channels, note mappings, concurrent replies/forwarding, repeated disconnect/reconnect, USB unplug/replug, sleep/wake and an occupied/unavailable endpoint. Compare MIDI traces to Windows.
- Run the opt-in hardware suite using the [hardware test guide](hardware-tests.md); the PowerShell scripts work with `pwsh` on the Mac and select the native backend automatically.
- For a public release, use real Developer ID/notarization credentials and test a **downloaded** ZIP on a clean Mac account. A local code-signature check does not reproduce Gatekeeper's download/quarantine path.

Passing cross-build and managed tests is preparation evidence, not native macOS or hardware acceptance.
