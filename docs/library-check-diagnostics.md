# Library check diagnosis, 2026-10-04

The reported 15/16 result compared a fresh random sample with the committed scan
started on 2026-10-02 at 22:17 UTC. It did not compare with the connection five
minutes earlier. Quick checks do not update that scan. At a 99% threshold, one
nonmatch in 16 prevents confirmation (93.75%). The old result contained counts
only and discarded sampled slots and mismatch evidence, so the exact slot in
the screenshot cannot be recovered.

A read-only examination of all 254 populated baseline slots found 251 matching
saved-image fingerprints and three different fingerprints: slot 1, **65 Bassman
Blackface**; slot 9, **Plexi 100W**; slot 53, **Silver Jubilee**. Device name and
firmware matched the assigned library. Each differing image was read again;
the fingerprint was stable, the preset/scene names were unchanged, and the
indexed Amp IDs and saved channel/bypass arrays were unchanged. No read failures
occurred. This supports a repeatable difference in saved-image bytes, not a
transient malformed transfer. The previous scan did not retain complete images,
so other effect settings, opaque header data, compression and storage padding
cannot be distinguished retrospectively. Do not infer a particular edit, a
sound change or a different physical unit from this evidence.

The first diagnostic run observed the active preset/scene change during its
read sequence, while a later four-slot validation began and ended at slot 0,
scene index 3. The probe sends device/name/state/saved-dump queries only; it
does not send preset selection, scene selection or parameter writes. No library
or profile baseline was refreshed. Local diagnostic output is kept under the
ignored `build_test/library-check-diagnosis/` directory; personal raw captures
are not committed.

## New evidence and bypass policy

`LibraryMatchResult` retains sampled slots, decoded before/after differences,
metadata cautions, read errors and unread slots after an aborted check. Completed
checks save a report under `FractalIndex/Checks/<library-id>.json`, with the prior
report in `.bak`, independently of the committed library. Check evidence is not
treated as a new baseline or reused as proof on another connection.

The original raw-image SHA-256 remains the annotation identity and integrity
evidence. A separate versioned `gen3-no-bypass-v1:` fingerprint compares the
preset header (excluding CRC) and full decompressed body, clearing only saved
effect bypass words 7–14 in placed block headers. Modifier records remain
untouched. Compression lengths/encoding and trailing raw storage padding are
excluded; all other body bytes, preset/scene names, routing, models, channels
and parameters remain significant. The opaque 257x1 trailing section observed
on the attached FM9 remains byte-for-byte significant rather than receiving a
placed-effect layout. Unknown bypass encodings or incomplete records return no
normalized fingerprint and retain conservative comparison.

The block-header interpretation uses the existing decoder and the original
[gen-3 implementation source](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/presetBody.ts),
which describes channel/bypass words and modifier records. This is community
implementation evidence for an undocumented saved format, not an official
Fractal specification. Four attached FM9 12.00 reads (slots 1, 9, 53, 108)
generated the new fingerprint, including one/two Amp layouts and varied effect
records. Synthetic regressions verify all eight bypass words on Amp and non-Amp
records, retained modifier/trailer data, retained parameters/channel/name edits,
and conservative handling of unknown encodings. Hardware bypass-write validation
was not performed; the probe remains read-only.

Both snapshots must have the new fingerprint to exclude bypass safely. Existing
libraries cannot derive it from a SHA-256 alone. Their raw comparison remains
conservative until an explicitly accepted full library sync establishes the new
data. The review explains this limitation and omits bypass differences from its
decoded change list.
