# ADR-18: Workspace layout as a split tree with docking

**Status:** Decided for P11 (2026-09-29, product owner D-41). Implemented 2026-09-29 (`PanelLayout` in Core, docking in the main window).

## Decision

- **Model.** The workspace layout is a normalized tree. A split has an orientation (side by side or stacked) and
  ordered children with proportional sizes; a leaf is a panel. After every change the tree has no split with one child
  and no split directly inside a split of the same orientation. The default is one side-by-side split of two panels.
- **Docking with the mouse.** Dragging a panel (by its number badge or its tab strip) over another panel shows drop zones:
  the edges dock it beside, above, or below that panel; the center swaps the two. A preview shows the result, drops
  that would break minimum sizes are refused with the reason, and Esc cancels.
- **Keyboard parity.** Alt+Shift+arrows move the active panel one step that way (past its neighbor, else out of its
  split, else to the workspace's edge, as tiling window managers do). Commands swap it with its target, add a panel
  beside or below it, equalize every size, and turn a split between side by side and stacked. Panels are numbered in
  reading order.
- **Invariants.** Designated targets refer to panel identity, never position, so moves never retarget a job; minimum
  sizes (260 × 140 DIP) apply to every panel; maximize keeps the tree; closing a panel gives its space to its siblings.
- **Persistence.** The tree is saved with the workspace. A tree that does not match the saved panels falls back to the
  default split; the P2 flat layouts (a row or a column) load as one split.

## Alternatives

A flat list with one orientation for all panels (the P2 layout) cannot express two panels above a third. Fixed layout
presets cover only the shapes chosen in advance. Floating panels need multi-window workspaces, which stay deferred (D-24).

## Consequences

UX-009. Any practical arrangement within one window, simple persistence, and moves that never touch targets or jobs.
Deep trees can make panels small; minimum sizes refuse such drops, and every drag has a keyboard equivalent.
