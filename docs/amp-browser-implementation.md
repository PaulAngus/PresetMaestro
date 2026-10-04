# Amp Browser

Implemented 2026-10-02 against the Amps flow in `fractal-index-ux-research.md`
and the accepted single-library assignment in `fractal-index-data-design.md`.

## Browse and find

**Amps** is a separate top-level page. It uses the active profile's assigned
device library, with no additional profile/device selector. Its initial empty
search shows compact manufacturer panes in responsive masonry columns. Each pane
stacks directly beneath the preceding pane in its column, without aligning to
the heights of panes in neighbouring columns. Search matches
manufacturer, family, aliases, exact Fractal name and raw model ID. Manufacturer
and **Used in my presets** narrow the directory. Unused catalogue entries remain
visible by default. Unknown IDs appear under **Unmapped**; Fractal originals
have their own group.

Preset-name sync collects names only. Amp usage requires saved preset data from
**Preset Index → Sync device** with firmware matching the library. Until that
data exists, **Used in my presets** is disabled and cleared, the catalogue stays
visible, and the page explains which sync is needed with an Open Preset Index
button. Find presets is also disabled without compatible usage data. Partial
compatible scans can supply observed matches while keeping usage labelled unknown.

Select a family to open the bottom inspector while keeping the directory visible.
Family rows show the family name and variant count; variant names appear in the
inspector. All variants are checked initially, and any subset can be selected.
With none checked, Find presets is disabled. Catalogue evidence and availability
details remain accessible through compact disclosure buttons. Body text is 14,
headings 16, and supporting text 12. **Find presets** opens Preset Index with a visible **Contains**
filter and the same library. Result rows and the inspector identify matching
Fractal names and Amp block/channel positions. Clear search and Clear amp filter
are independent. Back restores the family, selected variants, manufacturer, search
and directory scroll position. Zero matches produce an explicit empty result.

Optional saved-scene filters distinguish programmed selection from programmed
engagement. They do not claim effective live usage: Scene Ignore and unsaved
edits remain unknown. Browsing and keyboard navigation send no MIDI messages.

## Catalogue evidence and remaining gate

Catalogue 2026-10-03.4 contains **336 amp ID/name pairs read directly from the
connected FM9 running firmware 12.00**. Both the index name lookup and browser
use this device-confirmed table. All 336 names have explicit family mappings,
and FM9 12.00 catalogue coverage is complete. Other device families and firmware
versions remain separately scoped and do not inherit these IDs. The original
284-entry open roster is superseded by this observation; its licence notices
remain preserved. See [raw query/reply evidence](catalog/fm9-12-device-roster.json).

An FM9 library with no firmware recorded now opens the FM9 12.00 reference
catalogue as a clearly labelled preview, instead of filling the directory with
unknown saved IDs. It does not join those reference identities to the scan:
usage is unknown, Used in my presets and Contains are disabled, and no firmware
metadata is changed by browsing. Connect the device in Config, then sync to
obtain a scan with the detected firmware. Model and firmware are stored in the
shared library and displayed read-only; firmware is never entered or inferred
from the FM9 model name. The Gen-3 discovery sequence reads firmware with function
0x08; see [protocol evidence and limits](../FRACTAL-DEVICE-INFORMATION.md).
Known unsupported firmware and other device families do not receive this preview
fallback. Old scans with missing firmware are not retroactively labelled 12.00.

The 2026-10-02.2 attribution review checks all 331 names in the supplied CSV and
records two sources per row, with additional official Fractal sources where
applicable. Ampdex and the Wiki share source lineage; this is explicitly labelled
corroboration, not independent confirmation. The review preserves conflicting
claims and distinguishes physical amps, modified references, schematic bases and
virtual originals. See [the complete review](catalog/fm9-12-amp-review.md) and
[row-by-row evidence](catalog/fm9-12-amp-identities.json).

