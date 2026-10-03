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

The first shipped catalogue has 284 Apache-licensed open-roster candidates plus
the separately observed FM9 ID 271. Candidate eligibility is deliberately scoped
to FM9 12.00, the version checked in the protocol research. It is **not a complete
validated firmware roster**. The UI labels coverage incomplete and distinguishes
the ten device-verified names from candidates. Other devices/firmware display
their own observed scan identities without borrowing FM9 names.

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

Of the supplied entries, 280 join existing numeric candidates in 146 family/original
groups; 51 have no accepted numeric join. Five existing numeric candidates remain
Unmapped because their names do not safely join the supplied list. List positions
are never treated as DSP IDs. Real model details, relationship type, qualifications
and source URLs appear in expandable evidence. Attribution research does not
promote a numeric candidate to a device-verified name. The underlying saved scan
is not rewritten when catalogue names or mappings change.

The known missing '59 Bassguy RI Jumped requires an independently evidenced
firmware-specific ID before it can participate in Contains queries. Do not invent
one or treat the reissue as the original 5F6-A. Complete FM9 coverage and separate
FM3/Axe-Fx III rosters remain data-validation work; the catalogue must remain
labelled incomplete until that work is done. See `Catalog/NOTICE.txt` for pinned
source provenance, license and modifications.

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
firmware query is source-verified; a live FM9 reconnection and fresh scan remain
necessary to verify the complete path on the user's hardware.
