# I628 — Create file list chooser pointer dismissal

**Source:** `00b4be9d` (parent `03479df5`). **Disposition:** remediated preliminarily; exact-source CI and installed-candidate acceptance remain separate.

## User consequence and correction

In Commands → Create file list, an open chooser could remain expanded after a mouse click in another field or on the dialog backdrop. Keyboard dismissal worked, but mouse users could not reliably continue editing the form. The common in-window modal pointer handler now closes open ComboBox popups when the press is outside that chooser. Presses within the same chooser or its popup still reach the normal toggle and selection handlers. The Create file list dialog itself remains open on a backdrop press.

## Before and after checks

| Check | Observation |
|---|---|
| Baseline regression | New `Non_dismissable_modal_closes_chooser_on_another_pointer_target` failed at its first outside click: the chooser remained open after a click on the format editor. |
| Corrected macOS ARM64 isolated source | Focused overlay pointer suite **8/8 passed**. Focused Settings dialog suite **10/10 passed**, checking the shared modal handler. |
| Corrected Ubuntu x64 isolated source | Release App build passed with zero warnings/errors. In the native X11 Create file list dialog, opening the scope chooser created a popup; clicking another field removed it and left the dialog open. Reopening it and clicking the non-dismissing backdrop removed the popup while the dialog remained visible. A second click on the same chooser toggled it closed normally. Only the isolated test app was stopped. |

The native Ubuntu check confirms mouse dismissal at the reported dialog boundary; it does not establish every chooser item selection or the final Windows package. The Mac desktop was locked for this slice, so macOS evidence is focused headless tests and build, not a live desktop interaction. The unrelated Windows workstation was not used for live testing.

## Finite candidate acceptance and stop

1. At exact source `00b4be9d` or a descendant containing the same fix, producer, Windows x64, Windows ARM64, Ubuntu and macOS CI jobs pass. Reconcile consequential new skips against the preceding green source run; tag-only package/draft skips on main are expected.
2. In the final installed or packaged 1.0.0 candidate, open Create file list on one supported desktop. For each of its scope, format and output choosers, open it, click a different field and the non-dismissing backdrop, and require that the popup closes while the dialog and draft remain. Click an item in each chooser and require the selected value to update. Confirm the same chooser can be toggled closed and Escape/Cancel retain their existing behavior.

Stop after these affected interactions pass in the exact candidate and the CI gate is recorded. Reopen only for a changed modal/popup implementation, a demonstrated regression, or a previously untested mandatory platform boundary.
