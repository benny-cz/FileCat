# I618 — Settings layout and draft dismissal

**Status:** Remediated preliminarily in the 2 November 2026 working source after `f5bc6fd5`. **Severity:** Medium UX: a backdrop click or Escape could silently discard an unfinished Settings draft, and the tall Tools editors placed their section labels mid-column away from the fields. This is source-level evidence, not installed 1.0.0 candidate acceptance.

## Owner observation and distinct boundary

The owner's screenshot of the Tools page shows **User commands (F9)** centered vertically beside a tall editor, uneven labels, a prefilled **New command** name that must be manually removed, and the initial command preview already saying it is out of date. The owner also observed that a click outside or Escape closes Settings too easily, and asked that an outside click never close it and that switching tabs keep the window's upper-left position. The existing Settings tests covered preservation, validation and small-viewport reachability, but not this combined layout and dismissal journey.

## Correction and focused verification

- Settings is top-anchored in the main window. Changing tabs no longer shifts its card; the shared two-column form aligns labels to the top of their fields.
- Saved filters, User commands and Associations use full-width headings and editors. The long user-command text grammar appears only when advanced text mode is open.
- New commands, menus and associations use empty name fields with visible hints and a list-row instruction; OK still validates a real name. Before the first invocation, command and association previews retain neutral **Choose Preview invocation** guidance.
- When OK finds an invalid setting, its error stays visible above the action buttons while focus moves to the field. The pinned area takes no space before an error appears.
- A click outside Settings is consumed without closing it, with or without edits. Escape and the title-bar close button open a **Close Settings?** review; **Keep editing** returns to the draft and **Discard draft** closes without saving. The explicit **Cancel** button keeps its immediate discard behavior. The read-only Settings view also ignores outside clicks.

The focused headless Settings test changed tabs through Tools, Appearance and Privacy and checked the card's X/Y origin within one DIP; it exercised pristine and edited backdrop presses, the empty-name hints, neutral preview text, pinned invalid-name feedback, Escape review and discard without applying the draft. Release App/test build succeeded with zero warnings/errors. Direct xUnit SettingsDialogTests passed 4/4, SettingsPreservationTests 24 passed/one expected Unix-only skip on Windows, DialogViewportTests 37/37, OverlayDialogPointerDismissTests 7/7 and DialogFocusOwnershipTests 12/12. The VSTest runner could not connect to its test host in this environment; the same built test DLL ran successfully through xUnit's in-process runner. These are automated UI/layout checks, not an updated native screenshot or a packaged-candidate test.

## Finite candidate acceptance and stop

On the frozen 1.0.0 Windows candidate, open Settings at a normal and a narrow window size. In Tools, add a command, type a real name/program, switch to another tab and back, and require that the card's upper-left position stays fixed, fields remain reachable, and no left-column section label floats beside a tall editor. Require an untouched new name to behave as a hint, an initial preview to be neutral, and invalid empty names to keep OK from saving with a visible, pinned error. A click outside both pristine and edited Settings must leave it open. Escape must offer keep/discard; keep must preserve the draft, discard must leave settings unchanged, Cancel must close directly, and OK must apply the valid draft. Check the title-bar close action and read-only outside-click behavior once. Require producer plus Windows x64/ARM64, Ubuntu and macOS CI at the changed source with expected tag-only skips reconciled. Stop when those checks pass; do not repeat unrelated accepted Settings or dialog scenarios.
