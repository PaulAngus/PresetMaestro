# Preset-name sync comparison — 2026-10-03

Both Config's **Sync Preset Names** and the connection dialog's **Sync names**
call `SyncPresetNamesAsync`, using the same `PresetNameClient.QueryAsync` reader.
Counts represent distinct slots returned during that run, not the cached count.
Previously Config labelled even a partial result “Complete: 509 of 512”. It now
labels it **Incomplete** and explains that missing replies retain cached names.

## Live FM9 12.00 results

The temporary test harness opened the real Avalonia windows and ran their shared
sync method, with a copy of the user's profile/library. Reads did not select or
write presets. Original profile files were not changed by the harness.

| Connection | Result |
|---|---|
| MIDI Forwarder | Successful scans around 1.2 seconds, but intermittent failures on both screens: Config 511/512 in 4.33 seconds and 509/512 in 10.41 seconds; dialog 509/512 in 10.50 seconds. |
| Direct FM9 USB (`FM9 MIDI In` / `FM9 MIDI Out`) | Ten alternating scans, five per screen: all 512/512; 0.734–0.762 seconds. |

Each missing reply on the Forwarder incurred a 1.5-second timeout and a further
1.5-second late-reply quarantine. In one failed run the reader recorded no queue
drops; each timeout received two `0x64` frames, an unmatched `0x0D` frame and a
`0x0C` frame. The meaning/source of those interleaved frames remains unresolved.
These results implicate the Forwarder connection or traffic on that path; they
do not establish which component lost the expected reply.

The direct cable comparison used the same application code as the failing
Forwarder comparison. No additional retry or timeout change was introduced for
the direct test. Ten successful scans are evidence for this setup, not a guarantee
against all future transport failures.

The temporary harnesses, comparison builds and extra timing/queue tracing were
removed after the investigation on 2026-10-03. These notes retain the findings;
normal application failure logging remains.

## Library-match choice verification

The connection dialog now waits for Quick match or Full match. A read-only quick
check on the direct FM9 USB connection compared 16 random populated presets,
including their saved scenes: 16/16 matched in 9.538 seconds. Full-match tests
exercise all 512 FM9 slots, including previously empty slots, and detect added or
removed presets. They also verify that checks preserve the committed library.
