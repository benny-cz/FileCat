# Remaining UX/UI audit work

Updated: 19 October 2026. Maintain this file after every validated batch. The
[audit](FileCat_UX_UI_Audit.md) defines the acceptance criteria; the
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records code, tests and
evidence by finding. Work directly from the Git checkout and push reviewed
increments to `main`.

## Current position

All 76 numbered audit findings and eleven subsequently queued bugs have an
implemented code path and automated regression coverage. This is **not** audit
completion: the acceptance checks below remain open. The last fully green
[cross-platform CI run](https://github.com/benny-cz/FileCat/actions/runs/39290466271)
is at `a9738fcc`. Runs for `9495910c`, `bacbfa39` and `b80cfc71` failed;
newer runs and the next validation head must finish before their changes have
a cross-platform pass. The common App failure was a virtual-row test that
expected the focused row to be evicted; the corrected check uses a nonfocused
row and enough scrolling to reach the bounded cache. The macOS Core failures
exposed a timing-based secret-store assertion and loss of already captured
command stderr when a descendant held a pipe open. Both were corrected and
need the next macOS CI result. The complete local Core Release suite exited
successfully with 16 workers after running outside the restricted filesystem
sandbox; its platform skips do not establish non-Windows native behavior.

The Windows 11 VMware guest is available for isolated native checks. Its
repeatable runner is [FileCat.NativeAcceptance](../../tests/FileCat.NativeAcceptance/README.md).
Windows inspection has passed the Markdown resize/search journey, real
interrupted NTFS transfer continuation, Explorer clipboard replacement,
resizable dialogs, viewer size persistence, Streams count refresh, file-list
screen geometry and mark/focus semantics, breadcrumb menu accessibility, and
keyboard panel resize/cancel. The long-line comparison fixture also passed
native full-value, second-difference, exact Copy and wrapping checks; a VM
screen capture showed both differing suffixes in the primary aligned view.
FCUX-003 native bulk rename passed twice with fresh guest-local fixtures:
the default review and resulting bytes excluded a filtered hidden mark, and
explicit inclusion reviewed and renamed both files in the displayed order.
FCUX-002 native pointer acceptance passed a four-file sort reversal: a click
anchored `b.txt`, Ctrl+F3 reversed the list, and a real Shift-click on `c.txt`
marked exactly those two source files without changing their bytes.
Its filtered-anchor journey also passed on a fresh fixture: hiding and then
revealing `b.txt` retired that anchor, so Shift-clicking `d.txt` marked only
`c.txt` and `d.txt`.
The user manually confirmed the PE scrollbar
freeze fix on the supplied EXE. Native spoken screen-reader interaction,
macOS/Linux desktop behavior, provider-specific cases and display scaling
are still open where named below.

The VM currently has one guest-local writable NTFS volume (`C:`). Its `D:` is
optical and `Y:`/`Z:` are VMware host-shared folders. A real FCUX-001
cross-volume acceptance test needs a second disposable guest-local writable
volume; do not use a host-shared folder as a substitute for that check. The
comparison native runner and its evidence were committed at `6fa480db`.

## Next actions, in severity order

1. **Close the current validation gate.** The strengthened native FCUX-009
   comparison check passed again on a fresh VM instance and was pushed at
   `6fa480db`. Push and check the virtual-row assertion, secret-store test and
   command-output corrections on all CI platforms; fix any failing lane
   before declaring that batch verified. The remaining FCUX-009 gates are
   font fallback, pointer selection, spoken automation and macOS/Linux desktop
   behavior.
2. **Finish S1 operation and decision journeys.** For FCUX-001, provision an
   isolated second guest-local writable volume, then test a real
   cross-volume interrupted transfer and supported nonlocal providers, with
   process interruption, retained intent, source/target bytes and staging
   cleanup. For FCUX-018, verify real Windows inverse identities, Recycle Bin
   effects, close/restart ownership and retained reports. For FCUX-069/070,
   validate native clipboard handoff, Move effects and delayed Paste destination
   on each supported desktop. For FCUX-002/003/004/005, exercise native range
   anchors, modal focus/decision ownership and full synchronization endpoints
   at narrow and scaled layouts. FCUX-002's Windows sort-reversal pointer
   and filtered-anchor cases passed; cross-tab, refresh, Ctrl-click,
   Shift+arrows and focus-loss transitions still need native checks.
   FCUX-003's default and opt-in Windows rename journeys passed; its
   remaining native gates are scaled/narrow layout,
   keyboard and spoken review, and stale external-editor results. Preserve
   captured scope and mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, retain its passing lossless Settings round-trip coverage in the
   final CI gate; its structured editor extension is tracked with FCUX-036.
   For FCUX-007/008, test native Previous search and checksum selection, Stop,
   clipboard output and provider cancellation. For FCUX-009, include long
   lines, font fallback, pointer/keyboard navigation and automation on all
   supported desktops. For FCUX-010, inspect large-listing latency, complete
   marked-item review and actual spoken navigation with a supported screen
   reader. Confirm macOS/Linux bridges separately; the passing Windows UI
   Automation result does not establish their behavior.
4. **Work through S2 native acceptance in audit order.** The remaining groups
   are shared dialogs and validation (FCUX-011, 034, 041, 054), comparison and
   viewer semantics (012, 076, 072, 073, 056), theme/readability (013, 049,
   050, 051), safe listing and panel geometry (014, 022–028, 030–033, 047,
   048), external transfer (071, 075), operation evidence and recovery
   (015–017, 019–021, 042, 043, 068), notification lifetime (029), Settings
   and command consistency (035–037, 046, 057), bulk rename (038–040), Find
   and preview behavior (044, 045, 052, 053, 055). The status table identifies
   each group's exact provider, input, scaling and assistive-technology gate.
   Windows native coverage for FCUX-031/032 now passes; nested/scaled layouts,
   spoken feedback and non-filesystem breadcrumb paths remain.
5. **Finish S3 refinements on real desktops.** FCUX-058, 074 and 059–068 need
   the outstanding pointer, keyboard, multi-panel, window-manager,
   multi-monitor/DPI, clipboard, remote/archive naming, screen-reader and
   scaled-readability checks recorded in the status table. Update stale
   historical “rerun pending” text when final receipts are available.
6. **Close the queued bug acceptance gates.** UXBUG-001/002/004/005/007/008
   need their remaining native scaling, keyboard, browser-engine or
   assistive-technology checks; UXBUG-006 needs Unix attribute acceptance;
   UXBUG-009 needs native Details selection; UXBUG-010/011 need scaled and
   spoken dialog checks. UXBUG-003 has the user's Windows manual confirmation.
   Treat any new reproduction as an active regression, with a test and a new
   commit.

## Evidence required to finish

- Run affected tests first, then the applicable complete Core/App Release
  suites with up to 16 test workers. Record pass, skip and failure counts;
  platform skips do not establish that platform's native behavior. Require
  green producer checks and Windows, Ubuntu, macOS and Windows ARM64 CI on the
  final head.
- Use isolated fixtures and Git-built binaries for native checks. Preserve
  actual source/destination identities, bytes, cancellation results,
  accessibility names/roles/bounds and screenshots or machine-readable runner
  reports as appropriate. Test refusal, stale state and interruption as well
  as the successful path. Keep large-listing, slow-provider and race checks
  bounded and reproducible.
- Check 100%, 150% and 200% display scale, narrow/minimum and expanded
  windows, keyboard and pointer routes, high contrast/reduced motion, and
  supported assistive technology for surfaces whose audit criteria require
  them. The current Win11 VM can cover Windows; interactive macOS and Linux
  desktop access is still needed for their native acceptance.
- Close a finding only when its audit criteria and status-table remaining
  acceptance pass, or record a justified equivalent route with the same
  consequential scenarios. Update this file and the implementation status
  after each batch, commit and push to `main`, and retain unrelated untracked
  user files. Do not create source or changes ZIP handoffs.
