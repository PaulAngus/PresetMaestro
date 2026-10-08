# MIDI query pacing

The physical FM9 quick check on 7 October 2026 (1.2.5) recovered two missing preset-name
replies. Slot 84 saw 145 frames with no name response; slot 373 saw 161 frames with one
rejected name response. Neither request recorded a queue drop. Both retries completed
in about 1 ms. This does not establish a driver, device or competing-client root cause.

Previously the next serialized query was sent immediately after the preceding reply.
The client now enforces a minimum 10 ms between query starts, within its shared gate,
using a monotonic clock. The first request is immediate; requests taking longer than
10 ms do not incur an extra delay. Cancellation during pacing sends nothing and releases
the gate. The response deadline starts after pacing, so the wait does not consume it.
The existing late-response quarantine, address/checksum validation and bounded name
recovery remain in place. Preset and scene selection commands are unchanged.

The 10 ms interval is a conservative application choice to reduce bursts, not a
documented Fractal requirement or a proven fix for these timeouts. It adds about five
seconds of minimum wire scheduling to 512 fast name queries; Windows timer scheduling
can add more. Full preset dumps typically already take much longer than the interval.
Hardware comparison must measure total duration and initial-pass misses across repeated
checks, rather than treating a single recovered check as proof of a fix.

Receive diagnostics now include bounded counts by Fractal opcode (and an `other` bucket),
without payloads or names. These identify traffic seen during a miss. They do not change
which frames the decoder accepts or imply that unrelated traffic caused the failure.
Pacing is recorded as `midi.pacing`, separate from response time and timeout quarantine.

The [official third-party MIDI specification](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf)
documents preset-number identity in name responses and no preset identity in scene
responses. It does not specify a minimum query interval. Retaining serialized requests
and stable-preset checks remains necessary; broad retries of scene snapshots can hide
a preset change and are not added here.

Regression checks simulate a device that ignores bursts, concurrent name/scene callers,
cancellation during pacing, unrelated traffic with a wrong-address reply, and the
existing timeout recovery and fragmented dump paths. Physical improvement remains to
be verified on the user's connected FM9.
