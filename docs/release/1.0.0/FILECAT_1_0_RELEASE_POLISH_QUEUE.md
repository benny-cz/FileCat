# FileCat 1.0.0 — queued presentation work

This queue records owner-requested polish. It does not add release blockers or
change the release issue/campaign counts.

## PQ01 — improve the GitHub README and screenshots

- Requested: 2026-10-06 by the product owner during release execution.
- Priority: low; continue higher-priority release validation first.
- Status: the 2026-10-09 native Windows presentation remains accepted for its
  original scope. A current-source refresh on 2026-10-29 adds all eight Windows
  themes and one Classic Dark image each from macOS and Ubuntu, and splits the
  README's main and advanced features. [Original native evidence](evidence/E-PQ01-windows-theme-showcase.md)
  and [refresh evidence](evidence/E-PQ01-refresh-cross-platform-gallery.md) have distinct scope.
- Improve the README's text presentation of FileCat for GitHub readers;
  include main and advanced feature lists.
- Mention that the app has not just been vibecoded: it has also been properly
  and thoroughly tested.
- Include polished screenshots of the real Windows app. Show its main window
  with four panels, each containing more than two tabs (at least three).
- Include every available theme; verify the actual theme list before capture.
  The owner's 2026-10-29 choice is all themes on Windows and one representative
  theme each on macOS and Ubuntu.
- Use owned demonstration files and tabs with clear, useful contents. Preserve
  actual product behavior and avoid exposing personal paths or credentials.
- Let the owner decide any ambiguous presentation or screenshot choices.
- Completed with supported native Windows capture/input: eight original JPEGs,
  four panels and three tabs per panel. All seven palettes plus System are shown.
  The README and image links are reviewed; exact build/interaction/restoration
  receipts retain their scope. The owned profile and all 105 files were archived
  and removed. No accessibility/native-engine/candidate qualification is implied.
- Further live testing uses the VM; host UI is stopped while the owner works.
- Refreshed from current FileCat source with the actual `MainWindow` rendered
  off-screen on Windows 11 Insider, macOS and Ubuntu. The eight Windows and two
  Unix PNGs show the same owned 2×2 workspace; no installed package, native
  interaction, accessibility or final-candidate result is inferred from them.

Acceptance: a reviewed README with clearer product presentation, working image
links, all Windows themes, one macOS and one Ubuntu view, and a four-panel,
twelve-tab workspace. The earlier real-window Windows captures retain their
separate interaction evidence; the current gallery is a headless UI render.
