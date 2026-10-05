# Fractal preset index and Amps page: user experience research

Research status: 2026-09-30. This is a UX recommendation for the optional, read-only Fractal index, not a production UI implementation. Hardware support remains subject to the protocol gates in [fractal-protocol-research.md](fractal-protocol-research.md).

## The organizing problem

A user may have several physical processors and several PresetMaestro profiles. A profile can be relevant to more than one processor, and a processor can be relevant to more than one profile. Inside each processor are hundreds of presets, each with up to eight scenes and up to two Amp blocks with four channels. Showing every combination as a separate row would make the same preset appear many times and obscure what a search result actually means.

The recommended mental model is:

| Concept | User-facing meaning | Owns |
|---|---|---|
| **Device** | This particular FM9 or Axe-Fx III unit | One cached scan and its freshness/completeness; physical preset slots |
| **Profile** | This PresetMaestro setup and its performance context | Existing Favorites, MIDI mapping/display offset, and profile-specific tag assignments |
| **Profile–device association** | This profile is intended for use with this particular unit | A many-to-many relationship, with explicit association and verification status |
| **Tracked preset** | The user's idea of a preset as it is renamed/moved/edited | Local logical identity, subject to reconciliation; never inferred from slot/name alone |

**Recommendation:** scan each device once and share its cached index among its associated profiles. The active profile determines the default *view* and profile-specific tags, while the selected device determines which physical preset data is shown. Neither profile name, MIDI port name, device model, nor device's editable name is sufficient proof of a unique physical device. If no trustworthy device identifier is available, make the initial association user-confirmed and show its confidence. The existing profile-switch warning remains relevant; an index association must not silently override it.

The current application stores one device model/name and slot-keyed name caches in each profile, and its Favorites and tags are profile-local. Copying a profile copies those caches and Favorites. Its current switch check compares model/name and cached names against a connected device. Those are valuable compatibility clues, but they are not a durable many-to-many identity system (`PresetMaestro/Core/ProfileSettings.cs`, `PresetMaestro/Core/ProfileStore.cs`, `PresetMaestro/MainWindow.ProfileValidation.cs`, `PresetMaestro/Core/Favorite.cs`). The current switch check would warn on a second valid unit with a different name, and a Favorite's bare preset number could trigger a different sound there. A usable many-to-many design must address both cases, not just add a device dropdown.

## Documented precedents and what they imply

### Preset details dismissal (reviewed 2026-10-04)

