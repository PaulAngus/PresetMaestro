# Pinned shared MIDI dependency

These first-party packages come from the independently buildable [MIDIHelperLibrary repository](https://github.com/PaulAngus/MIDIHelperLibrary). Package and namespace names remain `MightBeUseful.Midi`.

The selected release is 0.3.6. Startup and explicit device discovery refresh the Windows backend capabilities on the discovery worker before enumerating ports, allowing a later refresh to recover from a temporary service failure. The macOS refresh hook is a no-op. Windows still uses the shared optional API setup described below; selecting 0.3.6 does not bundle the native API into this application.

The selected family is exactly **0.3.6**: Midi, Windows, CoreMidi and Rtp.
All four archives are byte-for-byte identical to the release bundled by AI Toolkit
for Fractal and MIDI Forwarder. `packages.sha256` pins their archive bytes;
`source-manifest.json` records the source baseline and reviewed local source hashes.
Licences and notices remain inside the archives, including the RTP attribution.

`MidiVersion.props` supplies the plain release value for packaging filenames and
the exact `SharedMidiPackageVersion` range `[0.3.6]`. All application and helper
MIDI references use this range, including the Mac RTP dependency. NuGet source
mapping routes `MightBeUseful.Midi` and `MightBeUseful.Midi.*` exclusively to this
bundled feed. Other sources retain their existing third-party roles. The local
`.build/nuget-contained` cache isolates normal builds from unrelated global cache
entries. Source-development overrides remain explicit.

Normal builds need no sibling checkout or externally published first-party feed.
Update the complete package family, exact version property, source provenance and
archive hashes together from one reviewed library release; do not copy MIDI
implementation source into the application.

MBU_MIDI_BACKEND=auto/current/legacy controls the shared Windows policy. Run ../../Initialize-SharedMidi.ps1 -EnableWindowsServices on an eligible Windows 11 25H2+ PC to install the optional current native API for all apps. Legacy MIDI needs no optional setup. Application --midi-backend and --list-ports commands report the same provider policy.

Release 0.3.6 adds the shared diagnostic RTP responder; production backends and
CC/PC encoding retain their behavior. This update preserves application routing,
backend selection, failure/reconnection and packaging policies. It does not select
an RTP route or enable live-device checks. Native hardware validation is separate.

## Offline validation (Windows, 2026-10-18)

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
