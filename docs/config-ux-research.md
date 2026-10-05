# Config numeric inputs

Reviewed 2026-10-04.

## Startup

The app now opens on Config, at its existing 1000-logical-pixel minimum width. Width is taken from MinWidth during construction, so the startup default follows that limit while subsequent user resizing stays available. The initial page and width are explicit user-requested product choices. [Windows responsive layout guidance](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design) recommends adapting to the space available in the app window; it does not prescribe these startup choices. The existing Config layout and theme are retained. Startup and resized Config renders are inspected in light and dark themes, alongside checks that Config opens by default and navigation and resizing remain available.

## Profile settings grouping

[Windows app settings guidance](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) recommends grouping related settings under section headers. [Fluent card guidance](https://fluent2.microsoft.design/components/web/react/core/card/usage/) describes cards as information and actions related to one concept; [Fluent layout guidance](https://fluent2.microsoft.design/layout) uses proximity to communicate relationships. Device library assignment, management and the profile's match threshold now sit inside the Profiles card, beneath a secondary heading with extra spacing. This grouping and spacing are product choices implemented with existing Avalonia controls. Appearance remains a separate card. No deliberate deviation from the relevant grouping guidance.

## Saved library choices

[Windows combo-box guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/combo-box) describes a list of selectable items, with the current selection visible when closed. [Fluent dropdown guidance](https://fluent2.microsoft.design/components/web/react/core/dropdown/usage) recommends short, descriptive option labels. The Assigned library selector and Manage Libraries now share the saved-library catalog and display-name disambiguation. Historical profile references without a saved library file are retained in profile data but are not offered as assignable libraries. Imported portable snapshots and the assigned legacy library are restored by the existing load path before the catalog is read. If a file disappears after the choices are displayed, assignment reports that it is unavailable and keeps the existing assignment instead of creating an empty replacement. This availability policy is a product choice; the existing Avalonia controls, labels and layout are retained. No deliberate deviation from the relevant selection guidance.

The regression fixture reproduces a stale profile reference and verifies both lists, selection, creation, preserved history and offline operation. Config with its selector expanded and Manage Libraries are visually inspected in light and dark themes at normal and minimum widths.

## Numeric inputs

[Fluent 2 spin-button guidance](https://fluent2.microsoft.design/components/web/react/core/spin/usage) documents direct typing, arrow-key adjustment, labelled units and disabling the appropriate button at a limit. [Windows number-box guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/number-box) supports inline spin buttons and visible labels.

PresetMaestro retains its existing Avalonia Fluent NumericUpDown input and behavior for Auto-send delay and Preset match threshold. These two controls use 24-pixel-wide buttons with 10×5 chevrons, retaining the Config input height and providing enough text space for 2000 ms and 100%. Their accessible names include units. These dimensions are product choices, not Microsoft requirements.

The [Avalonia 11.2.3 Fluent spinner template](https://github.com/AvaloniaUI/Avalonia/blob/11.2.3/src/Avalonia.Themes.Fluent/Controls/ButtonSpinner.xaml) assigns local button and icon widths. A small NumericUpDown subclass sizes those existing template parts after they are applied, preserving standard validation, limit states and keyboard behavior instead of replacing the template. Other numeric inputs keep their existing appearance.

Deliberate deviation: Fluent recommends an ordinary input for large ranges. The delay retains its existing 10–2000 ms spin input, with direct typing available, because this change addresses the requested sizing rather than changing the established entry pattern. Units remain in the existing visible label/adjacent ms suffix instead of a placeholder, keeping them visible while values are entered.

Rendered light and dark Config views were inspected at 1440×850 and 1000×850 with the maximum values. Regression checks measure actual numeric text against available input space and exercise button increments, keyboard decrements and maximum-bound behavior; these supplement visual inspection.

The grouped Profiles card was visually inspected in light and dark themes at 1440×850 and 1000×850. Library labels and controls remain readable, the threshold stays fully visible, and Appearance follows the complete Profiles card. Existing numeric keyboard and button checks and compact-layout containment checks pass.
