# Cart Launch Companion development rules

## Avalonia tabular UI

- Use Avalonia's native `TableView` for high-performance, read-only tabular data.
- Do not imitate a table with a styled `ListBox`.
- Do not use or over-engineer a `DataGrid` when the data is read-only.
- Use `ListBox` only when the interface is fundamentally a selectable list rather than a grid.
- Use `DataGrid` only when the requested experience genuinely requires editable cells or other grid-editing behavior that `TableView` does not provide.

## Responsive positioning

- Do not position controls with fixed coordinates or large static margins.
- Do not use margins to compensate for an incorrect parent layout.
- Use flexible `Grid`, `StackPanel`, `WrapPanel`, and content containers that size from their available space.
- Prefer alignment rules such as `HorizontalAlignment="Center"`, `VerticalAlignment="Center"`, `Stretch`, and proportional or automatic grid sizing.
- Use padding and modest spacing only to express the relationship between neighboring elements, not their screen position.
- Keep layouts fluid across display scaling, aspect ratios, Steam Deck-class screens, 1080p, 1440p, and 4K.

## Theme and styling separation

- Keep reusable presentation rules out of individual views and controls.
- Views should primarily describe semantic structure, content, bindings, and behavior.
- Put shared colors, brushes, typography, borders, corner radii, spacing, and control appearance in theme resources and reusable style classes.
- Prefer semantic resource names such as `SurfaceBrush`, `MutedTextBrush`, and `PrimaryActionButton` instead of repeating literal colors or visual values.
- Do not duplicate inline style values across AXAML files.
- Allow a local value only when it is genuinely unique to that content, such as an intrinsic media preview size or a binding-driven value, and not part of the application theme.
- Keep the Launcher, Configurator, and CLC-Cart Monitor visually consistent by sharing common tokens wherever their presentation language overlaps.

## Reusable button states

- Do not replace a button's `ContentPresenter` template merely to implement ordinary interaction states.
- Define button appearance through reusable semantic classes such as primary, secondary, danger, and navigation actions.
- Target control classes directly for `:pointerover` and `:pressed` pseudo-classes instead of overriding large control templates.
- Keep interaction-state styles lean and maintainable; override a control template only when its visual structure genuinely must change.
- Target those classes with Avalonia pseudo-classes such as `:pointerover`, `:pressed`, `:focus-visible`, and `:disabled`.
- Resolve state colors through `DynamicResource` brush keys rather than hardcoded color literals.
- Keep hover, pressed, focus, selected, and disabled behavior centralized in theme resources.
- Create a custom control template only when the control's structure truly differs from a normal button, not simply to change its color or border.

## Flat and virtualizable layout trees

- Avoid deeply nested visual hierarchies, especially inside repeating controls.
- Do not place `Expander` controls inside an `ItemsControl`, `ListBox`, or another repeating list to represent hierarchical data.
- Flatten hierarchical data into a single collection of row view models with explicit depth or indentation values.
- Use one lightweight item template with state-driven visibility and indentation instead of recursively nested containers.
- Prefer controls and panels that support virtualization, recycling, and row caching for large or variable collections.
- Keep repeated row templates shallow and avoid nesting multiple scroll viewers.
- Use true nested layouts only when the content is small, fixed, and semantically cannot be represented as flattened rows.

## Collection virtualization

- Back every potentially long ordinary list with a `VirtualizingStackPanel` rather than a non-virtualizing stack or wrap panel.
- Do not replace the native virtualization pipeline of controls such as `TableView`; configure and preserve their built-in virtualization instead.
- Give repeated rows a uniform `Height` or a reliable `MinHeight` so scroll extents remain predictable.
- Avoid item templates whose height changes after loading unless that behavior is essential and explicitly handled.
- Keep the list's scrolling responsibility at the virtualizing control; do not wrap it in an outer `ScrollViewer` that disables virtualization.
- Verify long-list behavior with enough sample rows to expose recycling, scroll-position, and delayed-content issues.

## Drawn and fluid Avalonia UI

- Build application UI with Avalonia's drawn, cross-platform visual system rather than unnecessary native control wrappers.
- Prefer modern layout primitives such as `Grid`, `StackPanel`, `WrapPanel`, `Border`, and purpose-built virtualizing panels.
- Let containers measure and arrange their children from available space instead of calculating screen coordinates manually.
- Keep layouts resolution-independent and compatible with Windows, SteamOS-class Linux environments, display scaling, and controller-focused use.
- Use native-hosted views only when an operating-system integration or media backend genuinely requires one, and isolate that boundary from the surrounding drawn UI.
- Keep visual trees shallow and compose small reusable controls rather than creating monolithic or heavily wrapped views.

## Window chrome

- Set `ExtendClientAreaToDecorationsHint="True"` only on each Avalonia application's primary `MainWindow`, including the primary window of future tools.
- Do not set it on dialogs, confirmation windows, progress windows, or other secondary windows.
