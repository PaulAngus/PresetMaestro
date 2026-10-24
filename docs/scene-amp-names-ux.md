# Scene amp name display

The scene details pane in **Presets and scenes** always shows its eight scenes. The approved **Replace the tabs** design replaces the Scenes and Amp models links with **Fractal / Real amp** in the same header grid cell. The separate Amp models detail view is removed. The Amps browser remains available.

## Interaction and layout

Fractal is the initial selection. Real amp uses the existing catalogue attribution for the active channel of each scene, for example `USA JP IIC+ Yellow · B` becomes `Mesa/Boogie JP-2C · B`. Channel letters, bypass markers and one line per Amp block remain visible. An unmapped model reads `Real amp not mapped (Fractal name)`; a preset without Amp blocks reads `No Amp blocks`.

The segmented control uses the existing Any tag / All tags segment widths, surfaces, outline, selected thumb and text colours. Its inner height is 22 rather than 26 pixels so the complete control fits the existing 28-pixel header. The header, two scene banks, row minimum heights and dock sizing rules retain their existing space. Long names shorten with an ellipsis rather than wrap into extra rows; the full name is available through tooltips and accessibility text. Channel and bypass text have their own reserved space.

Switching names updates the scene labels in place. It retains scene selection, scrolling, Go to controls, focus, tags and navigation state, and sends no MIDI. The selection lasts for the window session, including preset changes, closing and reopening details, and switching Presets/Scenes views. It is not a new saved profile setting.

Both choices support mouse, Space/Enter and Tab; Left/Home selects Fractal and Right/End selects Real amp. Clicking the selected choice leaves it selected. Accessible toggle names describe the naming mode and expose the checked state. Close stays at the trailing edge; Escape and focus restoration retain their existing behaviour.

## Design guidance and product choices

[Microsoft Windows radio button guidance](https://learn.microsoft.com/en-us/windows/apps/design/controls/radio-button) describes visible, related, mutually exclusive choices and accessible selection state. [Fluent 2 button guidance](https://fluent2.microsoft.design/components/web/react/core/button/usage/) recommends clear toggle labels and neutral treatment for peer actions. These are behavioural and accessibility references; they do not prescribe this Avalonia layout.

The user approved the segmented appearance, exact labels and use of existing header space on 24 October 2026. Fractal as the default and a window-session lifetime are implementation choices that preserve the existing initial display without adding stored settings. The deliberate adaptation is to retain the application's established tag-match style instead of adding conventional radio circles, a switch or another toolbar row.

## Verification and platform handover

Headless component checks cover both index views, light/dark themes, 1000/1440 widths, mapped/unmapped and multiple/no Amp blocks, all scene channels and bypass, unchanged geometry, selection, scrolling and no sends. These constructed-data renders are development evidence only.

On 24 October, the Release build and scoped format verification passed, together with 156 selected component regressions covering the new display, existing index/header/Amp browser interactions, navigation and preset/scene confirmation. Results are in `TestResults/scene-amp-names-final/`; inspected headless renders are in `build_test/scene-amp-names/final/`, with baseline renders in `build_test/scene-amp-names/before/`. At 1000 by 640, the original and final one-Amp scenes panes occupy the same vertical bounds; the header remains 28 pixels. Both modes also retain identical pane and row bounds for multiple/no Amp blocks. These observations do not qualify a native installed release.

The shared platform handover is `crossplatform-testing/docs/maestro-scene-amp-names-handoff.md`, linked from its permanent requirements catalog and release review. Native functional and hardware acceptance must use the unchanged normally installed future candidate under shared dispatch. The existing 1.2.32 package predates this UI change and remains unchanged.
