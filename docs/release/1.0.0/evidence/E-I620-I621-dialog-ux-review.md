# I620–I621: dialog UX review

**Scope:** Source commit `8008a3a8523faf830d8ebaa4399de8d2d2563d6a`, compared with `56ce008c9878c1857cdef1e1a5c3815c4f481200`. This is a source-level review, not final installed-candidate acceptance. Settings (I618) and the Mac TARGET badge (I619) retain their separate evidence.

## What was walked

The review inventoried the dialog-service entry points and inspected the shared card, focus, scrolling and dismissal behavior. A temporary Skia-backed headless harness rendered the minimum 640 × 455 main-window surface for About, Advanced Find, Ignored Folders, duplicates, prompt, choice, confirmation, Registry search, new connection, Apply command, Pack, Create link, theme choice, and the launchable command dialogs for Help, location, bookmarks, tabs, Copy, Move, new folder/file, selection, bulk rename, checksum, attributes, directory comparison and permanent deletion. About was inspected in Classic, DOS Commander, Psychedelic and High Contrast; its permanent width test covers all seven themes. The viewer, hex editor, file comparison and Find secondary windows were also rendered. The capture harness and its altered headless drawing configuration were removed after inspection; no temporary screenshots or test switches are shipped.

Rare state-dependent variants were source-reviewed against existing focused tests: operation conflict/issue/Undo and history retirement, verification results, Registry import/value edit, remote host-key/certificate and sign-in, remote/archive edit conflicts, and permissions. No live failure, remote server or Registry mutation was induced for this visual pass. Existing safety and viewport suites remain the controls for those paths.

## I620 — compact forms should retain their relationships

At the minimum main-window width, About's fixed 600-DIP body caused a horizontal page scrollbar and clipped its theme header. Advanced Find could put the “At most” unit on a separate line from its label and value. Apply command, Pack and Create link had fixed minimum widths of 560–640 DIP; their initial outer horizontal extents reached 560–647 DIP against a 554-DIP body viewport, shifting or clipping reviewed content. The SFTP connection's server and key-file rows had the same fixed-row risk when text or scale grew.

The correction lets About and the form fields use the card's available width, keeps About's logo and wrapped text in grid columns, wraps author/action rows, groups each size bound and both date endpoints, and wraps connection, compression and link-kind controls. The command and link previews no longer demand 640 DIP. Rendered post-fix outer extents for About, Apply command, Pack, Create link and theme choice were each **554/554 DIP** at the minimum window. Connection and the sampled standard/safety dialogs did not develop page-level horizontal overflow. Long commands and paths remain editable in their own text boxes; review text and buttons remain reachable.

**Finite acceptance:** In a rebuilt final candidate on Windows, Ubuntu and macOS, open About (all themes), Advanced Find, New connection, Apply command, Pack and Create link at the minimum supported window size and at increased content text size. There must be no outer horizontal page scrollbar, missing button, clipped warning or detached size/date label. The internal scroll of a long path or checksum field may remain. The permanent About, Advanced Find and four-form width regressions must pass on the matching source; a new overflow or unreachable consequential choice reopens I620. Otherwise stop visual retuning.

## I621 — Ignored Folders editing must not close by accident

The Add field consumed Enter only when nonblank. Blank Enter could fall through to the dialog's default OK action and close the editor. Add and Remove also appeared available when they had nothing to act on. Enter in the Add field now always stays in the editor and adds only nonblank text. Add is enabled only for nonblank text; Remove is enabled only for a selected row. The explicit OK and Cancel actions retain their meanings.

**Finite acceptance:** With an owned ignored-folder list, blank/whitespace Enter leaves the editor open and unchanged; a nonblank Enter adds exactly one entry; Add is disabled without input, Remove without selection, and Cancel leaves the saved list unchanged. The focused regression must pass on the matching source and the final installed candidate must pass a keyboard check on each platform. A lost draft or accidental acceptance reopens I621; otherwise stop.

## Verification and limits

- Before the product change, the existing Windows dialog viewport, focus and backdrop suite passed **56/56**. This confirms the shared card was not the source of the observed content overflow.
- After the correction, the focused About, search-dialog, four-form, viewport, Apply-command and Create-link suites passed **47/47** on the rebuilt Windows source. A first run passed 46/47 because a new Add-button test checked the state before Avalonia processed TextChanged; the test now settles the UI event queue, and the focused case and full 47-case suite pass. This was a test timing issue, not a second product defect.
- The last prechange main run [41547359382](https://github.com/benny-cz/FileCat/actions/runs/41547359382) passed producer, Windows x64/ARM64, Ubuntu and macOS; tag-only packaging/draft jobs skipped as intended. Exact-source CI for `8008a3a8` is pending at this record.
- No additional visual ornament or control was justified by the remaining reviewed dialogs. The candidate check above is deliberately finite; do not reopen an accepted dialog without a consequential counterexample.

Stable 1.0.0 remains **NO-GO** until its independent release gates and owner decision are satisfied.
