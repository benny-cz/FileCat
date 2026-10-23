# Remaining UX/UI audit work

Updated: 23 October 2026. Maintain this file after every validated batch. The
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
- Spoken acceptance now has native harnesses: `linux-orca-speech.py` reads
  Orca's own speech log in the Ubuntu VM, `win-nvda-speech.ps1` reads a signed
  portable NVDA 2026.2 log in the Win11 VM (below
  `%LOCALAPPDATA%\FileCatValidation\NVDA\portable`), and
  `mac-ax-focus-speech.py` records the AX focus notifications and
  announcements VoiceOver speaks from. All three also check the first and a
  repeated copy notice, a rejected Quick filter mask spoken as it appears
  and again on a refused Enter (the shared prompt that F7, Go to and other
  prompts use), the viewer search's outcome alone, and a panel boundary
  resized from command search. Extend these journeys for the "spoken" criteria of other
  findings (Find results, the panel filter field of FCUX-028, which needs a
  keyboard route into it, UXBUG-015 and FCUX-040/041 form errors, tabs).
  Extend the Mac journeys to assert VoiceOver's actual spoken phrases. Its
  AppleScript control is enabled with owner approval, and Mac audio is muted.

## Next actions, in severity order

1. **Close the final validation gate.** Require green producer, Windows,
   Ubuntu, macOS and Windows ARM64 CI lanes on the final `main` head. Fix any
   failing lane before declaring the audit complete. Rerun the complete
   final-head gate.
2. **Finish S1 operation and decision journeys.** For FCUX-001, cover
   remaining supported provider failure paths, including races after the final
   destination content check. In the Mac 1× mode with VoiceOver, investigate intermittent
   pointer activation of the local source immediately after SFTP Connect once;
   the exact source tab's AX action and the complete warning journey pass.
   Supported SFTP
   can atomically replace without an expected-version condition; FTP cannot
   atomically replace, so neither closes the final-check race. Finish the
   remaining provider cancellation choices. For FCUX-069/070,
   validate supported cross-app Move effects and
   delayed Paste destination on each desktop. Windows still needs Explorer Cut
   presentation and supported external Move/delete-on-paste handling, if
   safely offered. On Mac, investigate the scrolling form body's zero AX bounds and preview
   list with no AX children, beyond the now verified spoken Previous/Next
   mapping route. For FCUX-004, exercise remaining
   modal focus and decision routes after the Mac, Windows and Ubuntu
   Copy/Delete background accessibility guards and the Mac command-search to
   marked-review focus route passed. Require the final-head
   CI gate; preserve captured scope and mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, verify the lossless Settings round-trip in the final CI gate; its
   structured editor extension is tracked with FCUX-036.
   For FCUX-007, test the Hex editor controls and bounded match status with
   scaled and spoken input on Windows.
   Test visible Stop, changed-query ownership beyond the verified Text and
   Hex routes, provider cancellation, other scaled layouts and spoken Find
   results. Assistive-technology routes still need native checks.
   For FCUX-008, test native incomplete sources and changes during a read,
   scaled display and other narrow-layout states, and Ubuntu clipboard
   behavior. For FCUX-009, finish long-line and wrap-alignment Linux acceptance,
   font fallback, pointer navigation and spoken automation on the
   supported desktops. For FCUX-010, verify VoiceOver's other item actions,
   multi-item marked-review paging and hidden-mark scope. The Mac review's
   spoken entry, one-item scope and VoiceOver cursor movement now pass.
   Check Orca's repeated row description as a tutorial
   message (Avalonia maps one help text to both AT-SPI properties),
   large-listing latency, complete marked-item review and other
   Mac/Linux item and action routes.
4. **Work through S2 native acceptance in audit order.** The remaining groups
   are other shared dialogs and validation routes (FCUX-011 beyond the Mac Copy
   form, 034, 041, 054), comparison and
   viewer semantics (012, 076, 072, 073, 056), theme/readability (013, 049,
   050, 051), safe listing and panel geometry (014, 022–028, 030–033, 047,
   048), external transfer (071, 075), operation evidence and recovery
   (015–017, 019–021, 042, 043, 068), notification lifetime (029: clipboard,
   scaled layout and native Windows timed administrator notice; its notices are spoken on all three desktops), Settings
   and command consistency (035–037, 046, 057), bulk rename (038–040), Find
   and preview behavior (044, 045, 052, 053, 055). The status table identifies
   each group's exact provider, input, scaling and assistive-technology gate.
   FCUX-031 still needs nested and scaled layouts; its boundary is a slider
   whose share NVDA, Orca and the macOS AX journey hear (`eebb8832`). FCUX-032
   still needs scaled layouts, spoken feedback and non-filesystem breadcrumb
   paths.
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
   pinned-place interaction, scaled layouts and spoken acceptance.
   UXBUG-014 still needs native display-scale and spoken acceptance.
   UXBUG-015 needs localized speed entry, keyboard error navigation and
   actual screen-reader announcements on supported desktops. Report
   Avalonia 12.1's incorrect AT-SPI role mapping upstream if the owner agrees.
   UXBUG-016 needs native checks of other dialog families and non-Windows
   scaled visuals and actual screen-reader speech on each desktop. Investigate
   FileCat's response to SIGTERM while a dialog is open on macOS. Capture
   Ubuntu popup menus and the Find window at 200% scale. Report Avalonia's
   synthetic macOS modifier and scaled-bitmap behavior upstream if the owner
   agrees.

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
