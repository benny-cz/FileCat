# Remaining UX/UI audit work

Updated: 20 October 2026. Maintain this file after every validated batch. The
[audit](FileCat_UX_UI_Audit.md) defines the acceptance criteria; the
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records code, tests and
evidence by finding. Work directly from the Git checkout and push reviewed
increments to `main`.

## Open prerequisites

- Native macOS and Linux desktop sessions are needed for platform-specific
  clipboard, viewer, input, accessibility and window-manager checks.

## Next actions, in severity order

1. **Close the current validation gate.** Check the pending Windows, Ubuntu,
   macOS and Windows ARM64 CI lanes. Fix any failing lane before declaring the
   current batch verified. Require the same checks on the final head.
2. **Finish S1 operation and decision journeys.** For FCUX-001, finish the
   supported nonlocal-provider interruption and recovery cases, including
   retained intent, source/target bytes and staging cleanup. For FCUX-018, verify
   Windows close/restart ownership and report behavior for empty-file and
   link inverse kinds, including incomplete or kept attempts. For FCUX-069/070,
   validate native clipboard handoff, Move effects and delayed Paste destination
   on each supported desktop. FCUX-002 still needs its other narrow-window
   routes, scaled-display input and the non-Windows focus journeys. FCUX-003
   still needs scaled/narrow layout, keyboard and spoken review, and stale
   external-editor results. For FCUX-004/005, exercise modal focus/decision
   ownership and full synchronization endpoints. Preserve captured scope and
   mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, verify the lossless Settings round-trip in the final CI gate; its
   structured editor extension is tracked with FCUX-036.
   For FCUX-007, test native editor Previous routes, visible Stop,
   changed-query ownership, provider cancellation and non-Windows input.
   For FCUX-008, test native incomplete sources and changes during a read,
   scaled display and other narrow-layout states, and
   clipboard behavior on other supported desktops. For FCUX-009, include long
   lines, font fallback, pointer/keyboard navigation and automation on all
   supported desktops. For FCUX-010, inspect large-listing latency, complete
   marked-item review and actual spoken navigation with a supported screen
   reader. Confirm macOS/Linux bridges separately.
4. **Work through S2 native acceptance in audit order.** The remaining groups
   are shared dialogs and validation (FCUX-011, 034, 041, 054), comparison and
   viewer semantics (012, 076, 072, 073, 056), theme/readability (013, 049,
   050, 051), safe listing and panel geometry (014, 022–028, 030–033, 047,
   048), external transfer (071, 075), operation evidence and recovery
   (015–017, 019–021, 042, 043, 068), notification lifetime (029), Settings
   and command consistency (035–037, 046, 057), bulk rename (038–040), Find
   and preview behavior (044, 045, 052, 053, 055). The status table identifies
   each group's exact provider, input, scaling and assistive-technology gate.
   FCUX-031/032 still need nested/scaled layouts, spoken feedback and
   non-filesystem breadcrumb paths.
5. **Finish S3 refinements on real desktops.** FCUX-058, 074 and 059–068 need
   the outstanding pointer, keyboard, multi-panel, window-manager,
   multi-monitor/DPI, clipboard, remote/archive naming, screen-reader and
   scaled-readability checks recorded in the status table. Update stale
   historical “rerun pending” text when final receipts are available.
6. **Close the queued bug acceptance gates.** UXBUG-001/002/004/005/007/008
   need their remaining native scaling, keyboard, browser-engine or
   assistive-technology checks; UXBUG-006 needs Unix attribute acceptance;
   UXBUG-009 needs native Details selection; UXBUG-010/011 need scaled and
   spoken dialog checks, including keys, full paths, reflow and disclosure in
   the revised review hierarchy and the other resizable review windows on the
   Windows VM. Registry import and history review still need native narrow
   and scaled acceptance. Treat any new reproduction as an active regression,
   with a test and a new commit.

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
