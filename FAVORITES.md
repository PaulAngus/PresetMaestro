# Favorites implementation notes

The approved layout is Design 6 without range-header bands. The application shell, MIDI commands, profile services, palette and compact row construction remain the shared implementation.

## Layout and commands

- `MainWindow.FavoriteTables.cs` supplies repeated four-field headers (Slot, Name, Preset, Scene) and one ListBox template with a shared ScrollViewer. The six-pixel gutter is always reserved to prevent scrollbar breakpoint oscillation. Headers and data scroll together.
- `FavoriteTablesPanel` places existing row containers down balanced tables. `Core/FavoriteTableLayout` computes ranges by count, using 440 logical pixels as the minimum table width and an 18-pixel gap. A count/viewport change invalidates arrangement without replacing containers. Header layout updates are coalesced outside measurement, and their overlay does not contribute stale width to the next measurement.
- Native window size changes queue a coalesced layout invalidation while Favorites is visible. This prevents stale shell and toolbar measurements after shrinking at fractional DPI scaling, without replacing controls or losing focus.
- The ListBox remains the only selection owner. Refresh restores by favorite ID, including after temporarily hiding a row with search. A hidden selection is not returned by `SelectedFavorite()` and cannot be sent through the visible selection command.
- Resizing header fields updates the shared width model and every repeated header/row. Header separators do not appear inside data rows. Cached names wrap in neutral detail strips; tag pills follow each title in the shared Name field using `FavoriteTitleTagsPanel`. Short titles leave more room for tags; long titles ellipsize while retaining at least 55% of that field. Compact tag displays stay inside the main row with full text available in a tooltip.
- Sorting is global before distribution; headings cycle ascending, descending, and default order. Drag/drop is disabled with active search or header sorting. Default-order drops resolve the target by both X and Y against the underlying favorites and preserve the existing renumber-on-reorder/remove behavior.
- Up/Down and Left/Right use the panel's navigation map, including uneven tails. Page keys stay inside the current table. Selection/navigation never invokes MIDI. Pointer double-click remains the single send path and resolves the clicked row through its descendants.

## Search and compatibility

`MainWindow.FavoriteSearch.cs` owns transient typed chips. Tag chips match exactly without case sensitivity, using All/Any. Text chips are substring constraints combined with AND over displayed/cached data. Preview classifies the input using the same exact-tag rule as Enter. The explicit text suggestion lets the user choose text semantics. The live TextBox stays attached while chips/results update, and the entire search area is protected from the global keypad shortcuts. Profile switching clears chips and rebuilds suggestions from the active profile.

`FavoriteJsonConverter` is attached to the model, so standalone serialization, profile copying, ZIP imports/exports and folder imports all share migration. Retired fields exist only in the compatibility boundary. Load retains original slots, IDs and mapped values. `LegacyFavoritesStorage` validates an existing file before replacement and preserves a first `.pre-tags.bak` copy whenever retired fields are present. Existing pre-profile originals and imported source files are retained as before.

## Verification

Run from the repository root:

```powershell
dotnet test PresetMaestro.Tests/PresetMaestro.Tests.csproj --filter 'FullyQualifiedName~Favorite'
dotnet test PresetMaestro.slnx
```

`FavoriteMigrationTests` covers standalone migration, pre-profile initialization, profile saves, exports, ZIP and folder imports, deduplication, repeat round trips, malformed input and backup preservation. `FavoriteTableLayoutTests` covers balanced examples, empty/small/large lists and breakpoints. The Avalonia Favorites tests cover real input events, cross-table double-click, drag/drop, context commands, keyboard navigation, chip semantics, profile reset, global selection, header resizing, cached detail wrapping, row highlights, and existing editor/send behavior.

To generate actual Avalonia rendered fixtures (isolated test data, no live profile writes):

```powershell
$env:FAVORITES_SNAPSHOT_DIR = Join-Path $PWD 'build_test/favorites-screenshots'
dotnet test PresetMaestro.Tests/PresetMaestro.Tests.csproj --filter 'FullyQualifiedName~Favorite'
```

Inspect the 1000/1100, 1500 and 1950 logical-pixel images for two/three/four tables, both themes, all-details and selected-only details, long names/tags, and the 1320-pixel editor. Native desktop verification is also required: a headless screenshot cannot expose all native resize/arrangement issues. Use isolated constructor-injected settings/favorites and a fake MIDI implementation; do not launch against the user's profile merely to populate test cases.

Implementation verification passed all 272 solution tests (262 application tests and 10 probe tests). Rendered fixtures were inspected across the widths/themes above. The native Windows app was also inspected at 150% DPI with two, three and four tables, minimum width after shrinking, both themes, the editor, long text, all-details mode, and chip suggestions/commit/clear. Physical MIDI hardware was not exercised.
