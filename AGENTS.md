# PresetMaestro project guidance

## Builds, tests and releases

- Run builds, tests and release packaging locally on the appropriate Windows or Mac machine.
- Do not add, enable or run GitHub Actions workflows. GitHub Actions is disabled for this repository by the maintainer's choice. GitHub remains available for source control and release downloads.
- Use the local validation commands in `docs/windows-macos-development.md` and the release instructions in `docs/github-releases.md`.

## UI and UX changes

- For new or substantially changed UI elements and interactions, consult current official Microsoft Fluent 2 and Windows app design guidance for the relevant pattern before implementing. Do not require the user to request this research each time.
- Apply the guidance to Avalonia using the application's existing theme and controls. Distinguish documented requirements from product choices, and record relevant primary-source links and any deliberate deviations in the applicable UX document.
- Keep dismissal controls separate from task actions. For a details pane, put a clearly labelled close control at the trailing edge of its header, keep it reachable while scrolling, support keyboard dismissal, and return focus to the originating content.
- Check readable labels, spacing, hierarchy, keyboard access, accessible names, and focus behavior. Inspect rendered results in light and dark themes at normal and supported narrow window sizes before completing a layout change.
- Use proportionate regression checks for changed interaction behavior; do not replace visual inspection with assertions that merely mirror control construction.
