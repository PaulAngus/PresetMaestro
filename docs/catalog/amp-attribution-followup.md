# Amp attribution follow-up

Catalogue **2026-10-03.4**, reviewed 2026-10-03. Applies the user's supplied ChatGPT research table to the existing catalogue. The input digest, source scope and per-model findings are recorded in [the machine-readable audit](fm9-12-amp-identities.json), under `Sources` and `FollowUpMappings`. The original CSV names and descriptions are preserved.

## Applied findings

| Fractal model | Displayed attribution | Qualification |
|---|---|---|
| USA MK IV Lead | Mesa/Boogie Mark IV, Lead | Rev B probable, not proven. Exact selectable name mapped separately from the CSV LEAD/RHYTHM heading. |
| Matchbox Chiefman 1 | Matchless Chieftain, normal/unboosted | Shares one family with Chiefman 2. |
| Matchbox Chiefman 2 | Matchless Chieftain, boosted | Added gain/high-frequency emphasis; not another physical amp. |
| Fox ODS Mid | Fuchs Overdrive Supreme 50, OD/Mid-boost variant | Exact Mid-switch setting is firmware-specific; not equated with Deep. |
| Vibrato Verb Custom | Fender '64 Vibroverb Custom reissue, SRV/Mod | Separate from stock AA763/AB763. Normal-channel triode removal is supported by a direct Fractal administrator statement. |
| ODS-100 Clean | Dumble Overdrive Special, Clean | Current Ford/#102 descriptions and historical HRM serial 0213 descriptions are retained; exact FM9 12 reference unresolved. |
| Brit 800 2203 High | Marshall JCM800 2203 100W, High input | Exact JMP/JCM800 badge/year undocumented. |
| Brit 800 2203 Low | Same 2203, Low input | Shares the High-input reference. |
| Brit Silver | Marshall Silver Jubilee 2555 100W | Supplied research accepted; cited administrator page not independently confirmed in this follow-up. |
| Class-A 30W Bright | Vox AC30, non-Top-Boost Bright | Device-confirmed ID 233. Physical-channel detail uses a historical release-note repost. |
| Class-A 30W Brilliant | Vox AC30, Top-Boost style with Matchless DC-30 tone stack | Derived model; no invented production year. |
| Friedman BE 2010 | Friedman Marsha / early BE-100, Brown Eye | Device-confirmed ID 287. Separate from later BE-100 V1/V2. |
| Friedman HBE 2010 | Same early Marsha, Hairy Brown Eye | Device-confirmed ID 288; higher gain on the same early reference amp. |
| Porta-Bass | Ampeg B-15R Portaflex reissue **(inferred)** | B-15 family documented; B-15R inferred from controls and 6L6 section, not explicitly named by Fractal. |
| Princetone Reverb | 1966 blackface Fender Princeton Reverb, AA1164 family **(inferred)** | Wiki AA964 conflict retained; exact circuit not independently verified. |
| Vibrato Lux | 1963 brownface Fender Vibrolux 6G11, Bright | Preferred year from reproduced original release; Wiki also says 1962/63. Not Vibrolux Reverb. |

## Evidence handling

Fractal's own statements take precedence within their documented scope. These direct primary statements were checked:

- [FractalAudio on Vibroverb Custom](https://forum.fractalaudio.com/threads/vibroverb.214777/post-2697459): normal-channel triode removal increases gain and changes vibrato-channel bias (2025-08-15, post 24).
- [FractalAudio on Marsha and later BE-100 references](https://forum.fractalaudio.com/threads/which-are-the-new-be-hbe-models.114510/latest): original Marsha and Mark Day BE-100 are separate reference amps (2016-05-07, post 5).
- [2010 Fractal press release published by Premier Guitar](https://www.premierguitar.com/fractal-audio-systems-releases-firmware-10-0): historical Marsha development with Dave Friedman. This announcement concerns older models, not current DSP IDs.

The supplied Wiki findings, cited Silver Jubilee forum page, Guitariste historical release text and Rig-Talk release repost have their individual access/scope limitations recorded in the audit. A repost or ChatGPT summary is not relabelled as an independently checked official publication. Ampdex and Wiki share lineage; agreeing copies do not establish independent confirmation.

## FM9 hardware confirmation

The connected FM9 running firmware 12.00 returned all **336 selectable amp names** on 2026-10-03. Every numeric ID/name pair is now device-confirmed, including all previously confirmed IDs and the three missing joins above. The old table omitted 52 entries; ID 283 now returns Deluxe Tweed Bright. Bassguy RI Jumped is confirmed as 302 and kept separate from the original 5F6-A family.

[Raw roster evidence](fm9-12-device-roster.json) preserves each request index and reply, firmware, the rejected boundary response at 336, and unchanged-state checks. The active preset, scene, current amp type reply and complete Amp block data matched before and after. Only read requests were sent.

Fractal's [FM9 12.00 release notes](https://www.fractalaudio.com/fm9-downloads/) establish the new Deluxe Tweed Normal, PVH Block Clean and three JVM Crunch channels. They supply attribution for the five additional runtime entries at 331-335. Numeric confirmation applies to FM9 12.00 only. Unknown IDs and other device/firmware combinations still need separate evidence.

## Remaining attribution uncertainty

Base manufacturer/product identification is established or accepted for the supplied models. Remaining uncertain details are B-15R revision, Princeton circuit, Fox Mid switch meaning, ODS-100 Clean reference revision, Mark IV Rev B, Marshall 2203 badge/year, AC30 exact year, Silver Jubilee exact revision, and Vibrolux manufacturing year. Numeric joins for Bright and Friedman 2010 are now confirmed; remaining qualifications concern reference amplifier details.
