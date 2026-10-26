# Remaining UX/UI audit work

Updated: 26 October 2026. Maintain this file after every validated batch. The
[audit](FileCat_UX_UI_Audit.md) defines the acceptance criteria; the
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records code, tests and
evidence by finding. Work directly from the Git checkout and push reviewed
increments to `main`.

## Open prerequisites

- Resolve the macOS 26 CI `FileCat.Remote.Tests` testhost exit 134 seen on
  `be7b2a30` after its result file reported 2,595 passed and no test failures.
  A Mac 27 native rerun with the same pinned SDK passed 2,604/30 skipped;
  require a green replacement macOS 26 lane and capture the crash sequence if
  it recurs.
- Finish Mac visual layout checks for other dialogs and their unverified
  1× and scaled states using the owner-approved
  `FileCatCapture` helper in the desktop session. Direct macOS AX now reads
  FileCat's window and source row, and the Copy/Move/Delete modal-command
  guard passed repeatedly through it. Normal and 760 × 600 Retina
  Copy/Move/Delete captures were inspected; their other scaled layouts remain. System Events still
  exposes no children over SSH. Keep Mac audio muted. Finish the remaining
  Ubuntu Linux input, clipboard,
  viewer and assistive-technology checks.
- Extend the `linux-orca-speech.py`, `win-nvda-speech.ps1`, and
  `mac-ax-focus-speech.py` journeys for the remaining spoken criteria of other
  findings (viewer Stop and provider-error outcomes, UXBUG-015 and
  FCUX-040/041 form errors and tab routes beyond the now-spoken Ubuntu
  two-panel switch).
  Assert VoiceOver's actual spoken phrases for the other Mac routes. Keep Mac
  audio muted.

## Next actions, in severity order

1. **Close the final validation gate.** Require green producer, Windows,
   Ubuntu, macOS and Windows ARM64 CI lanes on the final `main` head. Fix any
   failing lane before declaring the audit complete. Rerun the complete
   final-head gate after the last audit change. If it recurs, investigate
   the intermittent Ubuntu metadata-demand extra read; earlier runs are
   recorded in the implementation status. Recheck the revised macOS checksum
   generation case and revised owned-FTPS stage fixture under the full CI load.
   Recheck the revised macOS inline Rename focus-guard test and Windows SFTP
   edit-baseline tests in the final-head gate.
2. **Finish S1 operation and decision journeys.** For FCUX-001, cover
   remaining supported provider failure and cleanup paths, including native
   target changes after the journaled backup preflight on servers without
   compare-and-swap. Verify
   recovery prompts and exact server manifests on an actual SFTP server
   without atomic rename and on desktop FTP/FTPS routes after ambiguous
   rename/cleanup results. Finish Windows FTP/FTPS failure
   kinds beyond native interrupted Copy and Move
   cleanup across all three FTP modes, Mac FTP/FTPS failure kinds beyond
   changed-destination and dropped-connection staging behavior, and SFTP
   provider failures beyond the native destination-content, metadata and
   dropped-connection staging checks.
   For FCUX-069/070, validate
   supported cross-app Move effects and delayed Paste destination on each
   desktop. Windows still needs Explorer Cut
   presentation and supported external Move/delete-on-paste handling, if
   safely offered. On Mac, the preview list has no AX row children; decide
   whether direct row navigation adds value beyond the tested, spoken
   Previous/Next full-path navigator. For FCUX-004, exercise modal focus and
   decision routes beyond the Windows/Ubuntu/Mac Copy/Move/Delete reviews'
   background-command guard and the spoken Copy/Move/Delete entry and return paths. Capture actual
   Orca speech for the remaining decision routes where required.
   Preserve captured scope and mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, verify the lossless Settings round-trip in the final CI gate; its
   structured editor extension is tracked with FCUX-036.
   For FCUX-007, finish other scaled layouts with real pointer and keyboard
   input and the remaining assistive-technology routes. Visible Stop passed
   Windows Hex/Text at 100%, 150% and 200% and Ubuntu Hex/Text at 100% and
   200%; test other modes, providers and Ubuntu 150%,
   changed-query ownership on other routes, cancellation over live remote
   connections, native spoken provider-error
   outcomes on Windows and spoken provider errors on Ubuntu and Mac. Ubuntu
   Orca and muted Mac VoiceOver already spoke Hex/Text local Stop.
   Complete the other provider/scale assistive-technology routes for search
   results and count completeness, including remote-backed searches and narrow
   viewer searches beyond the tested Ubuntu 720 × 500 and minimum 480 × 300
   Text views at 100%/200%.
   For FCUX-008, test native incomplete reads on local and remote providers,
   mid-read changes on the remaining desktop and provider routes, minimum-size
   layouts beyond the validated Ubuntu 100%/200% and Windows 100%/150%/200%
   checksum runs, and a stable Ubuntu 150% scale run after VMware's automatic
   resolution reset is controlled. For FCUX-009, the Ubuntu
   long-line Copy, physical Next difference and narrow Wrap route passed;
   finish Linux pixel alignment, font fallback, pointer selection and spoken
   automation on the supported desktops. For FCUX-010, verify VoiceOver's
   remaining item actions beyond F3, Copy, Move and Delete, and bounded native
   listings on other supported provider types.
   Finish the other provider, scale and spoken action routes recorded in the
   status table.
