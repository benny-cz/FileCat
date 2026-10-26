# Remaining UX/UI audit work

Updated: 26 October 2026. Maintain this file after every validated batch. The
[audit](FileCat_UX_UI_Audit.md) defines the acceptance criteria; the
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records code, tests and
evidence by finding. Work directly from the Git checkout and push reviewed
increments to `main`.

## Open prerequisites

- Finish Mac visual layout checks for other dialogs and their unverified
  1× and scaled states using the owner-approved
  `FileCatCapture` helper in the unlocked
  desktop session. Keep the Mac display awake while finishing Mac input and
  clipboard journeys. Finish the remaining Ubuntu Linux input, clipboard,
  viewer and assistive-technology checks.
- Extend the `linux-orca-speech.py`, `win-nvda-speech.ps1`, and
  `mac-ax-focus-speech.py` journeys for the spoken criteria of other
  findings (Find results, UXBUG-015 and FCUX-040/041 form errors, tabs).
  Assert VoiceOver's actual spoken phrases for the other Mac routes. Keep Mac
  audio muted.

## Next actions, in severity order

1. **Close the final validation gate.** Require green producer, Windows,
   Ubuntu, macOS and Windows ARM64 CI lanes on the final `main` head. Fix any
   failing lane before declaring the audit complete. Rerun the complete
   final-head gate after the last audit change. If they recur, investigate
   the intermittent Ubuntu metadata-demand extra read and macOS inline Rename
   focus return to `..`; earlier runs and isolated reruns are recorded in the
   implementation status.
2. **Finish S1 operation and decision journeys.** For FCUX-001, cover
   remaining supported provider failure and cleanup paths, including races
   after the final destination content check. Finish Windows FTP/FTPS failure
   kinds beyond native interrupted Copy and Move
   cleanup across all three FTP modes, Mac FTP/FTPS failure kinds beyond
   changed-destination and dropped-connection staging behavior, and SFTP
   provider failures beyond the native destination-content, metadata and
   dropped-connection staging checks.
   For FCUX-069/070, validate
   supported cross-app Move effects and delayed Paste destination on each
   desktop. Windows still needs Explorer Cut
   presentation and supported external Move/delete-on-paste handling, if
   safely offered. On Mac, fix the scrolling form body's zero AX bounds and
   preview list with no AX children. For FCUX-004, exercise the other modal
   focus and decision routes on each desktop, including actual Orca speech
   where required.
   Preserve captured scope and mapping throughout each test.
3. **Finish the remaining S1 content and accessibility journeys.** For
   FCUX-006, verify the lossless Settings round-trip in the final CI gate; its
   structured editor extension is tracked with FCUX-036.
   For FCUX-007, finish other scaled layouts with real pointer and keyboard
   input and the remaining assistive-technology routes. Test visible Stop,
   changed-query ownership on other viewer routes, provider cancellation,
   and spoken Find results. Assistive-technology routes still need native checks.
   For FCUX-008, test native incomplete sources and changes during a read,
   other scaled displays and narrow-layout states. For FCUX-009, finish
   long-line and wrap-alignment Linux acceptance,
   font fallback, pointer navigation and spoken automation on the
   supported desktops. For FCUX-010, verify VoiceOver's other item actions
   and bounded native listings on other supported provider types.
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
   and preview behavior (044, 045, 052, 053, 055). The status table identifies
   each group's exact provider, input, scaling and assistive-technology gate.
   FCUX-031 still needs nested and scaled layouts. FCUX-032
   still needs scaled layouts, spoken feedback and non-filesystem breadcrumb
   paths. For UXBUG-019, verify actual off-source recovery with a retained,
   recoverable deleted fixture on the Windows VM; then exercise the owner's
   disposable `G:` UI route if safe to do so without using the workstation
   desktop.
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
   UXBUG-020 still needs a stable scaled Ubuntu session with matching
   compositor, Xwayland and physical uinput pointer coordinates, followed by
   its full native scrollbar journey. Finish combined Hex editor search,
   selection, scroll, zoom and pointer alignment while resizing on each
   supported desktop, including the remaining scaled contexts.

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