Catalogue 2026-10-03.1 updates Porta-Bass to Ampeg B-15 Portaflex using the
archived Axe-Fx III Ares 12.05 model table. Exact B-15 revision/year remain
unconfirmed in the evidence notes. The historical attribution does not change
the candidate numeric ID or establish FM9 firmware availability.

Catalogue 2026-10-03.2 gives official Fractal product attributions precedence over
conflicting third-party guides. PVH 6160 Block Crunch shares the 5150 Block Letter
family with Block Lead. Channel detail is attributed to the current Wiki; the
official historical manual establishes the family, not the later Crunch channel.

Catalogue 2026-10-03.3 applies the user's supplied follow-up research with source
scope and inferences retained in the inspector. Chiefman 1/2 share Chieftain;
Vibrato Verb Custom stays separate from stock Vibroverbs. USA MK IV Lead and
Fox ODS Mid have separate follow-up mappings, preserving the CSV heading and
Deep row. B-15R and Princeton AA1164 are explicitly inferred. Friedman 2010 and
AC30 Bright findings initially remained in the review pending device-confirmed joins.
See [follow-up findings](catalog/amp-attribution-followup.md).

The direct device read confirmed all earlier observed IDs and resolved the missing
Class-A 30W Bright (233), Friedman BE 2010 (287) and Friedman HBE 2010 (288).
It added 52 names omitted by the old table and confirmed the firmware-12 rename
of ID 283 to Deluxe Tweed Bright. The complete table maps to 162 family/original
groups. Of the 331 supplied entries, 329 now have confirmed numeric joins. The
combined USA MK IV LEAD/RHYTHM heading and Fox ODS Deep row remain distinct from
the selectable Lead and Mid entries. No CSV positions are treated as DSP IDs.
Real amplifier details retain their attribution qualifications independently of
numeric confirmation. Catalogue updates do not rewrite saved preset snapshots.

The device confirms '59 Bassguy RI Jumped as ID 302. It is grouped separately
from the original 5F6-A reference. Fractal's official FM9 12.00 release notes
support the five new selectable names at IDs 331-335 and the Deluxe Tweed rename.
Separate FM3, Axe-Fx III and other firmware rosters still require direct evidence.
See `Catalog/NOTICE.txt` for provenance, licences and modifications.

Usage counts deduplicate by saved preset, across all blocks/channels. A number
including zero is definitive only for a complete committed scan of the matching
device/firmware, covering every slot without errors. Partial scans show usage
unknown and an observed lower bound where available. Old committed scans remain
usable during an unfinished scan; their scan date is displayed. A firmware
change prevents counts and filters from joining incompatible IDs.

## Personal references

Wiki and References appear only in the family detail. Add Wiki link accepts
HTTPS pages on `wiki.fractalaudio.com`; it rejects other hosts, credentials,
nonstandard ports and executable/local URI schemes. Links are deduplicated and
stored atomically in `amp-references.json` beside the app's settings, with a backup.
They are keyed by stable family or device/firmware-qualified variant IDs.

Relevant personal links travel in the existing profile settings/favorites ZIP
through `PortableAmpReferences`. Import keeps the catalogue untouched, and the
host merges the links into the personal store when loading that profile. A full
settings-directory backup includes `amp-references.json` and its `.bak` file.
Catalogue JSON and upstream license/notice files ship with the optional module.

## Verification

Automated tests cover catalogue isolation, distinct counts, incomplete scans,
firmware mismatch, unknown IDs, unused models, family and exact-variant Contains,
saved-scene narrowing, back navigation, scroll preservation, keyboard isolation,
Wiki URL validation, deduplication and profile export/import. Headless UI captures
exercise Light/Dark themes at 1000 and 1440 pixels. Firmware tests cover read-only
library metadata, malformed and fragmented replies, stale-version rejection,
changed-version resume and preservation of the previous committed scan. The
firmware and amp-name table queries were also read directly from the FM9 on
2026-10-03. The active preset, scene, current type reply and full current Amp
block data matched before and after the roster read. The 91 targeted browser,
index-core and index-workflow tests passed after the catalogue update.