The current side-by-side preset inspector follows the [Microsoft Fluent 2 inline drawer pattern](https://fluent2.microsoft.design/components/web/react/core/drawer/usage): supplemental details remain beside the preset list. Fluent's anatomy places the title and optional close control in the header, with a separate scrolling body; it allows a sticky header for long content. Its guidance also calls for predictable placement and concise action labels.

PresetMaestro applies this with an X and **Close details** at the top-right edge of the details header. The header remains visible while scenes and amp models scroll, and preset actions occupy their own row beneath the title. Top-right placement, the visible text label, Escape dismissal within the pane, and focus return to the originating preset are this application's implementation choices. Closing keeps the active search and returns the space to the preset list without sending MIDI. Verify the header remains reachable at the supported narrow size in both themes.

The close X uses a centered 12-pixel vector icon with an 8-pixel gap to its label, avoiding the baseline offset of a multiplication text character. This is an Avalonia implementation choice, retaining the existing theme, button and accessible name. [Fluent 2 button guidance](https://fluent2.microsoft.design/components/web/react/core/button/usage) documents concise action labels and the meaning of Close; the icon and text form one dismissal control.

**Back to scenes** is placed beside **Amp models** in the fixed header action row, rather than below the amp table. Fluent 2 drawer guidance documents header quick actions such as Back and sticky headers for long content; [Windows navigation guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview) calls for consistent navigation. Using persistent section-jump buttons in Avalonia's existing header is a product choice, not a requirement to introduce a NavigationView. Both section actions stay visible while the body scrolls, work with keyboard activation and remain separate from Close details. No deliberate deviation from these patterns is intended.

Verification on 2026-10-04 inspected rendered light and dark layouts at 1440×850 and 1000×680. Regression checks cover dismissal after scrolling, Escape and focus return, scene invocation across child boundaries, keyboard invocation and independent embedded button actions. The scene double-click failure was also reproduced in the running Windows app; desktop control was stopped by the user before a live retest of the updated build.

### Library update decision (reviewed 2026-10-04)

[Windows dialog guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) calls for a simple blocking question, concise specific actions and a safe cancellation choice. [Fluent 2 dialog guidance](https://fluent2.microsoft.design/components/web/react/core/dialog/usage/) documents modal blocking, focus containment and return, persistent headers/footers, and avoiding nested dialogs.

PresetMaestro's update confirmation belongs to the open **Sync with connected device** window through Avalonia's owned modal `ShowDialog` API. It shows the unmatched preset count instead of percentages and thresholds. **View differences**, **Update library**, and **Cancel** have short descriptions beside them. Review replaces the decision content inside this same confirmation window, showing preset numbers, names, saved/connected values, read errors or an explicit exact-change limitation when only a fingerprint differs. A fixed **Back** button returns to the decision; review performs no new device reads or writes. Escape returns from review, or cancels from the decision. Closing returns focus to Sync library after synchronization actions become available again.

Deliberate deviations: the user requires the confirmation to remain above the sync window, so this owned confirmation is nested within the existing sync dialog. Review reuses that confirmation instead of opening another dialog. Actions are stacked with explanations beside each button, with Cancel last, instead of a horizontal footer; this is the user's requested presentation. Light/dark rendering at 1440×850 and 1000×680 and interaction regressions cover review, focus, counts, cancellation, accepted updates and preservation of saved data before acceptance.

### Scene amp names (reviewed 2026-10-04)

[Windows writing guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style) recommends familiar wording and giving the key information the most visibility. [Fluent 2 text guidance](https://fluent2.microsoft.design/components/web/react/core/text/usage) documents consistent text presets and typography roles. PresetMaestro's product choice is to lead each scene's saved amp summary with the exact Fractal model selected by that scene's channel, instead of the uninformative block number. The channel and existing off marker follow the model name. Each Amp block uses a separate line; automatic row height and wrapping keep both names readable at supported narrow sizes. The scene's accessible name includes the same summary. Existing typography and task controls are retained, with no deliberate deviation from this guidance. Rendered light and dark views at 1440×850 and 1000×680 cover two Amp blocks, differing scene channels, and long model names; existing scene invocation tests check keyboard and pointer behavior after rows resize.

### Scene invocation (reviewed 2026-10-04)

[Windows mouse guidance](https://learn.microsoft.com/en-us/windows/apps/develop/input/mouse-interactions) documents routed pointer and double-tap events; [Windows list guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/lists) distinguishes selection from item actions. PresetMaestro's product choice is single-click selection and double-click invocation, with explicit **Go to**, a context-menu action and Enter on a focused scene row as alternatives. Each scene's label, amp summary and background form one target. Avalonia pointer click counts recognize a double-click that crosses child boundaries; the two clicks must select the same scene. Embedded Tags and Go to buttons keep their own actions. Invocation uses the existing connected-device checks and MIDI mapping. These are product and framework implementation choices, not Microsoft requirements; no deliberate departure from the documented patterns is intended.

| Source | Documented pattern | Application to PresetMaestro |
|---|---|---|
| [Fractal FM9-Edit](https://www.fractalaudio.com/fm9-edit/) | Presets and scenes are managed in a dedicated editor; the whole preset can be viewed before opening block details. | Keep PresetMaestro's index as a browser: preset summary first, scene/Amp detail on selection. This page establishes the editor pattern, not an offline search or profile model. |
| [Line 6 HX Edit Pilot's Guide 3.80](https://line6.com/data/6/0a00051aff0e673cd37585ccd/application/pdf/HX%20Edit%20Pilots%20Guide%203.80%20-%20English%20.pdf) | Device presets live in a device library, while exported presets form a computer library; device models expose different preset/snapshot capabilities. | Separate the synced device inventory from user organization and adapt available controls to device capabilities. HX Edit's automatic two-way sync is *not* a precedent for this feature's explicit, read-only scan. |
| [Ableton Live 12 Browser](https://help.ableton.com/hc/en-us/articles/12927340213660-The-Live-12-Browser) and [Browser/Tags FAQ](https://help.ableton.com/hc/en-us/articles/11425042663708-Browser-and-Tags-in-Live-12-FAQ) | A content scope precedes keyword search; filters and tags narrow the list; user tags and saved browser views help reuse complex searches. | Offer a clear scope, a single search box, a short set of meaningful filters, and reusable views only if real use warrants them. Distinguish clearing text from clearing all filters. |
| [Adobe Lightroom Classic collections](https://helpx.adobe.com/lightroom-classic/desktop/organize-photos-in-lightroom-classic/photo-collections.html) | One photo can belong to multiple collections; removing membership does not delete the photo. | A profile's association with a device or preset should be a relationship, not a copy of device data. Removing an association must not delete the cached device scan or another profile's tags. This is an analogy, not a music-device feature. |
| [Nielsen Norman Group: Progressive Disclosure](https://www.nngroup.com/articles/progressive-disclosure/) | Show common choices first; reveal less common detail when requested. | Default to preset results; open scene rows and the Amp/channel matrix only for a selected preset. Keep parser and identity diagnostics behind details. |
| [Nielsen Norman Group: Helpful Filter Categories and Values](https://www.nngroup.com/articles/filter-categories-values/) and [MoJ Design System: Filter](https://design-patterns.service.justice.gov.uk/components/filter/) | Filters should be predictable, domain-relevant and limited to what users need; long option lists benefit from type-ahead. | Start with device/profile scope, Amp identity, scene usage, and tags. Make long amp/tag lists searchable and show active filters with a clear action. |

These sources support the interaction pattern. None specifies how PresetMaestro should bind its own profiles to Fractal units or reconcile preset lineage; those are product decisions below.

## Proposed experience

### Library check differences (reviewed 2026-10-04)

Current guidance: [Fluent 2 message bar](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage)
calls for specific, actionable feedback with supplementary details and wrapped
content; [Windows writing style](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style)
calls for concise explanations that help people decide what to do next. The app
keeps the existing status surface, adds **What differs** immediately beneath it,
and preserves the separate **Next step** section and fixed dismissal footer.
Before/after values have readable labels and wrap instead of truncating. Long
preset lists use a bounded scroll area. Escape and Done dismiss the dialog.
These are Avalonia product choices; Fluent does not prescribe this exact diff
layout. No new confirmation gate is added. Inspection covers light/dark themes
with 1440- and 1000-pixel main windows and the supported 650-pixel sync dialog.
Regression checks exercise actual failed checks, independent baseline/name
preservation, keyboard dismissal and persisted evidence.

The app now makes sample scope and baseline age explicit. Bypass states are
excluded by new comparison fingerprints at the user's request. Older image-only
fingerprints cannot identify bypass-only differences or reconstruct unindexed
effect parameters; explain that limit and offer a deliberate library refresh.
Never present a content mismatch as proof that a different physical unit is
connected. See [diagnosis and comparison rules](library-check-diagnostics.md).

### Selected amp details and result summaries (reviewed 2026-10-04)

**User-directed flow:** after choosing an amp, this is a focused details view of that amp. The user returns through **Back to Amps** to search again or choose another amp. Show the chosen amp as a plain heading, such as Energyball, followed by Presets containing this amp and the matching preset list. Remove the Amp filter label, removable Contains chip, Clear amp filter action, Match in dropdown/radios, and matching-rule helper. Do not repeat the amp-search controls here. This replaces the earlier filter-form proposal; the internal matching mechanism must not dictate the user-facing layout.

Relevant current primary guidance:

| Source | Documented guidance | Application proposal |
|---|---|---|
| [Fluent 2 layout](https://fluent2.microsoft.design/layout) | Proximity establishes relationships; whitespace groups content and creates hierarchy. Use a consistent spacing ramp. | Separate navigation, the selected amp heading and matching presets. Use quieter secondary text. |
| [Fluent 2 button](https://fluent2.microsoft.design/components/web/react/core/button/usage) | Use restrained appearances for minor actions; links can serve navigation. | Keep a clearly labelled Back to Amps navigation action, using existing Avalonia controls and theme. |
| [Windows list/details](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/list-details) | Selecting a result updates its details; use layout appropriate to available width. | Keep a short summary in the result and expose exact channels and Fractal variants in the existing inspector. Preserve selection, device ordering and keyboard navigation during reflow. |

**Recommended product choice:** selected amp details plus compact preset summaries. For the supplied Energyball examples, show 186 Uber Chuggas with Amp 1 · channels A–D, and 196 Blitz III with Amp 2 · channels A–D. The selected model is already named in the heading. Aggregate only matches sharing the same Amp block and exact model, and use A–D only when all four channels match; otherwise enumerate the actual letters, such as A, C. When a broad family matches different Fractal variants, include the variant name once per group or move additional groups into details; do not erase distinctions. These are stored-content summaries, never proof that every scene selects or enables the amp.

Back to Amps preserves catalogue search, selection and scroll position. Direct Preset Index can retain its general-purpose preset search; the selected amp details view does not repeat it. This is the current product decision and supersedes older recommendations below for exposing an arriving Contains filter and scene-matching controls. Keep the saved-settings/Scene Ignore/unsaved-edits qualification with the scene-state details it qualifies. Check labels, keyboard access, focus return and reflow in light/dark themes at normal and narrow widths before implementation. Removing unnecessary controls and aggregating channel matches are user-directed application decisions; the guidance supports the hierarchy and list/details pattern. No deliberate departure from those patterns is intended.

Implemented in the Avalonia app on 2026-10-04. The selected amp view also hides idle library management and sync notices so the hierarchy stays focused on the selected amp and its presets; active sync progress and cancellation remain available. This is a product choice, not a Fluent requirement. General preset/tag search is preserved independently and restored by direct Preset Index navigation. Back returns focus to Find presets in the amp details. Regression checks cover saved-channel matching regardless of programmed scene state, exact-model/block grouping, noncontiguous channels, independent search restoration and navigation without MIDI. Rendered app inspection covers light/dark themes at 1000 and 1440 pixels, including preset details.

### Agreed navigation direction (2026-09-30)

Give **Amps** its own top-level page alongside **Preset Index**. Amps is the device's browsable capability catalog: manufacturers, real amp families, Fractal model variants, and a count of saved presets containing each family. It opens on the complete firmware-supported roster, including models found in zero presets. Preset Index remains the browser for saved preset content. Both pages share the selected device context; changing PresetMaestro profiles does not change which Amp models the hardware supports.

On an amp family or exact variant, **Find presets containing this amp** opens Preset Index with an explicit **Contains** filter and the same device selected. The result list identifies which Fractal variant and Amp block channel matched. A Back action returns to the Amps page with its manufacturer, family, search text, and scroll position preserved. The button must not say **uses**: a saved Amp channel can contain a model without any scene selecting or engaging it. Scene selection/engagement can be narrowed later in Preset Index. An available model with zero saved matches remains visible on Amps and leads to a clear empty result, not disappearance from the catalog.

### Existing screen layout precedent (reviewed 2026-09-30)

The application's own **Select Preset** window is the stronger visual precedent for a large catalog. PresetSelectionWindow.cs starts at 1280 × 780, can shrink to 640 × 420, keeps search across the full width, and presents 34-pixel entries in a resizable multi-column ListBox. PresetSelection.ColumnsForWidth reserves roughly 220 pixels per column and chooses two to five columns. ArrangeColumnMajor fills down each column so the visible area shows several ranges of preset numbers at once. The footer keeps count and actions visible. The screenshot supplied by the user shows five columns and 279 catalog entries without devoting permanent width to a detail pane.

The Favorites page provides the companion pattern: FavoriteTableLayout.TableCount adds equal-width tables as viewport width grows (440-pixel minimum target with 18-pixel gaps), and FavoriteTablesPanel distributes rows across those columns. A resize rearranges existing controls while preserving selection and focus. This is a useful pattern for **using the entire window**, though the preset index should not show all channels as rows.

**Revised index layout recommendation:** use the full content width for a dense, responsive, column-major preset catalog with a single broad search field styled and positioned like the existing Favorites search. Render **one compact entry per preset** and preserve device order within columns; search reduces the entry set. Keep profile and device selection in their existing app-level placement, as on Favorites; do not duplicate them inside Preset Index. Show scan age in one quiet status line. Do not add a multiple-select button or an **Amp match** dropdown. Open a selected preset in a temporary full-width detail view or overlay containing the Amp channel matrix and eight scenes. A three-block, four-channel matrix is a useful UX stress case, even though the researched FM9/Axe-Fx III targets have two Amp blocks. Closing detail returns to the same filtered list, scroll position, and selection. The number of columns depends on available width, with a readable minimum cell width; do not reserve a permanent detail sidebar. In code, keep the search/index model independent of the visualized entries. The existing picker clears and rebuilds all ListBoxItems on filter/reflow; a richer 1024-preset index should preserve result identity and use virtualization or another bounded visible-item strategy after profiling rather than multiplying UI controls by block/channel/scene combinations.

### 1. Enter and choose context

The optional **Preset Index** page uses the profile and device selected in PresetMaestro's existing app-level controls, in the same place used while viewing Favorites. The page itself starts with its title, a Favorites-style search row, result count, and a quiet scan status. It does not show another profile or device dropdown.

The default device is the currently connected, verified unit if it is associated with the active profile. If there is no connected unit, use the last chosen associated device's cached scan and label it **Offline**. If several devices are associated, the user chooses one. A separate **All devices** search is available for discovery, with each result visibly identifying its device; it is not the default. A profile can be used with no associated device, but the page then asks the user to choose a known cached device or associate the connected one. Association is an explicit action and may be removed without deleting device scans or other profiles' work.

Profile and device remain two independent app-level selectors. Switching profiles changes Favorites context and visible profile tags; it does not rescan the same unit. Switching devices changes the cached inventory; it does not silently change the active PresetMaestro profile. The current one-name profile validation must eventually check the confirmed profile–device associations and compatibility, with a clear **unverified** state when identity cannot be established. Until that behavior exists, the index should show the existing warning state and restrict device-directed actions; it cannot claim that an association alone makes profile switching safe.

### 2. Search one preset at a time

The first row represents one **preset on one device**, not one scene/channel permutation. Show displayed slot number, preset name, and profile tags inline as on Favorites. Reuse the Favorites search row and its exact user-facing controls: one field for a tag or search term, **Tag Match: Any tag / All tags**, and **Clear search**. Reuse its interaction too: known tags appear as suggestions; an exact tag previews as a tag filter; Enter commits a tag or text-search chip; chips sit inside the input and can be removed; Clear search clears chips/text and resets to All tags. A typed term filters preset number, name, or visible tag text; selected tags filter tag assignments. Any/All applies to multiple selected tags, while the typed term still narrows the result. Show the live result count below. An Amp family or variant filter is applied here when arriving from Amps and displayed only while active, not as a permanent dropdown. Scene/Amp details remain available after opening a preset. The following relationships still need precise semantics in the results and detail views:

| Control | Question answered | Scope |
|---|---|---|
| Amp selected on Amps page | Which preset contains a Marshall JCM800 or Brit 800? | **Contains** in any saved Amp channel; arrives with an explicit filter |
| Scene uses amp | Which scene actually has that Amp block engaged? | **Uses** in a selected channel and unbypassed block, only when statically knowable |
| Scene selects amp | Which scene selects that channel, even if bypassed? | **Selects**; an explicit advanced option |
| Tag | What did I mark Live or Needs Work in this profile? | Profile-specific assignment |
| Scan state | Which results are stale, partial, or uncertain? | Device snapshot quality |

Do not label a preset simply “uses JCM800” because it might only **contain** that model on an inactive channel. The Preset Index search row should not expose a separate **Amp match** control. When navigating from Amps, use an explicit **Contains: [amp family]** filter and show which variant matched in the result detail. Clear search text and clear the arriving Amp filter as separate actions. If Scene Ignore or a decoder gap prevents a reliable `USES` answer, mark that scene **cannot determine** and do not count it as a definite match.

**Discovering amp models:** the Amps page shows an **already populated directory of every Amp model available for the selected device and firmware**, including those used in zero saved presets. The default is **All available amps**; **Used in my presets** is an optional narrowing control. Group real amp families by manufacturer across the available width, with recognizable Fractal names, variant counts, and a separate number of saved presets containing each family. The user can discover that a Bassman exists even if they have never used one. Search starts empty and is only an optional way to narrow the visible directory; it matches manufacturer, real name, and Fractal name. Switching the selected device or its firmware changes the available roster and counts; switching profiles on the same device does not. Include Fractal-original models as their own group, and show unrecognized model IDs under **Unmapped** with their raw Fractal identity and an explicit review state. Do not invent a real-world name for an uncertain mapping.

The preset scan alone reveals only models **used** in saved presets. A complete **Available on this device** directory therefore requires a validated model roster for its hardware and firmware, obtained from a reliable device response or a maintained versioned catalog. A general name-mapping table is insufficient proof that a model is supported by the connected firmware. When roster coverage is unverified, label the directory incomplete instead of implying that absent models do not exist. An amp may show **0 presets** only when the selected device has a complete committed scan; for a partial scan, show **usage unknown**. Any catalog should expose the original Fractal name and confidence/source for the real-amp translation, and a firmware change should trigger roster and mapping review before claiming certainty.

**Model families and variants:** group Fractal model IDs by the amplifier a musician would recognize. The parent row is selected by default and matches any mapped variant; an expansion reveals exact Fractal models for people who need one input, mode, channel, or revision. For example, [Fractal's Fullerton amp descriptions](https://shop.fractalaudio.com/products/icons-fullerton-complete) list **59 Bassguy Bright**, **Jumped**, and **Normal** for the original Fender Bassman 5F6-A, plus **59 Bassguy RI Jumped** for a later reissue. A broad **'59 Bassman family** can contain all four, but the original and reissue should remain visibly distinguishable and individually filterable. Do not silently treat the reissue as the identical underlying amp. This hierarchy should be curated and versioned; name-prefix matching is too weak for many amp families and modified models.

**Reference links:** keep real-amp family and Fractal-variant mappings curated and versioned, rather than putting a mapping editor in the normal UI. A mapping change would alter search results and grouping for everyone using that catalog. The [community Fractal Wiki's Amp models page](https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models) already contains model descriptions, but it notes that the published list can differ from a particular unit's firmware; treat it as supplementary reading, not proof that a model is available on the selected device. Add one quiet **Wiki ↗** link in the expanded amp-family detail, never on every catalog row. If there are several relevant pages or variant-specific sections, use a small **References (n)** action there; its popover lists the links and contains **Add Wiki link…**. User-added links annotate a stable family/variant ID and do not change the mapping or device roster. Validate each URL and open it externally. No reference control appears in Preset Index results or in the collapsed Amps directory.

The family count is the number of **distinct presets** containing at least one member, not the sum of variant counts: one preset may contain Bright and Normal on different Amp block channels. A second drill-down can show which variant, Amp block (1 or 2), and Amp block channel (A–D) matched. Call these **Fractal model variants** and **Amp block channels** respectively; the latter is a preset storage/selection dimension, not another real-amplifier model. Search for any child Fractal name should find its family while preserving the option to select just that child.

### 3. Reveal the relevant scene and channel detail

Opening a preset shows eight scenes as compact, single-line rows below the preset list, with row height/spacing aligned to the list above. Each row shows scene number, name, its own tags and an **Add tag** action. The matching scene is highlighted. Amp 1/Amp 2 selection and bypass remain available on scene inspection without making every scene row tall. Opening a scene reveals the precise path, for example:

> Scene 3 “Lead” → Amp 1 channel C → Brit 800 → Marshall JCM800 2204 → engaged

A secondary **All Amp channels** view shows the A–D inventory for each Amp block. This separates “exists somewhere” from “selected by this scene” and “actually engaged”. A result found by real amp alias should also display the original Fractal model name so users can verify the translation. An uncertain real-amp mapping is visibly marked and searchable by its original Fractal name.

### 4. Tags, Favorites, and profile reuse

**Catalog visibility:** use the same tag chips as Favorites, directly after the measured preset name rather than in a separate tag column. Keep the Favorites chip surface/text colors, 22-pixel height, rounded corners, and spacing in both preset rows and search chips. Show as many tags as fit on one line and clip the remainder, with the complete list available on hover/focus and in preset detail. The same search field can find a chosen tag without expanding all 1024 rows. Keep number and name readable before allocating width to tags. The relevant existing implementation is `MainWindow.Favorites.cs` (`BuildFavoriteTagChip`), `FavoriteTitleTagsPanel.cs`, `MainWindow.FavoriteSearch.cs` (`RenderSearchChips`), and the `TagBrush`/`TagTextBrush` theme resources in `MainWindow.ApprovedLayout.cs`. Extract or reuse that presentation when implementing Preset Index; avoid a separate tag style or “+N” summary.

**Scene tags:** a scene tag belongs to a scene, not automatically to its enclosing preset or to a Favorite pointing at that scene. In the preset list, show a restrained `S1 Clean` style marker after preset tags, limited to what fits plus a small overflow count; the `S#` prefix distinguishes scene tags without relying on colour. The scene detail shows all eight rows and their full tag assignments. A tag added to a scene appears immediately in that preset's list entry and in search. **All tags** applies across the preset and all its scenes as one result; it does not claim that every selected tag is on the same scene. The [data design](fractal-index-data-design.md) defines identity, persistence and import/export for these assignments.

**Single-preset flow:** select a preset, open its detail, and use **Add tag** beside its existing tag chips. The entry accepts arbitrary text with suggestions from tags already used in the current profile; Enter or **Add tag** saves locally. The existing app-level profile/device context determines where the tag belongs. A chip can be removed there, and the search field immediately finds matching presets. Tagging works against the cached index even while the hardware is offline and never sends MIDI. The catalog has no multiple-select mode or bulk-tag action.

Arbitrary tags are local. **Recommended default:** assign a tag to the `(profile, device, tracked preset)` relationship. That fits the existing profile-local Favorites/tags and allows “Live” in one profile and “Recording” in another, even when both profiles use the same FM9. The tag text may be reused across profiles, but assignment does not silently copy. A shared device-wide tag layer could be introduced later if users need it; the first UI should not force them to choose a tag scope on every edit.

The existing **Add Favorite** action remains profile-local and scene-specific. A Favorite currently holds one displayed preset number and scene with no device identity. For a profile associated with two units, adding slot 43 from unit A could later invoke unrelated slot 43 on unit B. Therefore, the index-to-Favorite shortcut should **not** be enabled for a multi-device profile until the app can bind or validate that Favorite for each intended device. For a single verified profile–device pairing, it may prefill the existing editor with the current profile's display offset. Offline and cross-device results can still be browsed and tagged, but device-directed actions must not assume that the visible slot exists on the connected unit. Favorites tags and preset tags share the user-facing vocabulary and search controls, but their assignments belong to different objects; copying one assignment to the other requires an explicit action, if ever added.

Copying a profile should offer a clear choice to copy its index associations and tag assignments, while reusing the same device scan. Importing a profile should begin with unverified associations unless the user confirms the intended local device. Renaming a profile must preserve its associations; deleting one removes its tag assignments/relationships according to the user's chosen data-retention policy, without deleting device scans used by other profiles. These lifecycle rules need stable profile identity or explicit rename/copy/import/delete integration; using the profile's display name as a permanent key is fragile.

### 5. Sync and ambiguity are visible states

**Sync device** is an explicit action on the selected device, available through the app's device controls rather than a repeated selector row on Preset Index. Scan progress shows slots, allows cancellation/resume, and retains the previous complete snapshot if a run fails. Search always uses committed cache data, with “last complete scan” and partial/failed status visible. Profile switching never starts a new device scan. A user-selected slot range or quick name sweep should be marked partial; a name match must not be presented as proof that Amp content is current.

When a rescan suggests a moved, copied, overwritten, or restored preset, keep tags attached to their prior tracked identity and surface a small **Needs review** count. The review view shows old/new device, slot, name and evidence and offers explicit **Same preset**, **Copy tags**, **Different preset**, and **Later** decisions. Do not interrupt every search with a modal; do not silently reattach tags from a same slot or similar name. Uncertain results remain findable, but marked.

## Concrete many-to-many examples

| Situation | Expected behavior |
|---|---|
| Profiles **Live** and **Recording** both use one FM9 | One FM9 index scan. Each profile sees the same preset facts, with its own tags and Favorites. Switching the index view does not rescan; the app's existing profile validation may still perform its own name checks until revised. |
| Profile **Live** uses two FM9 units | Device selector chooses which cache is searched. “Preset 043” on unit A is distinct from “Preset 043” on unit B. A Live tag on A does not jump to B by slot or name. Existing Favorites need per-device validation before they can safely be treated as shared. |
| One preset is copied from unit A to unit B | Two device records and distinct logical preset identities. Offer an explicit tag copy/link decision if similarity is detected; never silently merge them. |
| Unit is offline | Search its last committed cache and show scan age. Labeling remains possible in the selected profile; Add Favorite or any device action requires the correct connected unit. |
| A profile is exported/imported on another computer | Existing profile JSON/Favorites travel as they do today. The index association and tags need an explicit export/import policy; otherwise show them as unavailable and ask the user to bind a local device. Do not pretend a matching port/name proves identity. |

## Design acceptance checks before UI implementation

1. With two profiles on one device, a scan runs once; the same preset facts appear in both, while tags and Favorites stay in their own profile.
2. With one profile on two devices, slot 43 and its tags never cross devices without a deliberate user action.
3. A search for **Contains**, **Selects**, and **Uses** gives different, explainable results for a saved preset whose scenes switch channels and bypass an Amp block.
4. Disconnected, stale, incomplete, unknown firmware, uncertain Amp mapping, and unresolved identity each have distinct visible states and no misleading definite `USES` result.
5. Profile copy, rename, delete, import, export, and manual rescan have defined association/tag behavior before persistence is built.
6. A cached result can create an existing Favorite only for a single-device profile with the correctly verified connected unit and the profile's display offset applied. Multi-device profile support requires a defined per-device Favorite rule first.
7. Amps opens independently of Preset Index, includes firmware-supported models used in zero presets, and uses explicit **Contains** wording when navigating to matching preset results. Back restores the same Amps browsing position and selection.
8. Preset Index uses the existing global profile/device placement and the Favorites search row, including **Tag Match: Any tag / All tags**, **Clear search**, and result count. It contains neither **Select multiple** nor an **Amp match** dropdown.

**Recommended first flow to prototype with users:** open Amps on the full available roster, browse to a real amp family without typing, inspect its Fractal variants, and choose **Find presets containing this amp**. Preset Index opens on the resulting saved presets and can narrow by scene selection/engagement, tags, and scan state. Test tasks such as “discover whether this FM9 has a Bassman model,” “find every scene that engages my JCM800,” “which preset did I tag for recording,” and “find this sound on my other FM9.” Add saved searches only if those tasks show a need.
