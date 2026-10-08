# Sync timing diagnostics

Config → Diagnostics contains **Detailed sync timing** and **Open sync logs**.
The setting is saved for this PC, independently of profiles and the MIDI channel-filter Debug mode.
It is captured when an operation starts; changing it affects the next sync or check.

The normal app supplies its profile-store Logs folder explicitly. Injected UI/test windows have file logging
disabled unless a test supplies an isolated folder, keeping synthetic failure runs out of the user's logs.

Every full/resumed sync, quick/full device check and names-only sync records a start and a summary.
Summaries include operation ID, app version, model/firmware, outcome, elapsed time, available slot/error totals,
and phase counts, total/max milliseconds, timeouts and failures. Failures and retry events retain their raw,
zero-based MIDI slot numbers even with detailed logging off. An interrupted names-only check reports
unavailable names, including unattempted slots, rather than asserting all were MIDI timeouts.

Background scene-status read failures produce a `background-failure` record with phase timings,
including `state.scene` for the current-scene query (`0x0C`). Successful idle polls and ordinary
cancellation do not write records. This keeps intermittent scene-status timeouts available for diagnosis.

Detailed timing adds successful per-slot records. It separates:

- `names.query` / `preset.name`: name queries, including any request-gate or quarantine wait.
- `names.retry`: the second attempt for an isolated missing name, recorded even with detailed timing off.
- `preset.dump`: each full read attempt; `preset.retry` marks recovery after a timeout.
- `midi.queue`: wait for the shared request gate.
- `midi.quiet`: wait to quarantine late replies after an earlier timeout/cancellation.
- `midi.send` / `midi.reply`: sending the query and waiting for/assembling its reply.
- `midi.receive`: request opcode, received/matching/rejected frame counts, invalid matching frame counts,
  frames discarded before sending, and receive-queue drops. Invalid matching frames fail model/checksum/framing
  validation; other rejected matches can have an unexpected slot, scene value or payload shape. Counts do not
  contain raw MIDI or names. Zero received frames means no complete frame reached this request's reader;
  it does not prove the device sent nothing. The timeout message also shows received/matching/rejected counts.
- `preset.decode`: preset-body decoding.
- `scan.checkpoint` / `scan.save`: checkpoint writes and full cache saves.
- `ui.queue` / `ui.update`: delay before the UI runs a posted progress update, and time to update controls.
- `ui.timer-delay`: lateness of the 100 ms name-progress timer, so UI delays during the initial names phase are visible too.
- `scan.read-loop`: actual scanner duration, excluding any subsequent update-confirmation dialog.

These phases are nested; do not add their durations together. The enclosing operation duration can include
setup, review prompts and final UI refreshes. Summary counts include retained slots on a resumed scan;
phase counts describe requests performed during this operation. Progress callbacks executed after the
operation ends are ignored. No polling or additional MIDI requests are introduced by logging.

Logs are local JSON lines at `%APPDATA%\PresetMaestro\Logs\sync.jsonl`, plus `.1` and `.2` backups.
Each file is limited to 2 MiB (approximately 6 MiB total). Records contain timings, numeric slots and failure
types, not preset/device/profile names, preset contents, exception messages or raw MIDI packets.
**Clear Log** clears the on-screen MIDI log; it does not delete the timing files.

Serialization and buffered file writes run on a background worker. The producer uses a bounded 4096-record
queue and never waits for file I/O. If the writer cannot keep up, records are dropped and the cumulative
drop count is included in later summaries. File failures disable this log writer for the current app session;
they do not stop or change device reads. Closing requests cancellation and a background drain after active
operations finish, without blocking the UI thread. Process exit or abrupt termination can interrupt that
drain and lose queued records; completed batches have already been flushed.

## UI guidance and validation

[Fluent 2 switch guidance](https://fluent2.microsoft.design/components/web/react/core/switch/usage/)
and [Windows toggle-switch guidance](https://learn.microsoft.com/en-us/windows/apps/design/controls/toggles)
recommend switches for immediate binary settings with clear labels. Applied using Avalonia's themed
ToggleSwitch, a short noun-phrase label, visible helper text and an accessible name. Product choice:
capture the setting per operation so an active log remains consistent; the helper text explains this.
No deliberate pattern deviations. The folder action is separate from the logging setting and Back navigation.

Validated keyboard Space activation and persistence without changing MIDI filtering. Rendered and inspected
Diagnostics in light/dark themes at 1000 and 1440 pixel widths. Regression tests cover missing names,
successful timeout recovery, cancelled reads, asynchronous scope isolation, log rotation, failed writers and
graceful shutdown. Existing busy-UI name-reading coverage verifies reads remain independent of UI rendering.
