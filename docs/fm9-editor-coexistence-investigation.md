# FM9-Edit coexistence investigation

## 21 October 2026: names-only sync on Windows 11

User evidence: `sync.jsonl`, operation `f0dd4477-823c-4329-9d1b-6761bdd9c28b`,
direct FM9 USB connection on 192.168.2.155, FM9-Edit connected, Forwarder stopped.
The supplied file begins during slot 13, so the operation's start/version record is absent.

- Completed all 512 names in 23,135.85 ms, including five successful recovery reads.
- Initial timeouts at zero-based slots 230, 237, 273, 277 and 476.
- Each timeout received two opcode `0x64` frames and no opcode `0x0D` frame.
- No receive-queue drops or invalid matching frames were recorded.
- Reply timeouts consumed about 7.5 seconds; subsequent quarantine consumed another 7.5 seconds.
- Request-gate waiting was negligible; recorded UI update delays do not explain these pauses.

This establishes missing expected replies at the application's assembled-frame reader. It does
not establish whether the hardware rejected the request, the transport lost it, or another
application interfered. A successful recovery does not make the new pauses acceptable.
The user's earlier completely stalled state is not present in this file.

Source review: background polling is cancelled and suppressed during names-only sync.
The 1.5-second post-timeout quarantine predates the new saved-scene polling. The 10 ms query
pacing was added in `f43b77b`; its effect on coexistence has not been physically compared.
Do not label either as the cause without a reproduction.

## Next diagnostic build

`MidiReadEvidence.ResultCodes` retains counts of checksum/model-validated `0x64` response
envelopes by echoed function and raw result byte, without preset names or raw packets.
Other clients' result envelopes remain unrelated traffic; they neither complete nor retry
our request. Malformed or other-model envelopes are excluded from these counts.

No timing, retry, polling, routing or device-write behavior was changed. A regression test
checks mixed-client responses, invalid checksum, another model and proper preset correlation.
The 26 focused pacing, name-recovery and scene-client tests passed locally on Windows.

Run the diagnostic EXE with FM9-Edit still connected and Detailed sync timing enabled,
then inspect the failed name requests' ResultCodes before choosing a protocol change.
This is an investigation build, not a claimed coexistence fix. Live cause and fix remain open.

## Second run and candidate transport fix

Operation `89fe13f3-bb7a-45f7-96e5-64223735f913`, FM9 firmware 12.00:
512 names completed in 35,178.72 ms. Nine first-pass reads timed out (slots 49, 123, 181,
193, 267, 311, 390, 397, 411). Every one received exactly the same validated result pair:
`0x0D:0x01` and `0x0D:0x00`, with no name reply. Each recovery succeeded.