4. **Work through S2 native acceptance in audit order.** The remaining groups
   are other shared dialogs and validation routes (FCUX-011 beyond the Mac Copy
   form, 034, 041, 054), comparison and
   viewer semantics (012, 076, 072, 073, 056), theme/readability (013, 049,
   050, 051), safe listing and panel geometry (014, 022–028, 030–033, 047,
   048), external transfer (071, 075), operation evidence and recovery
   (015–017, 019–021, 042, 043, 068), notification lifetime (029: clipboard,
   scaled layout and native Windows timed administrator notice), Settings
   and command consistency (035–037, 046, 057), bulk rename (038–040), Find
   and preview behavior (044, 045, 052, 053, 055). A native 50,000-file
   end-to-end bulk-rename review remains beyond the bounded headless UI test.
   The status table identifies
   each group's exact provider, input, scaling and assistive-technology gate.
   FCUX-031 still needs nested and scaled layouts. FCUX-032
   still needs scaled layouts, spoken feedback and non-filesystem breadcrumb
   paths. For UXBUG-019, verify that a retained deleted file's original bytes
   are recovered to another physical disk on the Windows VM. Its dynamic VHD
   exposes the deleted name and uncertainty warning, but returns discarded
   clusters as zeros; use a medium or fixture that retains the original
   clusters. Then exercise the owner's disposable `G:` UI route if safe to do
   so without using the workstation desktop.
5. **Finish S3 refinements on real desktops.** FCUX-058, 074 and 059–068 need
   the outstanding pointer, keyboard, multi-panel, window-manager,
   multi-monitor/DPI, clipboard, remote/archive naming, screen-reader and
   scaled-readability checks recorded in the status table.
6. **Close the queued bug acceptance gates.** UXBUG-001/002/004/005/007/008
   need their remaining native scaling, keyboard, browser-engine or
   assistive-technology checks; UXBUG-006 needs Unix attribute acceptance;
   UXBUG-009 needs native Details selection; UXBUG-010/011 need scaled and
   spoken dialog checks, including keys, full paths, reflow and disclosure in
   the revised review hierarchy and the other resizable review windows on the
   Windows VM. UXBUG-012 still needs other Mac Page focus routes and
   assistive-technology acceptance. Registry import
   and history review still need native narrow
   and scaled acceptance. Treat any new reproduction as an active regression,
   with a test and a new commit.
7. **Close newly reported minor issues.** UXBUG-013 still needs native
   pinned-place interaction, scaled layouts and spoken acceptance.
   UXBUG-014 still needs native display-scale and spoken acceptance.
   UXBUG-015 needs localized speed entry, keyboard error navigation and
   actual screen-reader announcements on supported desktops. Report
   Avalonia 12.1's incorrect AT-SPI role mapping upstream if the owner agrees.
   UXBUG-016 needs native checks of overwrite and other dialog
   families, including their popup choices, and the remaining
   non-Windows scaled visuals and actual screen-reader speech on each desktop.
   Report Avalonia's synthetic macOS modifier and scaled-bitmap behavior
   upstream if the owner agrees.

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
  user files.
