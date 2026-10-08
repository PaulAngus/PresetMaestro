# Recovery of missed preset-name replies

A new MIDI connection still needs a current check: a saved full scan cannot establish that the same
device remains connected or unchanged. Previously the initial 512-name pass skipped isolated timed-out
queries without retrying them, then refused to start the preset/scene comparison if even one name was
missing. This produced repeated 511/512 or 510/512 outcomes despite a recent successful full sync.

The name-read session now completes its initial pass and retries only missing slots once. Successful
names are not read again. Recovery uses the existing serialized MIDI source and late-response quarantine;
it does not overlap requests or assume a cached name is fresh. A recovered name is included in name
comparison and the quick/full content check can proceed normally.

Persistent misses retain their cached names, but remain excluded from the freshly returned slot set.
The check remains incomplete and does not update the saved baseline. Its message now explains that
missing replies were retried, offers another check or Done with saved data, and makes clear that a full
sync is optional. Three consecutive initial failures still stop promptly; three consecutive recovery
failures stop the recovery pass. Cancellation is honored during either pass. Timing logs record each
failed request and `names.retry` with the zero-based slot.

This improves recovery from intermittent missed replies; it does not identify or repair an underlying
MIDI driver/transport issue. The running app must use the updated build for this behavior.

The explanatory copy follows [Fluent 2 content guidance](https://fluent2.microsoft.design/content-design),
using a concrete outcome and next action. This is a copy/recovery change within existing controls;
no new layout pattern or deliberate design deviation. Tests cover successful and persistent misses,
bounded retries, cancellation, names-only outcomes, and reconnection after a completed saved scan.
Rendered success and incomplete outcomes were inspected across light/dark themes at 1000/1440 widths.