The [manufacturer's historical specification](https://archive.axefx.fr/AxeFX%20II/Docs%20%26%20Manuals/Fractal%20MIDI%20Spec%20Public%2022%2001%202014%20resaved.htm)
defines status 1 as bad checksum. This is suggestive, not a current FM9 firmware contract or
proof that either particular application's request caused the result. Numeric wire evidence
is retained without relying on that interpretation in production code.

The shared Windows physical transport submitted the SysEx7 start and end of a 10-byte
name request in **two separate API calls**. Its local lock does not serialize other apps.
[Microsoft tracks cross-client SysEx collision handling](https://github.com/microsoft/MIDI/issues/60)
as a separate service feature. Fragment interleaving is a plausible cause consistent with
the result pattern; a physical capture of both transmitters has not established it conclusively.

The initial test package submits each MIDI message in one
`SendMultipleMessagesWordArray` transfer when it fits. It queries the connection's maximum
transfer size; larger messages split only at whole UMP boundaries, and sending stops on
failure. This follows the [Microsoft API guidance](https://microsoft.github.io/MIDI/sdk-reference/MidiEndpointConnection/).
It removes our per-fragment API boundary without claiming a service-wide atomicity guarantee.
No timeout, retry, polling interval or legacy/Mac path is changed.

The source fix is in the sibling MIDIHelperLibrary repository, and the new pinned packages
are checked into this consumer's local feed. Ordinary package-based validation is used.
`InputTransport` now records the concrete receive adapter's type name (no endpoint names)
to establish whether the affected machine actually uses the changed path.
Hardware acceptance must repeat names-only sync with FM9-Edit connected and inspect both
timeouts and result codes. Saved rename/swap acceptance follows once coexistence is stable.

Local candidate validation: 72 shared-library tests and 51 focused application tests passed.
Windows native virtual endpoint acceptance passed short and 60 KB current-sender SysEx
round trips, plus 128 short requests sent concurrently with 128 editor-sized messages
from a separate legacy client, checking exact complete bytes. These are synthetic virtual
endpoints, not the FM9 driver or firmware. Package-based Windows publication and the
index-disabled build passed. Test EXE version 1.2.30 is separate from the installed app.
The complete ordinary Windows application suite also passed: 955 tests, zero failures
(hardware and compatibility categories excluded).

## Direct FM9 testing and receive-loop fix, 21 October

SSH acceptance on the affected Windows 11 build 26300 workstation used the physical `FM9`
input/output through Windows MIDI Services, with the existing FM9-Edit process connected
throughout. No Forwarder or production Preset Maestro instance was running. Tests allowed
only identity, firmware/name/state queries and stored-preset reads. They used isolated
profiles/checkpoints, never the user's profile, and verified unchanged active selection.

Initial sender-fixed build: all 512 names in 8.0 seconds with no timeouts/retries; two complete
production-scanner runs in 155.487 and 155.446 seconds with no errors/retries; the actual
`MainWindow.SyncIndexAsync` command in a headless application also completed in 155.804 seconds.
The latter exercises its dispatcher, progress updates and publishing an isolated profile/library.
These successful reads alone did not reproduce the reported first-attempt stop.

A subsequent connection attempt received no frames and timed out on WHO_AM_I. Investigation
found that `ServicesMidiInputPort.PumpAsync` caught decoding exceptions outside the entire
read loop. Attaching midway through an existing SysEx7 stream throws on an orphan continuation,
which killed reception permanently while the manager still reported an open input.

Native virtual-endpoint regression first reproduced this terminal failure by injecting an
orphan continuation followed by valid SysEx. The receive-loop fix catches malformed-packet
`InvalidDataException` per packet, resets all partial SysEx assemblies, reports the discarded
input and continues listening. Transport failures/queue overflow still terminate rather than
silently skip lost data; preset checksum/CRC validation is unchanged.

Package `0.3.11` contains both the sender and receiver fixes. Hardware comparison:

- Old receiver, identical probe: attempt 2 failed. Its raw log records exactly
  `MIDI INPUT ERROR: Received an out-of-sequence SysEx7 UMP packet.` before identity timed out.
- Fixed receiver: 20/20 fresh process/port connections succeeded with FM9-Edit connected.
  Attempts 1 and 15 encountered the same orphan-packet condition (9 and 53 discarded tails)
  and then successfully read identity and current state. This is positive recovery evidence,
  not merely 20 runs in which no fault happened.
- Native virtual-endpoint regression passed after the fix, including subsequent valid SysEx,
  short/60 KB transfers and concurrent legacy/current writers.
- 74 shared-library tests and 51 focused application tests passed for the receiver fix.

Private raw traces and probe sources remain under ignored `TestResults/coexistence` and
`C:\Users\paul\PresetMaestro-Coexistence-Test` on the workstation. The raw files contain
personal preset data and are not repository documentation. Startup evidence is retained in
`startup-evidence-remote.zip`; its numeric/error-only comparison is `startup-report-remote.json`.
The standalone application containing both fixes is version 1.2.31.

Final receiver-fixed main-app sync acceptance completed all 512 slots in 155.835 seconds,
with zero errors and unchanged active preset/scene. Evidence: `app-fixed-01-remote.jsonl`.
Version 1.2.31 was installed at `C:\bin\PresetMaestro.exe` on the affected workstation,
preserving `C:\bin\PresetMaestro.exe.before-coexistence-20261021`. Installed SHA-256:
`1F3E6AD002BCC4BFA14D2BF29586C8A8462F1A7FE37F1A435891572BA633B5E3`.
FM9-Edit remained running/responding after acceptance. The shared coexistence-test EXE was
also updated to this build. No user preset, tag, favourite or profile data was modified.
The helper package was finalized as the numeric version `0.3.11` and the app rebuilt
without behavioral changes; the installed hash above identifies that final rebuild.
