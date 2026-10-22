# Remaining UX/UI audit work

Updated: 22 October 2026. Maintain this file after every validated batch. The
[audit](FileCat_UX_UI_Audit.md) defines the acceptance criteria; the
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records code, tests and
evidence by finding. Work directly from the Git checkout and push reviewed
increments to `main`.

## Open prerequisites

- Finish Mac visual layout checks for the other viewers, dialogs and scaled
  states using the owner-approved `FileCatCapture` helper in the unlocked
  desktop session. Keep the Mac display awake while finishing Mac input and
  clipboard journeys. Finish the remaining Ubuntu Linux input, clipboard,
  viewer and assistive-technology checks.

## Next actions, in severity order

1. **Close the final validation gate.** Require green producer, Windows,
   Ubuntu, macOS and Windows ARM64 CI lanes on the final `main` head. Fix any
   failing lane before declaring the audit complete.
2. **Finish S1 operation and decision journeys.** For FCUX-001, cover
   remaining supported provider failure paths, including races after the final
   destination content check. Verify that every FTP/SFTP replacement review
   exposes the residual risk, and determine whether any supported provider
   offers a safe atomic publication path. Also finish native
   recovery choices plus safe review and cleanup of the remote stage after an
   FTP process crash. For FCUX-018, verify
   native Windows close/restart ownership during a held inverse after the
   headless active-close test passed, and inspection of a report with a
   failure beyond row 30, and incomplete or mixed-effect attempts. For FCUX-069/070,
   validate supported cross-app Move effects and
   delayed Paste destination on each desktop. Windows still needs Explorer Cut
   presentation after the completed Copy effect and supported external
   Move/delete-on-paste handling, if safely offered.
   FCUX-002 still needs scaled-display input and deeper non-Windows focus
   journeys beyond the verified Mac pointer/keyboard range at normal and
   narrow widths. FCUX-003
   still needs scaled display and other smaller-layout states, keyboard and spoken
   review, native external-editor round trips and rule-change races after the
   headless stale-scope result was rejected. For FCUX-004/005, exercise remaining modal
   focus/decision ownership and full synchronization endpoints, including
   other modal focus and decision routes after the Mac, Windows and Ubuntu
   Copy/Delete background accessibility guards passed. Preserve captured
   scope and mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, verify the lossless Settings round-trip in the final CI gate; its
   structured editor extension is tracked with FCUX-036.
   For FCUX-007, test the Hex editor controls and bounded match status with
   scaled/spoken input after the Windows VM, Mac and Ubuntu normal desktop
   runs. Test visible Stop, changed-query ownership beyond the verified Text
   and Hex routes, provider cancellation, scaled layouts and spoken Find
   results. Assistive-technology routes still need native checks.
   For FCUX-008, test native incomplete sources and changes during a read,
   scaled display and other narrow-layout states, and
   clipboard behavior on other supported desktops. For FCUX-009, include long
   lines, font fallback, pointer/keyboard navigation and automation on all
   supported desktops. For FCUX-010, inspect large-listing latency, complete
   marked-item review and actual spoken navigation with a supported screen
   reader. Confirm macOS/Linux bridges separately.
4. **Work through S2 native acceptance in audit order.** The remaining groups
   are other shared dialogs and validation routes (FCUX-011 beyond the Mac Copy
   form, 034, 041, 054), comparison and
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
   Windows VM. UXBUG-012 still needs other Mac Page focus routes and
   assistive-technology acceptance beyond the verified Find, keyboard
   Select All and rendered-text Copy journeys. Registry import
   and history review still need native narrow
   and scaled acceptance. Treat any new reproduction as an active regression,
   with a test and a new commit.
7. **Close newly reported minor issues.** UXBUG-013 still needs native
   pinned-place interaction, scaled layouts and spoken acceptance after the
   verified Windows normal/narrow/wide/restore row reflow and overflow.
   UXBUG-014 still needs native display-scale and spoken acceptance after the
   verified Windows finished drawer and Open/Copy/Save report routes, Ubuntu
   normal-width details, and Mac normal/narrow finished details. UXBUG-015
   needs localized speed entry, keyboard error navigation and actual spoken
   feedback on supported desktops. The same-dialog invalid-speed correction
   and accessible field help passed native Mac, Ubuntu and Windows checks,
   but screen-reader announcements remain to be heard and checked.
   UXBUG-016 needs native visual checks of the centered vector X at 150%
   and other scaled states, dialog families beyond the verified Windows
   Create folder, Create in Registry and Copy and Ubuntu Create folder and
   Copy routes, and actual screen-reader speech on each desktop. The Ubuntu
   Wayland physical uinput mouse and touch routes passed; the Mac AX label and
   native X/backdrop pointer route passed through direct AXUIElement traversal.

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
  them. Use the current Win11 and Ubuntu VMs for their native checks; finish
  Mac visual capture and the remaining native UI checks.
- Close a finding only when its audit criteria and status-table remaining
  acceptance pass, or record a justified equivalent route with the same
  consequential scenarios. Update this file and the implementation status
  after each batch, commit and push to `main`, and retain unrelated untracked
  user files. Do not create source or changes ZIP handoffs.
