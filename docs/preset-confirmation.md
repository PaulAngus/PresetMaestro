# Preset and scene confirmation

Implemented 23 October 2026 for preset entry (including Next/Previous/Last),
favorites, index sends and Go to from preset/scene pickers and amp results.

A successful MIDI write displays a neutral **Confirming preset** message. After
200 ms, Maestro queries the active preset on its existing MIDI connection. Only
a valid reply for the requested preset changes this to **Preset confirmed**.
The query addresses the detected FM9, FM3 or Axe-Fx III; checksum, model and
slot range must match. This does not broaden the existing FM9-only scene/name
scan support or claim physical qualification for other models.

The current-preset query and returned slot follow Fractal Audio's
[third-party MIDI specification, commands 0C and 0D](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf)
and its [current-generation model identifiers](https://wiki.fractalaudio.com/wiki/index.php?title=MIDI_SysEx).

The total deadline is four seconds, including request queueing and the existing
late-response quarantine. Different slots can settle to the requested slot
within that interval. A persistent mismatch reports requested and actual display
numbers. An absent reply/read failure reports that the preset was not confirmed
and asks the user to check the connection and retry. On mismatch the display
returns to the reported preset; without readback the current preset is unknown.
The send is never repeated automatically. Sending the already active preset
counts as confirmed when that same preset is reported.

New sends supersede older confirmations. Disconnect, profile switch and closing
cancel pending confirmation. Background state polling yields while confirmation
is pending, and stored-scene refresh starts only after confirmation when needed.

## Scene confirmation extension — 23 October 2026

Favorites and every scene Go to (index, amp results and scene pickers) now display
**Confirming preset and scene**, then **Preset and scene confirmed** only after
both requested values match. Preset-only sends retain their existing wording.
Sending an already selected scene also requires readback; it does not require an
artificial change away from that scene first.

Scene replies contain no preset identity. The existing serialized client reads
the current preset, queries the current scene with `0C 7F`, and reads the preset
again under the same request gate. Both preset reads must match, and a preset
change notification during the snapshot invalidates the scene. An initial wrong
preset is reported without attributing a scene to it. Model, checksum and scene
index (wire 0–7, displayed 1–8) must be valid.

A persistent wrong scene shows **Scene did not change**, including requested
scene, preset and actual reported scene. Missing/invalid replies show **No scene
confirmation** within the same four-second total deadline. A transport loss also
shows a scene error if scene confirmation was pending. The actual observed scene
is shown on mismatch; unverified scene state stays unknown. No navigation is
resent automatically, and a newer preset/scene send, profile switch, disconnect
or window close cancels older confirmation. Same-preset Go to retains its
scene-only CC send and does not fetch a stored preset dump.

## Verification boundaries

`PresetConfirmationTests` provides component-only coverage with in-memory MIDI
replies: active-slot success for all three routes, bank boundaries and display
offsets, wrong preset, no response, delayed loading, corrupt/wrong-model replies,
rapid sends and disconnect/profile cancellation. Existing layout/hover tests
remain component evidence. No MIDI device is opened by these tests.

`SceneConfirmationTests.cs` extends that component class with all eight scenes
and all three scene routes, both display offsets, wrong/missing scene replies,
scene settling, rapid successive sends, transport/profile/window cancellation,
model/index/checksum validation, and a preset changing during a scene read even
when it returns to the original slot. These simulations are development
evidence only and must never be presented as native functional acceptance.

The shared platform's requirements register updates `maestro-keypad-index-scene`
to assertion version 4 and updates `maestro-favorite-preset-confirmation` (version 2)
for unchanged installed application actions, native confirmation feedback,
independent actual FM9 preset/scene readback and verified restoration. It also
updates `maestro-preset-confirmation-failures` (version 2) as explicitly incomplete
until genuine failure conditions and native recipes are available. Windows
native coverage also remains incomplete. See
`../crossplatform-testing/docs/maestro-preset-confirmation.md` in the sibling
repository for exact handoff requirements.

User correction, 23 October: real functional/integration acceptance uses no
synthetic tests, fabricated settings/configurations or artificial environments.
An unreproduced wrong-preset or missing-response condition remains incomplete;
simulated component results do not qualify it. No hardware or native acceptance
was run for this change, and existing distributed candidates remain unchanged.

## Scene extension development evidence — 23 October 2026

- Windows Release solution and optional no-index builds passed with zero
  warnings/errors. Scoped formatter/analyzer verification passed.
- Selected offline component suite: **369 passed, zero failed/skipped**. This
  includes the preset regressions and new scene confirmation cases; no device
  was opened. Receipt: `TestResults/scene-confirmation/paulg_PAULPC_2026-10-23_21_01_38_net10.0.trx`.
- Pending, combined confirmation and wrong-scene messages were rendered in both
  themes at 1000x850 and 1440x850 under
  `build_test/scene-confirmation/component-renders`. Banner text wraps and is
  readable; page position and keyboard focus are retained. These headless
  component renders are not approved native acceptance screenshots.
- The shared platform's updated Python and JSON were statically parsed without
  importing/executing platform tests. No test machines, MIDI connections,
  installed release candidates or approved drivers were accessed or changed.
- Source version and dependency pins remain as found. No normal release package
  was published/installed, so actual-release verification remains pending via
  an immutable shared-platform `application-change` plan with normal candidate
  provenance and hashes. Genuine failure cases remain explicitly incomplete.

## Original preset extension development evidence — 23 October 2026

- Windows Release solution build and the optional no-index build passed without
  warnings or errors. The source version was already 1.2.31 and the MIDI package
  pin was already 0.3.12; this task did not publish or install a candidate.
- Initial focused component regression run: 288 passed, zero failed/skipped.
- Follow-up confirmation/connection/feedback component run: 76 passed, zero
  failed/skipped. A prior render attempt had a test setup error (sender page not
  opened); it is superseded by the corrected runs.
- Final same-preset Go to, transport error and correctly sized render checks:
  nine passed, zero failed/skipped. Light/dark pending, confirmed and mismatch
  renders were captured at 1000x850 and 1440x850; readable banner text, unchanged
  content position and retained keyboard focus were checked. Native inspection
  remains a platform acceptance obligation. The existing white preset-number
  text on the light theme was observed outside the changed banner; its relation
  to prior shipped releases is not established by these component renders, and
  no unrelated color change was made.
- Changed C# files passed scoped formatter/analyzer verification. Shared
  platform Python and requirements JSON were statically parsed only; no platform
  test code was imported or executed.

TRX receipts are in `TestResults/preset-confirmation`,
`TestResults/preset-confirmation-final` (use the later passing receipt) and
`TestResults/preset-confirmation-render-final`. Component PNGs are in
`build_test/preset-confirmation/component-renders`. These are development
artifacts, not normal release provenance or acceptance qualification.
