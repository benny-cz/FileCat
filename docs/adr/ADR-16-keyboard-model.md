# ADR-16: Keyboard model and reference-conflict resolution

**Status:** Decided for v1 (2026-09-27). TV-10's manual part (users of all three references, screen readers) is pending.

## Decision

- **Defaults** are agreement-first (§4.5):
  - F3–F8 are canonical, with F4 for Edit and F7 for Create.
  - Insert and Space mark; Space toggles the mark without moving.
  - Tab moves to the target panel.
  - Alt+F7 searches and Ctrl+Enter inserts the focused name.
- **Recorded additions and conflict resolutions:**
  - No Ctrl+Alt+letter chords, because AltGr produces Ctrl+Alt on Czech, German, Polish, and similar layouts.
  - F11 maximizes the panel. F12 opens the panel picker; Shift+F12 picks the target panel.
  - Ctrl+J opens the operation center, Ctrl+Shift+P the command palette, Ctrl+E the command line, and Ctrl+S the quick filter.
  - Alt+F8 opens command history, as in TC and FAR.
  - Alt+0–9 switch column profiles.
- **Configuration.** Every binding can be changed in Settings → Keyboard. Every command is reachable through the
  bound palette and menu bar, and a headless test checks this. The searchable F1 reference shows the effective bindings.

## Consequences

Users arriving from any of the three references keep the shared muscle memory. Their differing secondary bindings are
one settings line away.
