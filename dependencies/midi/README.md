# Pinned shared MIDI dependency

The exact package family is **0.3.14** (`MidiVersion.props`, `version.txt` and
`packages.sha256`). It is built from MIDIHelperLibrary and copied here as a
build feed; no sibling checkout, user-profile API installation or global runtime
is required by the published application. `source-manifest.json` records the
library source identity. Other consumers retain their independently pinned versions.

The Windows provider embeds Windows.Devices.Midi2 0.99.88-preview.10 DLL/PRI and
the matching architecture's required Visual C++ runtime libraries. Eligible
Windows 11 builds automatically extract verified, content-addressed private copies
and load them using absolute paths. Windows 10 never extracts or loads this API.
Existing application-specific extraction remains supported. The explicit
`MBU_MIDI_API_DIRECTORY` diagnostic override is optional and must point at a
complete compatible API if set. Ordinary users should leave it unset.

Windows 10/11 hardware MIDI uses the OS WinMM path where the current service is
unavailable. Virtual MIDI presentation is unsupported on Windows 10. Windows 11
virtual interfaces additionally require compatible OS MIDI Services and loopback
transport; embedding the API does not install or upgrade those OS components.

The macOS packages carry DryWetMIDI's universal native library and RtMidi's arm64
library. RtMidi bulk relay is used by Forwarder (Apple Silicon); Maestro and
Sender's standard CoreMIDI paths also support their existing Intel build target.
Native .NET, Avalonia, Skia and HarfBuzz assets belong inside the signed .app.
CoreMIDI and Apple network MIDI are OS components; the network backend still needs
an enabled OS RTP session. No Homebrew or runtime setup script is required.

`Initialize-SharedMidi.ps1 -PrepareBuild` is developer package preparation only.
The older `-EnableWindowsServices` per-user API installer is obsolete for these
application releases and must not be included in user installation instructions.

## Validation scope

The dependency audit separates static/published checks from clean-host and full
MIDI workflow qualification. See crossplatform-testing/docs/runtime-dependency-audit.md
for evidence and outstanding Windows/macOS/architecture checks.

## Historical validation of 0.3.6 (not evidence for 0.3.10)

### Offline validation (Windows, 2026-10-18)

Windows: **870 passed, 4 failed**. Managed Mac: **878
passed, 4 failed, 1 native test skipped**. Both suites used the hardware and
compatibility category exclusions and disabled physical-device opt-ins.

The four failures in each suite are
`IndexViewLayoutTests.IndexHeaderAndResultsRetainTheirVerticalFootprint`
(light/dark at two window sizes). The exact same case names and assertion messages
were reproduced against the original 0.3.5 packages, with the current application
sources and isolated build outputs, on both platform graphs. They are existing
layout failures; the complete suite is not green. Application/UI/test sources
were preserved, including their existing work in progress. No MIDI regression
was observed among the passing tests. The source application version is unchanged.

Self-contained win-x64, osx-arm64 and osx-x64 publications passed. They used fresh
repository caches and local sources only: first-party MIDI archives from this
bundled feed, existing third-party/runtime packages from the local package cache.
Publication outputs were isolated for verification. Payload inspection confirmed
exact MIDI DLL hashes, the complete .NET runtime, platform exclusions, correct Mac
apphost architectures and the declared native asset hashes/architecture slices.

Empty-cache restore probes accepted the exact 0.3.6 packages, refused a missing
bundle even with an alternate local feed, and refused a synthetic higher version.
All four applications now select the same exact 0.3.6 family with identical
archive bytes and source provenance. Source-development overrides were inactive.

No native MIDI enumeration, device connection or MIDI send was performed.
Native macOS execution and physical interoperability remain untested.

The exact clean producer publication receipt is retained as `normal-publication.json`.
This adoption is a separate shared-MIDI change; no earlier acceptance qualifies these bytes.
