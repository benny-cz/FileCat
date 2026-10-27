# Remaining UX/UI audit work

Updated 27 October 2026. [The acceptance ledger](UX_UI_ACCEPTANCE_LEDGER.md)
is the sole authoritative list of required scenarios, their status, tested
build/environment, evidence and exact closure condition. This file is a
severity-ordered index of **remaining work only**. Dated passing results and
accepted equivalent routes stay in
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md). Follow the
[session workflow](UX_UI_SESSION_WORKFLOW.md) before any repeat run.

## Highest priority — S2 and queued defects

- [S2 shared desktop scenarios](UX_UI_ACCEPTANCE_LEDGER.md#s2--shared-desktop-boundaries):
  dialog reachability, operation evidence, Undo/sync, navigation,
  workspace/listing, panels,
  Settings/rename, Find, scrolling, provider context and viewer refresh.
  Each table row specifies one bounded consequential route.
- [AC-S2-019](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-019) and
  [AC-S2-020](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-020): separate finished-history
  and interrupted-recovery choices.
- [AC-S2-036](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-036) and
  [AC-S2-037](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-037): separate editable Settings
  round trip and command-availability parity.

## S3 and final gate

- [S3 focused usability scenarios](UX_UI_ACCEPTANCE_LEDGER.md#s3--focused-usability-routes):
  Places/hierarchy, density, clipped cells/tab identity, dependent controls,
  window restoration, one-panel/last-tab behavior, About and queue movement.
- [GATE-CI-FINAL](UX_UI_ACCEPTANCE_LEDGER.md#gate-ci-final): green producer,
  Windows, Windows ARM64, Ubuntu and macOS CI on the final implementation head.

Optional refinements and a documented repeat rule are in the ledger; they do
not silently extend required completion scope.
