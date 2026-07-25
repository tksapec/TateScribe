# Crop controls layout

## Goal

Show all four crop-exclusion labels and percentage fields in the fixed-width
left pane at the current application font size.

## Design

Replace the single horizontal row of crop inputs with a two-column, two-row
grid.  Each cell contains a concise, explicit label and its percentage input:

| Row | Left column | Right column |
| --- | --- | --- |
| 1 | Left exclusion / percentage | Right exclusion / percentage |
| 2 | Top exclusion / percentage | Bottom exclusion / percentage |

The existing control names, validation, apply buttons, and persisted crop
values remain unchanged.  The change is limited to the XAML layout.

## Verification

- Add a lightweight source-shape test that requires the two-row crop grid and
  all four input controls.
- Build the Release configuration.
- Confirm at the existing fixed window size that no crop label or input field
  is clipped.
