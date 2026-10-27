# Native Windows acceptance runner

`win-ax-fractional-list-wheel.ps1 -AppPath <Git-built FileCat.exe>
-ReportPath <fresh guest JSON>` sends native Win32 12-unit fractional and
120-unit conventional wheel messages over an owned 200-file panel. It reads
the list's UI Automation scroll position to compare ten fractional events with
one whole event, reversal and both end clamps, then verifies unchanged source
hashes and an empty destination. Its app, private profile and report must be
inside the Windows guest's `FileCatValidation` directory.

`win-ax-fractional-viewer-wheel.ps1` uses the same owned Windows guest route
for a 300-line Text file, then its Hex mode. It compares ten 12-unit events
with one 120-unit event and reverses to the starting viewport using UI
Automation document text and scrollbar position. It checks byte-exact source
preservation and an empty destination. HexCompare uses the separate runner below.

`win-ax-fractional-compare-wheel.ps1` opens a native binary Compare window
from two owned files with one changed byte. It compares ten 12-unit wheel
messages, one 120-unit message, reversal and ordinary detents through the
HexCompare vertical scrollbar, then verifies both source hashes. It creates
only a private profile and fixture under the Windows guest's validation root.

`linux-atspi-find-stop-held.py APP REPORT.json --speech` runs a native Ubuntu
Find against an owned local fixture. A path-filtered, bounded `strace` delay
holds the child-directory read long enough to activate Stop; the runner then
checks the incomplete headline and exact scope explanation, unchanged source
hashes, and empty destination. With `--speech`, it also requires Orca to say
that only part of the scope was searched. The app and report must be below the
guest's `~/FileCatValidation`; use a fresh report path and pass
`FILECAT_NATIVE_SUDO_PASSWORD` only to this runner. The password is removed
before launching FileCat.

`win-ax-find-exclusions.ps1 -AppPath <Git-built FileCat.exe> -ReportPath
<fresh guest JSON>` uses a private local folder and a separate private data
profile with one ignored `cache` folder. It restores the Find window on-screen,
sizes it to 640 × 420, searches for an absent name, and checks the qualified
headline, visible issue count, in-window Log action, keyboard-opened log, and
unchanged fixture hashes. It changes no unrelated settings or files.

`win-ax-find-result-scope.ps1 -AppPath <Git-built FileCat.exe> -ReportPath
<fresh guest JSON>` opens an owned two-file `--list` result set in a fresh
Windows VMware profile. It invokes native toolbar Find, verifies the separate
Find within results window, read-only two-item snapshot scope and enabled Find
action, then checks that the list and both files stayed byte-exact. It closes
only its own app process. The report path must be fresh.
Add `-NarrowNoMatch` to resize that Find window to 640 × 420, search for an
absent unique name, and require a visible no-match headline plus an accessible
explanation naming the query and result-set scope. This is a distinct
completion boundary and does not retest the accepted origin without cause.
Use `-RemappedShortcut` separately to seed only the private profile's Find
Subtract binding as Ctrl+Shift+S and unbind Add. At 640 × 420, it checks the
visible effective guidance, actual Ctrl+S versus Ctrl+Shift+S result membership,
and unchanged original files. Component tests cover Settings conflicts, criteria
editing, path suggestions and an open dialog.

`win-ax-find-registry-scope.ps1 -AppPath <Git-built FileCat.exe> -ReportPath
<fresh guest JSON>` runs in the logged-in Windows VMware desktop with a fresh
profile under `FileCatValidation`. It opens Find from `HKCU\\Software`, verifies
the native Registry scope guidance, accessible search field and Close action,
and preserves an owned filesystem sentinel. It reads but does not change the
Registry and closes only its own app process. The report path must be fresh.

This runner exercises actual Win32 pointer input against a running FileCat
window. It requires a logged-in Windows desktop, FileCat, and the locally
installed Windows Application Driver listening on `127.0.0.1:4723`. It does
not connect the driver to a network interface.
`win-ax-panel-mask-theme.ps1 -AppPath <Git-built FileCat.exe> -HarnessPath
<FileCat.NativeAcceptance.exe> -ReportPath <fresh guest JSON>` runs directly
through Windows UI Automation and physical pointer input in the logged-in
test desktop; this specific script does not need WinAppDriver. It seeds a
private source, destination and data profile, selects the Psychedelic theme,
then adds a panel below each original panel by activating the destination tab
through its accessibility identity. All four Filter fields must expose `*.*`
with visible bounds. It accepts only paths under the guest user's
`FileCatValidation` folder and saves a same-stem PNG of the app window for
confirmation that
the masks are painted, verifies that fixture files and destination are intact,
and closes only its exact test app. Use a fresh report path for each run.
`linux-atspi-rename-editor.py APP_PATH REPORT.json KEY_HELPER [--narrow|--rule-change]`
checks a native Ubuntu bulk-rename editor round trip. Use the logged-in
desktop, a Git-built executable below `~/FileCatValidation`, and a copy of
the tracked `linux-uinput-key.py` in that directory. Pass the guest sudo
credential transiently as `FILECAT_NATIVE_SUDO_PASSWORD`; the runner removes
it before launching FileCat or its configured editor. It marks both fixture
files with physical Insert, opens the review with physical Ctrl+M, uses the
configured editor to save two explicit names, and verifies exact source
identities, bytes, empty target and removal of the private name list. The
review step also invokes Previous and Next through AT-SPI and checks both
complete source-to-destination paths, then focuses those buttons and uses
physical Enter and Space to check keyboard activation before opening the
editor. The
`--narrow` records the GNOME display scale and derives the app's effective
rendering scale from its 640 × 420 minimum-size window hints. GNOME's 150%
session can give Xwayland a 2× app scale. The runner requires a measured
720 × 540 app-unit window before the journey; an unmeasured or refused
resize produces a failed receipt.
`--rule-change` loads editor names, changes the mask in the same review, and
requires the final rename to use the new rule mapping instead.
`--pause-review` waits at `<report-stem>.ready` before opening the editor.
Create `<report-stem>.resume` after capturing the upper review. It then focuses
the full-path detail and waits at `<report-stem>.detail.ready`; create
`<report-stem>.detail.resume` after capturing its lower layout. Both waits are
bounded to 120 seconds and all marker files are removed when the run ends.
`linux-atspi-keybar-popup.py` checks the twelve visible key-bar labels after
physical Shift+F10 context-menu and Alt+F7 focus transitions in a logged-in
Ubuntu session. Pass an isolated Git-built `FileCat`, a JSON report directly
under `~/FileCatValidation`, and the tracked `linux-uinput-key.py`; supply
`FILECAT_NATIVE_SUDO_PASSWORD` transiently for uinput. It removes that value
before starting FileCat, preserves fixture bytes, and kills only its own app.
GNOME may reserve Alt+F7 for Move window. With `--expect-find`, the runner
requires FileCat's Find window; temporarily release GNOME's `begin-move`
binding for that route and restore its exact previous value afterward.
With `--expect-find --pause`, the runner waits at bounded `<report-stem>.menu.ready`
and `<report-stem>.find.ready` markers for native screen capture. Create each
matching `.resume` file after capture. The runner removes all four markers
when it finishes, including on failure.
When launching the app or this runner with VMware `vmrun runProgramInGuest`,
pass `-interactive`; otherwise the process starts in Session 0 and cannot
inspect or control the guest user's desktop.
`win-ax-drag-external.ps1 -AppPath GIT_BUILT_FILECAT_EXE -ReportPath FRESH_JSON`
uses a fresh guest-local two-file source and Explorer destination; add
`-Scope Archive` to stage two members from a test ZIP, or
`-Scope MixedRefusal` for a test-owned working set with one local file and
one ZIP member. `-Scope ExternalIntoArchiveRefusal` selects two files in
native Explorer and drags them onto FileCat's open ZIP panel; it records the
hover refusal, supported alternative, unchanged source and ZIP hashes, and
empty job journal. `-Scope ExternalIntoLocalReview` drags two Explorer files
onto a FileCat local target, checks the exact review destination and empty
job journal before approval, then cancels and checks unchanged source and
target hashes. `-Scope ExternalIntoVanishedTarget` removes only the test-owned
local folder after a valid native hover, then checks the changed-target or
unavailable-folder refusal, no review or job, and unchanged source. The
`3a4d7cba` accepted route used the raw UI receipt plus a read-only
same-fixture file/journal follow-up because its initial assertion expected
only the narrower unavailable-folder wording. `-Scope InternalArchiveRefusal`
is a diagnostic variant with
FileCat as the source. Launch it
with `-interactive -noWait`; when `FRESH_JSON.HoverReady` appears, capture
the VMware screen and create `FRESH_JSON.HoverCaptured` in the guest within
45 seconds. The runner releases Ctrl and the mouse in `finally`, verifies
exact copied filenames/hashes and unchanged sources, and writes a receipt.
The accepted scopes are local-to-Explorer Copy, archive-to-Explorer Copy,
local/ZIP complete refusal, Explorer-to-ZIP refusal, Explorer-to-local
review/Cancel and changed/vanished local target refusal.
`linux-atspi-sync-endpoints.py APP_PATH REPORT.json KEY_HELPER [--pause-review]`
checks FCUX-005 in the logged-in Ubuntu VM. It compares two isolated folders
with the same final name, opens Synchronize, reads both complete selectable
endpoint fields through AT-SPI, reverses direction with a physical key, and
checks the full target path in the Mirror warning. At the guest's 200% display
scale it resizes the review to 1280 × 1000 physical pixels, verifies that both
paths wrap and that the controls stay inside the dialog, then cancels and
checks both exact directory trees and file hashes. Optional `--pause-review`
creates a `.ready` marker before Cancel; create the matching `.resume` file
after capturing the isolated guest screen.
Optional `--orca-speech` starts a test-owned Orca log and requires speech of
both complete endpoints before and after reversing direction, plus the full
Mirror target when the mode changes. It stops only its own Orca instance.
Set `DOTNET_ROOT` to the guest's pinned SDK when it is not installed systemwide.
`win-ax-sync-endpoints.ps1 -AppPath <isolated FileCat.exe> -ReportPath <guest JSON>
[-PauseReview]` checks the equivalent Windows review using UI Automation and
physical Down keys. It reads both full path values and accessible names,
reverses direction, checks the exact Mirror target, resizes to a 640-pixel
logical-width window (physical size derived from measured window DPI) and
verifies field wrapping, in-bounds choices and unchanged fixture
trees after Cancel. Run it in the logged-in Win11 VM, with paths below
`%LOCALAPPDATA%\FileCatValidation`. Optional `-PauseReview` creates `.ready`;
write the matching `.resume` file after capturing the VM desktop.
Optional `-NvdaPath <signed portable nvda.exe>` starts an owned NVDA speech
log, focuses both complete endpoints before and after direction reversal, and
requires the reversed Mirror target to be spoken when the mode changes. It
stops that NVDA instance and retains the log under its private fixture.
`python3 mac-ax-sync-endpoints.py BUNDLE.app REPORT.json` checks the same
journey on an unlocked Retina Mac. Keep `mac-ax-rename-snapshot.swift`,
`mac-ax-panel-sort.py` and `mac-ax-markdown-resize.py` beside the runner and
use the signed, isolated FileCat bundle and approved `FileCatCapture.app` under
`~/FileCatValidation`. Mac output must be muted while VoiceOver runs. The test
reads both full selectable endpoint values and accessible names through bounded
native AX traversal, requires VoiceOver's actual speech for both paths before
and after reversal, selects direction and Mirror from their native popups, and
records whether VoiceOver automatically announces the complete target warning
after the popup closes (with a Tab route for diagnostics if it does not). It
resizes the review to 640 × 600 logical points, captures its
wrapped paths and warning, then cancels and verifies the exact fixture trees
and file hashes.
`win-ax-dependent-undo-crash.ps1 -DotnetPath <guest-dotnet.exe>` is a
Windows-only FCUX-018 check. Build the app and Core crash harness in the same
guest Git checkout first, then run this script in the interactive desktop. It
creates one disposable fixture under `AppData\Local\FileCatValidation`, kills
only its own held inverse worker, opens that checkout's FileCat build, checks
Cancel, reviewed rename fallback and the explicit Mark reviewed decision, and
checks exact bytes and journal state throughout. Its result is
`AppData\Local\FileCatValidation\win-undo-last.json`; the fixture remains for
inspection. The script closes only the FileCat instance it started.
`win-ax-undo-mixed-report.ps1 -AppPath <isolated FileCat.exe> -ReportPath
<guest-local.json>` checks the native FCUX-018 report beyond row 30. Run it
with `-interactive` in the logged-in Windows VM. It moves 42 exact files,
holds the 36th target open exclusively and changes the 37th, then approves
Undo. It requires 40 byte-exact restorations, one retained failed inverse and
one safely kept changed file. The actual Undo report's Copy all text must
contain all 42 outcomes and complete fixture paths after row 30. It leaves its
fixture for inspection and closes only its own FileCat instance. It also
resizes the native review to 720 × 540 logical pixels, requires the complete
affected path to wrap and both decisions to remain inside, then resizes the
report to 600 × 460 and requires Copy all to remain reachable.
Pass `-NvdaPath <signed portable nvda.exe>` to own an NVDA speech log during
the same mixed Undo run. The test moves focus from Cancel to the complete
inverse row and requires NVDA itself to speak both full paths; it stops only
its own NVDA process. The same runner uses Next report line and requires NVDA
to speak both complete paths from one visibly accessible outcome; all 42
outcomes and Copy all remain checked independently.
`linux-atspi-undo-narrow.py APP_PATH REPORT.json` checks the matching native
Ubuntu 200% route with a fresh isolated Move/Undo fixture. Run it in the
logged-in desktop with a Git-built executable and report below
`~/FileCatValidation`. It resizes the review to 720 × 540 app units and the
retained report to 600 × 460, checks both full paths and decision bounds,
verifies exact restored bytes, and reads Copy all from the guest clipboard
through a separate GTK 3 process. It closes only its own FileCat process.
Pass `--orca-review-speech` to start an owned Orca debug log and require
actual speech of both complete inverse paths after a controlled focus
transition. The runner also requires the report's AT-SPI text interface to
contain the full restored path, then uses Next report line and requires Orca
to speak both complete paths from the visible outcome.
`win-ax-display-scale.ps1 -Percent 150|200|100 -ReportPath <guest-local.json>`
selects a Windows display scale through the Settings accessibility control.
It refuses a noninteractive session, a non-VMware host or a report outside
`FileCatValidation`. Run `win-ax-undo-mixed-report.ps1` in a new process at
each scale and require its measured DPI of 144 or 192. Restore the original
100% scale afterward and verify 96 DPI from a fresh process; the scale
setter's own process can retain its old DPI value.
`python3 mac-ax-undo-narrow.py BUNDLE.app REPORT.json` runs a real Mac
Move, narrow Undo review, Cancel, reopened approval and retained report. It
requires the signed Git bundle, `mac-ax-panel-sort.py`,
`mac-ax-markdown-resize.py`, `mac-ax-markdown-find.py`,
`mac-ax-rename-snapshot.swift` and owner-approved `FileCatCapture.app` in
the isolated validation folder. Direct AX checks both complete highlighted
paths, wrapped row geometry and in-bounds choices at 720 × 540 points;
native captures record the review and 600 × 460 report. The runner verifies
unchanged bytes after Cancel, exact restoration after approval, and Copy all
through macOS `pbpaste`. With Mac audio muted and the owner-approved VoiceOver
controls, it also requires actual speech of both complete review paths,
retained-report paths and a navigated Next report line. It closes only its own
app process.
`win-ax-undo-active-close.ps1 -AppPath <isolated FileCat.exe> -ReportPath
<guest-local.json> [-FileCount 5000] [-Action KeepCancelReport|StopExitRestart]`
checks active Undo ownership in the logged-in Windows VM. The first route
requests close while a 5,000-file inverse is running, chooses Keep FileCat
open, cancels that inverse and copies its complete retained report. It requires
both restored and unattempted actions, one byte-exact copy of every fixture
file, and full paths beyond report row 30. The second route chooses Stop them
and exit, waits for FileCat to close after the safe step, requires a settled
Undo journal and an exact partial file inventory, then reopens the same profile
and checks every byte again. Each run uses a fresh isolated fixture and closes
only its owned FileCat process.
`win-ax-modal-guard.ps1` is a separate Windows UI Automation check that does
not need Windows Application Driver. Run it in the interactive guest session
with `-AppPath` pointing to an isolated `FileCat.exe` below the guest's
`AppData\Local\FileCatValidation` directory and `-ReportPath` naming a local
JSON report. It creates a fresh source/target/profile fixture, opens Copy,
invokes the obscured toolbar and key-bar Copy/Delete actions through UI
Automation, requires one unchanged review and no target, then cancels and
checks the source hash. It closes only the test process it launched.
`linux-atspi-copy-option-correction.py` runs in a logged-in Ubuntu desktop
session against a Git-built executable under `~/FileCatValidation`. It creates
its own fixture, opens Copy, expands Options, submits invalid `-1`, requires the
visible error and matching accessible speed-field help in the still-open form,
corrects the same field to `0.0625`, then checks the exact copied bytes and
unchanged source. Its JSON report records the app and fixture hashes. It closes
only the instance it launched.
`win-ax-copy-option-correction.ps1` performs the same correction and exact-byte
checks in the logged-in Windows VM with UI Automation. Pass an isolated
`-AppPath` under `AppData\Local\FileCatValidation` and a guest-local
`-ReportPath`; run it with VMware's `-interactive` switch. It checks both the
visible error and the speed field's accessible HelpText, and closes only its
own test instance.
`win-ax-dialog-pointer-dismiss.ps1` opens the Registry chooser in an isolated
Git-built app, verifies the accessible close X, then clicks the X and the
backdrop with native Win32 pointer input. Pass the same isolated `-AppPath` and
guest-local `-ReportPath`. It checks the foreground process ID before sending
F7, so an older FileCat window cannot receive its keystrokes. A guest desktop
that refuses to foreground the isolated app fails safely before the check.
Pass `-MinimumOwner` to restore and settle the owner at 640 × 420 DIP before
opening the chooser, then require its visible native options and mouse exits
to remain inside the owner. The Registry chooser has no Cancel footer; its
close X and backdrop are its intended pointer cancellation controls.
Combine it with `-RegistryForm` to select the named Key option and inspect
the actual Create key form: its input, Cancel, Next and X stay inside the
minimum owner, then separate native X and backdrop clicks cancel without
submitting a Registry change. The runner saves a full frame beside this
minimum-size receipt for visual inspection.
Pass `-DialogKind Folder` to repeat the same check on the Create folder dialog
with an empty disposable filesystem fixture; the default remains `Registry`.
Pass `-DialogKind Copy` to open the Copy review for one disposable file and
verify that both pointer exits preserve its SHA-256 and the empty target.
Pass `-DialogKind Move` to check the corresponding Move review and the same
source and destination invariants.
Pass `-DialogKind Delete` to open the Recycle Bin review for the same owned
file; both pointer exits must preserve its source SHA-256 and empty target.
The runner saves a close-button crop beside its JSON report, measures the
high-contrast X pixels against the button background and requires their
centroid within 1.5 physical pixels of center. It also records the window DPI,
accessible name/help, button bounds and both native pointer outcomes. Run the
same five dialog kinds after changing the isolated VM's native Display Settings
scale. The Windows VM runs at 100% by default; restore that setting after any
scaled acceptance session.
`win-ax-remote-stage-hint.ps1 -ReportPath <guest-local.json>` runs in the
logged-in Windows VM after an exact Git checkout and Release build. Its
`vm-clone-build.json` names the isolated executable and SHA-256. It seeds a
remote-destination interruption with no server connection, opens that exact
FileCat instance, invokes local cleanup review through UI Automation, and
requires the journal-derived `.fc-<job-id>-` search hint in the row, approval
dialog and saved report. It also checks that the local source hash is unchanged,
the target folder is empty and the remote journal remains open. No server file
is inspected or deleted by this check. The report includes all evidence fields.
`win-ax-ftp-crash-stage-review.ps1 -AppPath <isolated FileCat.exe> -DotnetPath
<guest dotnet.exe> -HarnessPath <Git-built FileCat.RemoteCrashHarness.dll>
-PythonHelperPath <copied win-ftp-owned-stage.py> -ReportPath <guest-local.json>
[-Kind Copy|Move]` covers the real FCUX-001 FTP crash boundary in the logged-in
Windows VM. Install `pyftpdlib` in that isolated guest and run with VMware
`-interactive`. The helper serves only `127.0.0.1` and an owned directory below
`FileCatValidation`. The runner kills only its own rate-limited upload worker
after the hidden stage is created, then requires the exact partial stage,
unchanged source and open journal. FileCat's native recovery row, confirmation
and retained report must name the journal-derived candidate prefix without
deleting the remote object. The helper then retrieves the exact stage over FTP,
checks it is a prefix of the owned source, deletes only that object, and checks
the empty remote manifest. Finally, the runner explicitly marks that journal
reviewed in FileCat and verifies its closure. Copy and Move use separate fresh
fixtures; their JSON reports and fixture roots remain for inspection.
`win-ax-ftp-replace-warning.ps1 -AppPath <isolated FileCat.exe>
-PythonHelperPath <copied win-ftp-owned-stage.py> -ReportPath <guest-local.json>
[-Protocol ftp|ftpes|ftps|sftp] [-NvdaPath <signed portable nvda.exe>]`
checks native FTP, explicit-TLS FTPS, implicit-TLS FTPS and SFTP replacement review without
replacing the target. For SFTP, supply a fresh owned Ubuntu fixture ID, the
independently recorded SSH host-key fingerprints and the test credential with
`-SftpFixtureId`, `-ExpectedHostFingerprints` and `-SftpPassword`. Run it with
VMware `-interactive` against the exact Git build,
after installing `pyftpdlib` and `pyOpenSSL` in the isolated guest. The TLS
fixture generates a new self-signed certificate for each run; implicit TLS
starts before the FTP greeting. The runner
requires FileCat's unverified-certificate prompt and accepts it for this
connection only; it never alters operating-system trust. Plain FTP instead
requires explicit plaintext consent. It signs in and chooses Replace in
the Copy review. The runner verifies FileCat is foreground before sending the
Location Enter key. At 720 × 540 logical pixels it requires the complete
final-check warning and Cancel to lie inside the real window. Keep both must
hide the warning; Cancel must preserve both exact source and remote target
hashes. It then starts a fresh Ask copy, follows the waiting operation's
Review decision action, requires the per-item conflict warning and Skip to
stay inside the narrow viewport, and chooses Skip. Both hashes and the single
server target must remain unchanged. The fixture and JSON receipt remain for
inspection. `test_win_ftp_owned_stage.py` smoke-tests all three loopback fixture
protocols; CI runs it on Windows, Windows ARM64, Ubuntu and macOS.
With `-NvdaPath`, the runner starts an owned NVDA 2026.2 instance from the
isolated validation folder and requires its actual speech log to include the
complete final-check risk and Keep both choice in both the Copy review and
per-item conflict. It uses extended native navigation keys while NVDA runs,
stops only its own NVDA process, and retains the log with the fixture.
For SFTP, seed one target in the Ubuntu VM with
`python3 linux-sftp-replace-fixture.py seed <fresh-32-hex-id>`, copy the seed
receipt, and run the Windows harness with `-Protocol sftp`,
`-SftpFixtureId <id>`, `-ExpectedHostFingerprints <comma-separated SHA256
fingerprints from seed receipt>` and `-SftpPassword <test-VM credential>`.
The harness requires FileCat's New server key prompt and an exact visible
fingerprint match before choosing Connect once. It checks the same narrow
transfer review and per-item conflict, then Cancel and Skip. Afterward run
`python3 linux-sftp-replace-fixture.py verify <id>` in Ubuntu and inspect its
separate receipt: the remote target must retain the exact SHA-256 and be the
only file. The Windows receipt intentionally leaves `TargetUnchanged` null
because only the independent Ubuntu verifier can establish that fact.
`linux-atspi-ftp-warning-speech.py APP_PATH REPORT.json KEY_HELPER FTP_HELPER
ftp|ftpes|ftps|sftp` checks the same consequential replacement warning in the
logged-in Ubuntu desktop. Use the isolated Git-built `FileCat`, the tracked
`linux-uinput-key.py` and `win-ftp-owned-stage.py` helpers copied below
`~/FileCatValidation`, and the local `ftps-venv` fixture environment. Supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to the runner process for physical keys;
SFTP also needs the transient `FILECAT_NATIVE_SFTP_PASSWORD`. The runner
removes both from child environments, refuses an existing Orca instance,
starts its own Orca instance on the logged-in Wayland session with a speech
log, and owns a fresh loopback server or SFTP folder. Implicit-TLS FTPS starts
TLS before the server greeting.
For SFTP it compares the new host-key prompt with independently read SSH
server public keys before Connect once. It chooses Replace, requires Orca to
speak the complete final-check risk and Keep both choice, then chooses Keep
both and Cancel. A second Ask copy opens the per-item conflict; Orca must
speak the same complete warning before Skip. It resizes FileCat to 720 × 540
logical pixels, records actual GNOME scale and both warning/action bounds,
and requires exact source and target hashes and a one-file server manifest.
Run separate fresh sessions at 100%, 150% and 200% and restore the VM's
original display scale afterward.
`mac-ax-remote-warning.py BUNDLE.app REPORT.json FTP_HELPER ftp|ftpes|ftps|sftp`
checks a signed, isolated Git-built Mac bundle with VoiceOver running and Mac
output muted. It owns a fresh loopback FTP, explicit or implicit FTPS, or
public-key-only SFTP server. The SFTP fixture creates disposable host/client
keys below its own folder, requires the new host-key prompt to show the independently
computed fingerprint, and accepts that key only for the current connection.
It does not change the Mac's system SSH configuration or `authorized_keys`.
The runner records the certificate or plaintext consent for FTP, focuses the exact left source row using
the PID-checked `mac-ax-set-field.swift` helper, and requires the Copy review
to name the exact remote destination. It chooses Replace and waits for both
the complete visible final-check warning and VoiceOver's full automatic
phrase. Cancel must preserve source and remote target hashes and the one-file
server manifest. A separate Ask copy must open the background Review decision,
automatically speak the same complete per-item warning, and preserve the
exact remote target after Skip. When the approved capture helper is available,
the runner dismisses only an overlapping Mac Apple Account “Later” notice,
then retains a screenshot of the first warning. The report and disposable fixture stay below
`~/FileCatValidation`; `--probe` stops after exposing native options and is
for diagnosing the Mac accessibility tree. `--narrow` repeats the full
journey with both the Copy warning and per-item conflict at 640 × 480 Mac
points. It records both screenshots and requires the decision actions to
remain inside the native window. `--pointer-probe` skips the keyboard panel
switch after SFTP Connect once and requires the exact local source to activate
by pointer and open the Copy review; it makes no transfer. `--pointer-narrow`
uses the same pointer route and completes both 640 × 480 warning reviews.
`--ax-probe` and `--ax-narrow` activate the same exact source tab through
macOS accessibility, independently of physical pointer focus.
`--change-during-upload` uses FTP or either FTPS mode to start a rate-limited
Replace of a 1 MB local source. Only after the exact owned stage has partial
bytes does another client change the test-owned destination. The runner
requires a visible changed-destination refusal, removal of the owned stage,
an unchanged local source, and the newer destination as the only server item.
This focused failure-path route checks the visible replacement warning;
the default route separately checks its automatic VoiceOver speech.
`--change-content-during-upload` keeps the destination's size and exact
modification timestamp while changing its bytes, then requires the more
specific changed-content refusal with the same preservation and cleanup.
`--disconnect-during-upload` stops only its owned loopback FTP/FTPS server
after the unpublished stage contains source bytes. It chooses Skip at the
native operation decision, opens Details, and requires the retained-stage
warning. The partial stage must remain an exact source prefix; the original
source and remote target must keep their exact hashes. The report records the
stage name, size and final two-item server manifest for deliberate cleanup.
`--disconnect-and-speak` runs that same journey, selects the visible issue
row, and requires VoiceOver's actual last phrase to include the retained-stage
reason and its complete provider path while system output remains muted.
Set `FILECAT_FORCE_KEYBOARD_SOURCE=1` in a disposable run to exercise the
Mac Tab/End fallback when AX reports a zero-size frame for the visible source
tab after a certificate decision.
`mac-sftp-owned-selftest.py REPORT.json` independently verifies that the
disposable SFTP service lists only the expected fixture file and stops its
server process afterward. The same script's `prepare` mode supplies the
isolated server configuration to the native VoiceOver journey.
`linux-atspi-dialog-pointer-dismiss.py APP_PATH REPORT.json` opens Create folder
in an isolated Git-built Linux app below `~/FileCatValidation`. It verifies the
close control's accessible name, description and pointer bounds, then uses
XTest mouse input for the X and backdrop. The runner checks the active X11
window PID before each click and preserves source/target fixtures on
cancellation. Run it in the logged-in desktop session with an X11 pointer
bridge; if Wayland blocks XTest movement, the JSON report explicitly marks the
input bridge unavailable and does not count the pointer route as passed.
On the Wayland Ubuntu VM, pass `linux-uinput-touch.py` as the optional third
argument after the report. The runner sends two physical taps through a
disposable uinput touchscreen and records the input type in JSON. Supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to the runner process or use a scoped
noninteractive sudo rule; the runner removes this variable before launching
FileCat and passes authentication to `sudo` on stdin. The helper holds no
credential or persistent input device.
Pass `linux-uinput-mouse.py` as that optional helper to exercise an actual
relative-motion mouse and left button through the same guarded AT-SPI journey.
The helper clamps the pointer to the upper-left corner, moves in small steps,
clicks the bounded point and destroys its uinput device before returning.
Append `Copy`, `Move` or `Delete` after the helper to open the corresponding
review twice. The runner selects its exact `a.txt` source, checks the Copy/Move
destination or the Delete file and Recycle Bin wording, uses the specified input for the
close X and backdrop, and requires unchanged source SHA-256 and an empty
target. On the Wayland VM, the mouse variant uses
the owned absolute-touch helper only to select the narrow source row; the X
and backdrop clicks under test remain native mouse clicks. The touch variant
uses touch for all three taps. The default `Folder` journey remains available.
`linux-atspi-rename-cancel-focus.py APP_PATH REPORT.json linux-uinput-key.py`
uses a fresh three-file fixture and physical End, F2, F7, F8 and Escape keys.
It requires each canceled action to restore the exact focused row and keep
source/target bytes unchanged. Pass `--dialog-only` to exercise Create folder
and Delete while the stricter inline Rename editor's AT-SPI exposure is under
repair. Both routes record a bounded failure probe and clean their owned
fixture and app. Give the runner the transient sudo password only in
`FILECAT_NATIVE_SUDO_PASSWORD`; it removes the variable before starting
FileCat.
`linux-atspi-selection-keyboard.py APP_PATH REPORT.json KEY_HELPER TOUCH_HELPER`
creates two byte-checked files in an isolated profile and exercises a physical
touch anchor followed by Shift+Down, Up and Shift+Down from the transient
uinput keyboard. AT-SPI must report the exact mark summary and focused row at
each step; source hashes and the empty target are checked at the end. Append
`--narrow` for a measured 720 × 540 window. Append
`--sort MOUSE_HELPER` for a four-file sort reversal followed by a physical
Shift+click range. On Wayland, the transient uinput mouse is first moved and
its actual pointer position is measured and calibrated inside the target row;
the button event is sent only after that check. The runner requires the exact
two-file 13-byte mark, focused target and unchanged fixture bytes. Append
`--sort-control MOUSE_HELPER` to require a bounded, named Sort button in both
populated panels, open the source menu with native pointer input, select its
accessible Name choice, and observe a four-file reversal before the same
Shift+click range. `--sort-control-narrow MOUSE_HELPER` repeats it in a
measured 720 × 540 window. Append
`--filter MOUSE_HELPER` to enter `*|b*` and then `*` with a physical keyboard,
proving that the hidden `b.txt` anchor retires: after restoring the list, a
physical Shift+click on `d.txt` must mark only `c.txt` and `d.txt` (14 bytes).
Append `--tab MOUSE_HELPER` to touch `d.txt`, open a second tab with Ctrl+T,
enter its owned subfolder, focus `e.txt` with Home/Down and physically
Shift+click `f.txt`; the first tab's anchor must not carry over. All three
four-file variants also accept `--sort-narrow`, `--filter-narrow` or
`--tab-narrow` for a measured 720 × 540 window, respectively. Each checks
the exact selection summary, focus, source hashes and empty destination.
Append `--refresh MOUSE_HELPER` or `--refresh-narrow MOUSE_HELPER` to touch
`b.txt`, remove only that owned anchor and add `0.txt`, then physically send
Ctrl+R. After focus moves to `d.txt`, a calibrated physical Shift-click on
`0.txt` must mark exactly all four current files for 25 bytes, focus `0.txt`,
and preserve their expected hashes and the empty target. The narrow route
measures 720 × 540. A stale numeric anchor at the old `b.txt` row would
select a smaller range.
Append `--ctrl MOUSE_HELPER` or `--ctrl-narrow MOUSE_HELPER` to touch
`b.txt`, then physically Ctrl-click `d.txt` with a calibrated uinput mouse.
AT-SPI must show only `d.txt` marked for 6 bytes; a subsequent physical
Shift-click on `c.txt` must preserve that mark and add `b.txt` and `c.txt`
for 19 bytes, with `c.txt` focused. The narrow route measures 720 × 540;
both keep exact source hashes and an empty target.
Append `--touch-modifiers` or `--touch-modifiers-narrow` for Wayland display
scaling, where Xwayland's pointer coordinates can differ from physical input.
The runner touches `b.txt`, keeps a physical Ctrl key down until the touch on
`d.txt` has completed, then does the same with Shift on `c.txt`. AT-SPI must
expose exactly `d.txt`, then `b.txt`, `c.txt` and `d.txt` as marked, with
6 and 19 bytes respectively; it checks exact fixture hashes and an empty
target. Each report records the live `gdctl` display scale when available.
On the isolated GNOME VM, use `gdctl set --logical-monitor --monitor Virtual-1`
with `--mode 1920x1200@59.950 --scale 1.5 --primary` or
`--mode 2560x1600@59.987 --scale 2.0 --primary`; restore its original
`--mode 1280x800@60.000 --scale 1.0 --primary` afterward. The native
100%, 150% and 200% reports must show those exact scales.
Append `--deactivate` or `--deactivate-narrow` to check a Shift+Down gesture
whose Shift key remains physically held while a test-owned GTK window takes
focus. The runner first marks `b.txt` with Insert, unmarks it with held
Shift+Down, changes the active native window, releases Shift there and returns.
A fresh Shift+Down must then mark only `c.txt` (8 bytes) and focus `d.txt`;
source hashes and the empty destination are checked. The narrow variant
requires a measured 720 × 540 window.
Append `--command-line` or `--command-line-narrow` for the same held-Shift
mark/unmark sequence, then physically touch FileCat's accessible command line
while Shift is still down. The runner requires the command-line entry to own
focus, releases Shift there, presses Escape to restore list focus, and requires
a fresh Shift+Down to mark only `c.txt` (8 bytes) with `d.txt` focused. Both
variants check exact source hashes and an empty destination; the narrow variant
measures 720 × 540.
Both held-Shift routes now use a fresh, test-owned release signal, so the
physical key stays down until the focus change has been observed; the runner
asserts the helper is still holding it before and after that change.
Pass the one-time guest sudo
credential only to the runner process; it is removed before FileCat starts.
For `hex-editor-search-native`, put the fresh `selection-fixture.json` beside
the report path. If multiple isolated FileCat windows have the same title, set
`FILECAT_NATIVE_TEST_APP` in the runner process to the exact test app
`FileCat.exe`. The runner accepts only an executable below the guest's
`AppData\Local\FileCatValidation` root and selects its window by process ID.
Its `nativePointerFocused` result distinguishes a raw pointer selection from
the element-targeted click fallback. A passing Hex search with that value false
still leaves the raw pointer route for separate scaled acceptance.
`firstPointerFocused` shows whether one click sufficed; the runner retries the
same physical click once and records the foreground state on both sides of the
first click.

Run `FileCat.NativeAcceptance.exe find-resize <report.json>` in the Windows
desktop session. The runner opens **Find files** if needed, drags its lower-right
corner outward and back, and writes the measured window dimensions. It fails
unless both dimensions grow substantially and return to their original size.
Use `advanced-resize` with the same report argument to open Advanced criteria,
repeat the drag, and require its eight tested inputs and actions to remain
visible in the native UI Automation tree.

Run `editor-picker-cancel <report.json>` to check that the Windows editor
program picker opens and Cancel returns to the unchanged Settings draft. Run
`editor-program-preview <report.json>` to select the existing
`C:\Program Files\dotnet\dotnet.exe` sample, inspect the nonexecuting preview
with a spaced filename, cancel Settings, and confirm the original editor value
is restored when Settings reopens. The sample program must already be installed
in that location. Run `editor-keyboard <report.json>` to Tab from the program
field to Browse, activate the picker with Enter, dismiss it with Escape, and
confirm Settings remains open. These checks leave saved Settings unchanged.

For Markdown acceptance, copy the tracked
`docs/validation/UX_UI_IMPLEMENTATION_STATUS.md` from the Git checkout into a
guest folder and open it in FileCat's Page viewer. Run
`markdown-search <report.json>` to exercise Find, Next and Previous against
the rendered page and verify its visible directional match status. Run
`markdown-resize <report.json>` to drag the viewer wider, measure the native
table width and prose reflow, drag it back and restore its exact original
bounds. Run `markdown-narrow <report.json>` to drag the viewer narrower and
verify prose reflow, a document that stays within the window, and a
keyboard-focusable table region with its own horizontal overflow. The runner
allows a 60-second local UI Automation read because a
long rendered Markdown table produces a large native accessibility tree.
Both modes require the fixture viewer to be open on the guest desktop.

For interrupted-transfer acceptance, run `seed-recovery recovery-fixture.json`
in the guest. It creates a new isolated folder below the guest user's
`AppData\Local\FileCatValidation\Recovery`, writes two small source files, and
uses FileCat Core's real journal writer to retain a filtered move intent. The
generator has zero worker slots, so it cannot start that move. Launch FileCat
with `--new-instance --data <data> --left <source> --right <destination>` using
the paths in the seed report. Run `recovery-review <source.xml>` to open the
native continuation review, then `recovery-cancel <report.json>` with the seed
report in the same folder. That mode presses Enter on default Cancel and checks
the journal and files remain intact. Finally run
`recovery-continue <report.json>` to approve the reviewed move and verify that
only `photo.jpg` reaches the destination while excluded `notes.txt` stays at
the source. These modes mutate only their isolated guest fixture. A seeded
begin-only journal exercises startup recovery without killing a live process.
On Windows, `win-ax-recovery-hide-reopen.ps1 -AppPath <Git-built FileCat.exe>
-FixturePath <seed-recovery.json> -ReportPath <fresh report.json>` covers only
the native **Hide for now** boundary. It selects the sole interrupted row,
checks the notice disappears, closes and restarts its owned app, and requires
the same notice and Mark reviewed action to return with identical journal,
source bytes and empty destination. On that same still-open fixture,
`win-ax-recovery-report-reviewed.ps1` opens the distinct **Review report**
window and uses Copy all, checks the report's documented JSON-escaped
complete paths, then Cancels **Mark reviewed…** once before explicit
approval. It requires unchanged source/destination bytes and a saved
closure-confirmed full-path reviewed record. After the reviewed decision,
`win-ax-reviewed-report-reopen.ps1` restarts only the
test-owned profile, opens the saved report through Operations, and requires
Copy all to exactly equal the durable record with both full paths; it does
not repeat the review or approval. For the separate narrow spoken
boundary, `win-nvda-recovery-review.ps1 -AppPath <Git-built FileCat.exe>
-FixturePath <still-open seed-recovery.json> -NvdaPath <signed portable nvda.exe>
-ReportPath <fresh report.json>` runs in the interactive Windows guest at
900 × 650. It reads NVDA's actual speech for the Mark reviewed consequences
and all retained-report lines, then Cancels and verifies the exact open
journal and fixture files. It accepts only a signed NVDA and fixture paths
under the guest's `FileCatValidation` folder; it closes only its owned app
and NVDA processes. Use a separate still-open fixture from the one explicitly
closed by the reviewed-decision runner.

For an actual Core worker interruption, run `interrupt-live live-fixture.json`
in the guest. Its nonzero exit is expected: the runner terminates itself after
the rate-limited copy has written bytes and a durable journal. Run
`inspect-live live-inspect.json` with `live-fixture.json` beside that report;
it checks that the source remains, the target is unpublished, and the journal
can continue. Launch a separate FileCat instance with the live report's
`--data`, `--left` and `--right` paths, then use `live-review <source.xml>` and
`live-continue <report.json>`. The latter verifies the 8 MiB photo content,
the excluded note and cleanup of the interrupted staging file. Only the
guest-local validation root is used; the process terminated by this test is
the runner's Core worker, not the FileCat desktop process.

For an actual FileCat desktop-process interruption, run
`seed-desktop desktop-fixture.json` and launch the isolated build with the
reported `--data`, `--left` and `--right` paths. `desktop-prepare <source.xml>`
focuses `photo.jpg`, opens Copy and expands Options. Place the seed report
beside `desktop-interrupt <report.json>`; it enters a 64 KiB/s limit, starts
the 32 MiB copy, waits for a journal and staging file, and terminates only
the FileCat process whose executable is in the guest validation folder.
After Windows releases the staging handle, it requires the nonzero source
marker in staging, a recoverable journal with the rate choice, an intact
source and no published target. `inspect-desktop <report.json>` can recheck
that state. Relaunch FileCat on the same fixture and run
`desktop-continue <report.json>` to review the rate and conservative Skip
policy, approve Continue copy, compare the complete source and target bytes,
and require staging cleanup. The runner never targets a host process.

For an incomplete legacy record, run `seed-legacy legacy-fixture.json` in the
guest, then launch an isolated FileCat instance using its reported `--data`,
`--left` and `--right` paths. `legacy-review <source.xml>` opens the new-transfer
form and `legacy-start-review <source.xml>` advances to its final confirmation.
Keep the seed report beside `legacy-cancel <report.json>`: it checks that the
confirmation says this is a new transfer, that Cancel retains the journal and
source, and that the destination stays empty. Then run
`legacy-new-copy <report.json>` to review the form again and explicitly approve
the new copy. It checks the destination mapping and both files' contents.
On a build with highlighted recovery confirmations, run
`legacy-highlight <report.json>` while the final review is open. It requires
the visible choices, one-root name mapping, separate Important effects callout,
and approval button in reading order. It opens the full-review disclosure and
checks that the complete mapping becomes available without starting the copy.
`legacy-resize <report.json>` drags the final review's resize grip wider and
taller, then restores it. It checks that the destination path text receives
the extra width, the key choices remain available, and approval is still
pending. Run it after `legacy-highlight`, because that check also verifies the
initial Cancel focus before interacting with the review.
`legacy-stop <report.json>` gracefully closes only the isolated VM build after
the review; it verifies that build's executable path before closing it.
The fixture uses a real journal with its transfer-intent fields removed and
CRC rewritten, so startup follows the legacy path without inventing a ZIP or
modifying user data.

For the unsupported-provider refusal case, run `seed-remote-source
remote-fixture.json` or `seed-remote-destination remote-fixture.json` in a
separate guest validation folder for each case. The seed writes a versioned
journal containing an unavailable SFTP endpoint and a local sentinel file;
it never connects to that endpoint. Launch the fixture's `app` executable
with `--new-instance --data <data> --left <source> --right <destination>`.
Keep `remote-fixture.json` beside `remote-recovery-native <report.json>`.
The native check focuses that exact isolated build, scrolls its recovery row
into view and requires reselection guidance, an opened retained report, no
prefilled Continue or new-transfer action, a non-continuable journal,
unchanged local bytes and an empty local destination.
`FILECAT_NATIVE_TEST_APP` can point the seeder to a
different Git-built `FileCat.exe` beneath the guest's FileCatValidation root.
These cases test refusal for an unavailable provider. They do not establish
interruption or recovery behavior for a supported live remote connection.

For a live SFTP-source interruption in this VM pair, use the Ubuntu
`benny@192.168.58.129` SSH account from the Windows guest. Create a fresh
32-lowercase-hex fixture
ID under the Ubuntu account's `~/FileCatValidation/SftpRecovery/<id>`, place
`probe.txt` there containing `FileCat SFTP source marker <id>` followed by
a newline, then run
`linux-sftp-recovery-seed.py <id>` in that guest. Its exclusive-create
`slow.bin` is 16 MiB and the adjacent manifest records its SHA-256 and
prefix marker. Make matching Windows `data` and `destination` folders beneath
`%LOCALAPPDATA%\FileCatValidation\SftpRecovery\<id>`. Use FileCat to copy the
probe from the live SFTP source first, checking its bytes at the destination.
Put `sftp-live-fixture.json` beside the runner reports with `id`, exact
`app`, `data`, `destination`, `source`, `size`, `sha256`, and `marker`;
store no authentication secret in the fixture or repository.

Open the live `slow.bin` Copy form, expand Options, then run
`sftp-live-interrupt <report.json>`. It sets a 0.0625 MB/s rate, waits for
journaled staging, and terminates only the validated fixture process.
Restart the same app with
`--new-instance --data <data> --left <source> --right <destination>`.
`sftp-live-recovery-native <report.json>` checks the
reselection guidance, absence of misleading continuation, retained staging
and report, unpublished target and unchanged local probe. Run
`sftp-live-cleanup-native <report.json>` once to approve the scoped partial
cleanup, then `sftp-live-verify <report.json>` to check the closed journal,
saved reviewed report and unchanged local bytes. Rehash `slow.bin` in the
Ubuntu guest after cleanup against the original manifest. The cleanup
verification is repeatable; the cleanup action requires an interrupted row.

For a live SFTP-destination interruption, use the same isolated VM pair and
fresh 32-lowercase-hex ID. Create a 16 MiB local `upload-source/up-slow.bin`
with a unique prefix marker and a fresh empty Ubuntu `upload-destination` in
the matching fixture root. Record the source SHA-256. In FileCat, open a Copy
to `sftp://benny@192.168.58.129/home/benny/FileCatValidation/SftpRecovery/<id>/upload-destination`,
set 0.0625 MB/s, and interrupt only the verified FileCat fixture process
after the hidden `.fc-*` file has nonzero marker bytes and the journal is
durable. Restart the same `--data` profile with a local panel at the fixture
root. Put the fixture JSON beside the runner output and run
`sftp-upload-cleanup-native <report.json>`. It checks that the row and decision
warn about server files, the exact remote target is highlighted, local cleanup
retains the journal and report, and the source hash does not change. Inspect
the hidden file on Ubuntu by exact resolved path, owner, size, marker and
checksum, and verify that the public target is absent. Resolve that exact
test file before running `sftp-upload-mark-native <report.json>`, which checks
the final warning, explicit closure, retained report and source hash. Neither
runner mode contains a credential or claims to inspect the remote file.

For native Windows clipboard interchange, run `seed-clipboard
clipboard-fixture.json` in the guest and launch the current Git-built FileCat
with its reported `--data`, `--left` and `--right` paths. Keep that seed report
beside every following report. The fixture contains `a.txt` and `b.txt`.
`places-native <report.json>` measures the source panel's visible Places row
at normal, narrow, wide and restored window sizes. It requires responsive
membership and More overflow without changing the fixture. To verify a
finished transfer average, run `clipboard-copy <source.xml>` to publish a
FileCat-owned Copy of `a.txt`, then `clipboard-paste <report.json>` and
`operation-open-details <source.xml>`. Run `operation-average-native
<report.json>` while the finished Operations drawer is open. It checks exact
source and target bytes, the visible `Average transfer speed:` label in the
selected finished operation's details, and the same average in
Copy report and Open report's Copy all text. The two exports may have distinct
  capture timestamps; their remaining report bodies must match. The runner
  also measures the window at normal and 760 × 620 pixel sizes, requires the
  same visible average after narrowing, and restores the exact original
  Win32 window size before it reports success.
Run `operation-save-native <report.json>` with the same finished drawer open.
It chooses a new guest-local report path in the validation folder through the
native Save picker, then requires the saved report's average, actual processed
bytes, one completed item, and unchanged byte-exact source and target files.
`win-ax-operation-focus.ps1 -AppPath GIT_BUILT_FILECAT_EXE -ReportPath FRESH_JSON`
checks the Running-job focus boundary in a Windows VM. It creates eight
test-owned completed folder jobs, submits a ninth rate-limited 64 MiB Copy,
and requires a new journal plus visible Copying progress before reopening
Details. The selected Running row must be visible and the source hash exact.
It invokes the Copy approval inside the named dialog, avoiding the key-bar
Copy button, and closes only its owned app even if an active-copy close prompt
persists. The dated `e7b1dbdf` receipt accepts this focus route; native live
issue speech remains separate. Add `-CheckSelectionPreservation` for the
distinct route that deliberately selects completed `finished-08` after the
ninth Copy is Running and requires that selection to survive a later live
Copy status refresh with the exact source hash intact. The dated `e7b448b7`
receipt accepts that scope.
`win-nvda-operation-issues.ps1 -AppPath GIT_BUILT_FILECAT_EXE -NvdaPath SIGNED_NVDA_EXE -ReportPath FRESH_JSON`
tests the separate live-issue route in the Windows VM. It selects three
test-owned source files with two existing target names, uses a rate-limited
middle file to separate conflicts, and requires NVDA to speak both new issue
counts and full paths without job reselection. The first selected issue must
remain selected after the second append. Source and target hashes for skipped
conflicts and the middle copied file must remain exact. The dated `2a371b4c`
receipt accepts this scope; do not rerun it because another audit case remains
open.
`win-ax-sync-stop.ps1 -AppPath GIT_BUILT_FILECAT_EXE -ReportPath FRESH_JSON`
uses the isolated Windows VM to review Mirror with one Copy child and one
target-only removal. The first small file must arrive exactly while a large
second file keeps Copy active. Stop must cancel both unfinished child steps,
retain the first committed effect and both exact child reports, leave the
target-only file unchanged, and admit no later target mutation. The dated
`2a371b4c` receipt accepts this bounded interruption route.
`linux-atspi-navigation-capacity.py APP REPORT.json KEY_HELPER` uses an
isolated Ubuntu Git build, the transient native input and SFTP credentials,
an owned delayed loopback SSH relay, and a real `/dev/shm`/home mount boundary.
It requires two newer navigation intents before the remote listing completes,
then drops the owned relay and requires the current panel/capacity and exact
sentinels to remain stable. The dated `34fee315` receipt accepts that scope;
current-failure screen-reader speech is a separately accepted check.
`linux-atspi-current-navigation-error.py APP REPORT.json KEY_HELPER`
reserves a non-listening loopback SFTP port in the isolated Ubuntu VM. It
requires transient input/SFTP credentials and no other FileCat or Orca process.
The native Orca and AT-SPI result must expose the specific connection refusal;
physical Alt+Left must restore the prior local folder, show its sentinel row,
and leave its bytes exact. The dated `34fee315` receipt accepts this route.
`linux-atspi-workspace-save-race.py APP REPORT.json KEY_HELPER` launches two
FileCat processes with a shared test-owned `--data` root and distinct local
folders. Both native Save as prompts hold the same fresh name before either
publishes. After the first save, the second must show a replacement review;
Cancel leaves the saved JSON, absent backup and both source files unchanged.
The dated `34fee315` receipt accepts this collision route only.
`linux-atspi-workspace-40-tabs.py APP REPORT.json KEY_HELPER` opens a
test-owned named workspace with four panels, ten distinct local tabs per panel,
and cyclic target links. It measures native display/window scale, checks each
active path and target announcement, then physically cycles through all ten
tabs per panel while reading AT-SPI path and focus. Its bounded tree limit is
larger than a file-list runner because 40 tab controls are present. The dated
`34fee315` 200% narrow-window receipt accepts this restore route.
`linux-atspi-horizontal-sort.py APP REPORT.json KEY_HELPER POINTER_HELPER`
uses an isolated Ubuntu profile with 20-point list text and three files whose
name and modification orders differ. It drives a physical Shift+wheel over the
clipped list, clicks the revealed Modified header, checks the actual reordered
rows and retained AT-SPI file focus, then records Orca's spoken file name and
modified value after Down/Up. Source bytes and empty target must stay exact.
The dated `34fee315` receipt accepts the 200% stricter-scale equivalent route.
`clipboard-cut <source.xml>` selects `a.txt`, sends Ctrl+X, and checks that
FileCat published its exact path as a Windows shell file drop without removing
the source. With an Explorer window
open, `explorer-navigate <source.xml>` visits that source folder;
`explorer-copy <source.xml>` selects its file and sends Ctrl+C.
`clipboard-native <report.json>` verifies that Explorer replaced the clipboard
with exactly that path in `CF_HDROP`. Finally, `clipboard-paste <report.json>`
focuses FileCat's destination pane, sends Ctrl+V, and checks the source and
destination contents byte for byte. It fails on a missing file, changed source
or incomplete copy. `clipboard-stop <report.json>` gracefully closes only the
validated isolated FileCat process when a new Git build must be deployed.
For an Explorer Cut publication, use a fresh fixture and the same isolated
build. Run `explorer-navigate <source.xml>`, `explorer-cut <source.xml>`,
`clipboard-cut-native <report.json>`, and
`clipboard-paste-explorer-cut <report.json>`. The native report requires the
exact `CF_HDROP` path and `Preferred DropEffect` value 2 (Move). Paste must
copy exact bytes while keeping Explorer's source because an external Cut
does not carry FileCat's own Move authority. Run `clipboard-ole-effect-native
<report.json>` after Paste: it reads Explorer's OLE data object without
changing it and requires `Performed DropEffect` value 1 (Copy), the same
source file and exact target bytes. The Win32 clipboard view can omit this
format even when the OLE data object stores it, so inspect the OLE report.
For the cancellation gate, seed `seed-clipboard-conflict clipboard-fixture.json`
on a fresh fixture, launch the isolated build, then run Explorer navigation,
Cut, `clipboard-paste-review <review.xml>`,
`clipboard-open-review <decision.xml>`, and
`clipboard-cancel-native <report.json>`. The latter requires both byte-exact
files, the same clipboard generation and no performed OLE effect. Finish
each route with `clipboard-stop`.
For the native FCUX-018 Move inverse, use another fresh `seed-clipboard`
fixture, then run `clipboard-cut <source.xml>` and `clipboard-move
<report.json>` in that order. The latter uses FileCat Paste and checks that
the source is gone, the target has the exact bytes and the unrelated file
remains. For visual QA, `undo-review-native <source.xml>` opens and leaves the
safe review pending so a VM screenshot can show the whole highlighted source
and target paths. Run `undo-native <report.json>` without restarting FileCat;
it accepts an already open review or opens one with Ctrl+Z. It checks the full
source and destination identities, measures the
multi-line inverse row before and after dragging the review wider, cancels and
verifies the move still exists, then reopens the review and approves it. It
requires byte-for-byte restoration at the original path, removal of the moved
target, and preservation of the unrelated file. It then opens the retained
Undo report from Operations and requires Copy all to publish the complete
restored outcome with both paths. Finish with `clipboard-stop`.
For an empty-folder inverse, use a fresh `seed-clipboard` fixture and launch
FileCat in the same way. Run `folder-undo-native <report.json>`: it focuses the
destination listing, creates a folder with F7, requires the complete path in a
wrapped Undo action, checks that Cancel leaves the folder in place, then
approves Undo and requires the folder to disappear while both source files
keep their exact bytes. Finish with `clipboard-stop`.
On another fresh fixture, `file-undo-native <report.json>` uses Shift+F4 to
create a zero-byte file through FileCat. It requires the complete path in the
wrapped inverse, verifies Cancel preserves the empty file, then approves Undo
and checks that only the created file is removed. Both original source files
must retain their exact contents. Rerun `folder-undo-native` after changing
this shared native path.
`file-undo-kept-native <report.json>` starts from another fresh fixture, changes
the created file after FileCat records it, and approves the inverse. The file
and exact changed bytes must remain. The retained Undo report must be available
through Operations → Copy all and state one kept action, the full path and the
changed-since-creation reason.
`hardlink-undo-native <report.json>` selects `a.txt`, uses FileCat's Create
link review to create a hard-link name in the destination, and requires the
preview to say hard link before Create is enabled. A write through the original
name must appear through the new name. The complete wrapped Undo row must name
both paths; Cancel preserves the link, and approval removes only the new name
while the original retains its bytes. This also catches a stale symbolic-link
preview after changing the radio selection.
`hardlink-undo-changed-native <report.json>` starts another fresh hard-link
fixture, replaces the original name with a different file after creating the
link, and approves Undo. Both names and their distinct bytes must remain. The
retained report's Copy all text must record one kept action, the complete link
path, and the original-name identity reason.
`symlink-undo-native <report.json>` uses the same fresh-file fixture to create
a real symbolic link through FileCat. It verifies the selected kind in the
preview, shared bytes, both complete paths in the wrapped inverse, safe Cancel,
and removal of only the link after approval. On another fresh fixture,
`symlink-undo-changed-native <report.json>` replaces the created link name with
a regular file before Undo. Both exact byte sequences must remain, and the
retained report must identify one kept action and explain that the recorded
link was replaced.
`junction-undo-native <report.json>` creates a new source folder on a fresh
fixture, refreshes FileCat, then creates a real local-drive junction through
its review. A target-file write must appear through the junction. The wrapped
inverse names both complete paths; Cancel preserves the junction and approval
removes only the junction. On another fresh fixture,
`junction-undo-changed-native <report.json>` replaces the junction name with a
real folder. Undo must keep both directories and their distinct bytes, with a
retained one-kept report naming the complete path and replacement reason.
On a separate fresh Cut/Move fixture, run `history-wrap-native <report.json>`
before Undo. It opens Clear finished history from Operations, measures the
long job row before and after widening the review, requires the Keep history
action to remain reachable, then cancels and verifies the moved bytes remain
at their destination. Finish with `clipboard-stop`.
For a separate test-owned 5,000-file Move, run
`win-ax-undo-active-close.ps1 -AppPath GIT_BUILT_FILECAT_EXE -ReportPath
FRESH_JSON -FileCount 5000 -Action ClearOriginalHistory`. It admits an Undo,
approves removal of the original completed row while the inverse is active,
and requires all 5,000 exact files restored with a retained inverse report.
The runner records that the review disclosed active Undo, the original row
disappeared and active work survived. Its other action modes retain their
separate earlier scopes.
`win-nvda-clear-history.ps1 -AppPath GIT_BUILT_FILECAT_EXE -NvdaPath
SIGNED_PORTABLE_NVDA_EXE -ReportPath FRESH_JSON` checks a separate 900 × 650
native Windows Move and unused Undo. It hears the Clear finished history
consequences and Keep history choice through NVDA, then requires Undo,
Open report and exact moved bytes to remain after Keep. It admits only a
signed NVDA and a Git app under the VM's `FileCatValidation` folder, and
closes only its owned processes. The `e7b448b7` receipt accepts this exact
route; active inverse lifetime is covered by the separate action above.
`linux-atspi-clear-history.py` is a separate, currently diagnostic Ubuntu
200% Orca route for the unused Undo consequence and Keep history retention.
It runs an owned Move in the logged-in VM desktop. Its 27 October
`4c042d37` probes stopped before a valid history review while the AT-SPI
tree refreshed; they do not establish product acceptance. A future run must
capture actual speech and verify Undo and file bytes after Keep history on
that distinct platform if later needed for interoperability. The Windows
equivalent spoken Keep route passed; the active-inverse boundary remains open.
For the Windows Recycle Bin inverse, use another fresh `seed-clipboard`
fixture and launch its isolated FileCat instance. Run `recycle-review
<source.xml>` to select `a.txt`, press F8 and require the explicit Recycle Bin
review while both files still exist. Run `recycle-native <report.json>` with
that review open: it approves the reviewed Recycle action, checks the isolated
file disappeared while `b.txt` and the destination stayed intact, checks the
full path in Ctrl+Z's Recycle inverse review, verifies Cancel keeps the file
recycled, then approves Undo and checks byte-for-byte restoration. It reopens
the retained Undo report from Operations and requires Copy all to include the
restored path and outcome. Finish with `clipboard-stop`.
Run `accessibility-list <report.json>` with this fixture and its FileCat
window open. The native Windows UI Automation tree must expose both named
panel lists, two distinct source file rows with positive screen rectangles
inside their list, and an empty destination state. The runner marks `a.txt`,
moves keyboard focus to `b.txt`, and checks that mark and focus remain
distinct. It can be repeated against the same fixture.
Run `breadcrumb-native <report.json>` on that fixture to send Alt+Up,
inspect the focused ancestor and its full-path accessible name, open its
submenu with Right, and require Open here, Open in new tab and Copy path with
the same target. Escape must return keyboard focus to the source list.
Run `panel-resize-native <report.json>` to use command search for keyboard
panel resizing, verify that the focused splitter exposes its orientation,
neighboring panels and Escape hint to UI Automation, move the boundary with
Right, then use Escape to restore its original screen position and list focus.
For FCUX-009, run `seed-compare <fixture.json>` to create two disposable files
with 2,000-character shared prefixes and different suffixes. Launch the
Git-built FileCat with the reported source, destination and data paths, then
run `compare-native <report.json>`. It marks both files, opens Compare, checks
full native source values, navigates to the second difference, verifies a
source-faithful Copy, and checks that wrapping keeps the selected changed row
on screen. `compare-stop <report.json>` gracefully closes only that isolated
FileCat process, including a comparison window that outlives the main window.
On the unlocked Mac desktop, run `mac-ax-compare-long-lines.py BUNDLE.app
REPORT.json` with a signed validation bundle and fresh report under
`~/FileCatValidation`. It creates two 2,000-character suffix differences
separated by an equal line, uses native Ctrl+I and F8, captures both changed
spans, verifies that Copy line returns each complete source line and navigation
does not replace the clipboard, then checks source hashes and restores the
prior clipboard. Add `--narrow` to repeat in a measured 640 × 600 comparison
window; the toolbar reflows while both changed suffixes remain visible.
Use `--narrow-wrap` to switch on Wrap long lines in that same 640 × 600
window. F8 must reveal the second suffix in both aligned source panes, and
Copy line must still return the complete source line.
On the logged-in Ubuntu validation VM, run
`linux-atspi-compare-long-lines.py APP REPORT.json` with the exact Git-built
app under `~/FileCatValidation` and one-time native keyboard sudo
authentication. The runner owns and removes two long-line files, uses
physical Ctrl+I and F8, verifies both complete copied lines, checks Wrap in
a narrow 640 × 600 native window, preserves source bytes and restores the
previous clipboard text.
For the uncovered enlarged-font pointer and spoken boundary, use
`linux-atspi-compare-pointer-font.py APP REPORT.json` in that same isolated
Ubuntu session. It uses a short-lived physical uinput mouse to select and
copy the changed suffix after four font increases, then starts a test-owned
Orca and requires Next difference to speak the second changed line. A fresh
report path under `~/FileCatValidation` and the one-time native sudo credential
are required. The test restores the clipboard and checks unchanged source
bytes; a recurring GNOME Remote Desktop request is explicitly dismissed
before input rather than being counted as a FileCat failure.
For FCUX-003, start from a fresh `seed-clipboard` fixture and launch the
Git-built app with the reported paths. `rename-native <report.json>` marks
both files, filters `a.txt` out, verifies the default one-file preview, and
renames only visible `b.txt` after reviewing the generated name. With a second
fresh fixture, `rename-hidden-native <report.json>` explicitly includes the
hidden mark and verifies both reviewed mappings and the resulting file bytes.
On a third fresh fixture, `rename-narrow-native <report.json>` checks the
default excluded-hidden scope in a measured 760 × 560 main window. The hidden
mark disclosure, preview, name mask and Rename action must remain accessible;
only the reviewed visible file may change, with its exact bytes preserved.
`rename-hidden-narrow-native <report.json>` repeats the explicit inclusion on
a fresh 760 × 560 fixture. It requires both reviewed mapping rows to be visible
before approval, verifies the resizable dialog's essential controls, and checks
both renamed files and exact contents. The less common counter, search and case
controls can be expanded when needed so the preview has room at this size. The
same check expands the owner to 1060 × 800, opens those options, drags the review
resize grip, and requires both mapping rows and the approval buttons to stay
visible as the preview grows. `rename-hidden-compact-native <report.json>`
starts a fresh fixture at a requested 640 × 420 owner size, checks that the
hidden-mark choice, name mask and Cancel/Rename actions remain on screen, then
expands to 760 × 560 to review and submit both exact mappings. It repeats the
expanded-options and resize-grip checks and verifies both renamed file bytes.
`rename-keyboard-native <report.json>` starts another fresh `seed-clipboard`
fixture, includes the hidden mark, sends Return to focused Next and Space to
focused Previous, and checks both complete path mappings before approving
the exact two-file rename. Its report records `keyboardMappings: true`.
For the actual external-editor round trip, run `seed-rename-editor <setup.json>`
beside a fresh `clipboard-fixture.json` before launching FileCat. This sets the
isolated profile's editor to the native acceptance executable; its
`rename-editor-write` mode accepts only that fixture's private `rename-*.txt`
list and the expected two proposed names. Then run `rename-editor-native
<report.json>` against the launched app. It reviews the visible and explicitly
included hidden mappings, opens the editor action, waits for the saved names,
chooses the nested Load names decision, verifies the updated preview and
temporary-file cleanup, then checks the exact resulting source bytes and empty
destination. The action is placed before the long preview so pointer users can
find it without first scrolling through the list.
Run `rename-stop <report.json>` before another
fixture. It closes only processes whose executable matches the guest-local
validation build; a fixture left behind by a stale modal is terminated and
reported so a new run cannot attach to it accidentally.
On a fresh fixture after `rename-native` (the default visible-only case), run
`rename-undo-native <report.json>` before `rename-stop`. It requires a wrapped
native Undo row naming both the reviewed new path and original path, verifies
Cancel preserves the renamed bytes, then approves Undo and checks the exact
original filename and bytes are restored without changing the other file.
For FCUX-002, run `seed-selection <fixture.json>` and launch the Git-built app
with its reported source, destination and data paths. Run the selection checks
and `selection-stop` with `FILECAT_NATIVE_TEST_APP` set to
that exact guest-local Git-built `FileCat.exe`. The runner requires the app to
reside under the guest's `AppData\Local\FileCatValidation` directory and
matches its process path, so another FileCat window cannot satisfy the check.
The bulk-rename `rename-native`, `rename-hidden-native` and narrow variants
also accept `FILECAT_NATIVE_TEST_APP` for the same path-verified Git build.
Their approval click is scoped to the modal containing the hidden-mark choice
and Name mask, so an obscured main-window Rename control cannot satisfy the
test. Run each against a fresh `seed-clipboard` fixture and close that exact
process with `selection-stop`.
Run
`selection-native <report.json>` in the interactive guest desktop: it clicks
`b.txt` as the range anchor, reverses the name sort, then Shift-clicks `c.txt`
with native input. Only `b.txt` and `c.txt` may be marked, and the fixture
files must remain unchanged. `selection-stop <report.json>` closes that
path-verified app after the check. On another fresh fixture,
`selection-narrow-native <report.json>` repeats the sort and Shift-click route
after resizing the isolated main window to 720 × 540 physical pixels. The
report requires the measured window size, exact two-file mark and untouched
source and destination. Finish with `selection-stop`. On another fresh fixture,
`selection-filter-native <report.json>` clicks `b.txt`, filters it out,
restores the full list and Shift-clicks `d.txt`. The retired anchor must not
return; only the fallback range `c.txt`–`d.txt` may be marked.

The Windows
native runner declares per-monitor DPI awareness before reading WinAppDriver's
physical row bounds or sending Win32 pointer input. Run fresh selection modes
at 100%, 150% and 200% in the isolated VM, record the selected Display
Settings scale, and restore the VM to 100% afterward. UIA source snapshots
retry a transient driver HTTP 500 for at most 800 ms; a persistent failure
still fails the native case.

Other FCUX-002
transitions still need their separate native checks. `selection-tab-native
<report.json>` uses a fresh fixture with `second/e.txt` and `second/f.txt`:
after clicking `d.txt` in the first tab it opens a second tab, enters that
folder, focuses `e.txt` with the keyboard and Shift-clicks `f.txt`. The new
tab must mark exactly `e.txt` and `f.txt` without inheriting the old tab's
row coordinate.
`selection-refresh-native <report.json>` uses another fresh fixture. It
clicks `d.txt` as the anchor, moves keyboard focus to `b.txt`, deletes `d.txt`
and creates `0.txt` in the owned folder, then rereads with Ctrl+R. Shift-click
on `c.txt` must mark only `b.txt` and `c.txt`, with exact fixture bytes and no
destination changes.
`selection-create-native <report.json>` keeps the clicked `c.txt` anchor in a
fresh fixture, creates `0.txt` ahead of it, then rereads with Ctrl+R. The
focused file must still be `c.txt`; Shift-click on `d.txt` must mark only
`c.txt` and `d.txt`. This catches a stale numeric anchor after a new row is
inserted. It also checks all source bytes and the empty destination. Finish
with `selection-stop` as for the other selection modes.
`selection-ctrl-native <report.json>` uses a fresh fixture to click `b.txt`,
Ctrl-click `d.txt` on, Ctrl-click `b.txt` on, and Ctrl-click `d.txt` off. A
native Shift-click on `c.txt` must then use the original `b.txt` anchor, mark
only `b.txt` and `c.txt`, and leave every fixture file unchanged. Run the
gesture and `selection-stop` in the guest's interactive desktop session so
native input and graceful window close target that session.
`selection-keyboard-native <report.json>` clicks `b.txt`, uses two Shift+Down
keys to mark `b.txt` and `c.txt`, returns focus to `b.txt` without Shift, then
uses two more Shift+Down keys to unmark both. It checks native focus and mark
states after each phase and verifies unchanged fixture bytes. Use another
fresh seed and interactive session for each run.
`selection-deactivation-native <report.json>` uses another fresh fixture. It
marks `b.txt`, holds Shift through Down and requires `b.txt` to become unmarked
while focus moves to `c.txt`. It verifies Shift is physically down, activates
that fixture's Explorer window, releases Shift there, then returns to FileCat.
The next Shift+Down must start a new mark gesture on `c.txt`, leaving all
fixture bytes and the destination unchanged. It then starts an unmark gesture
on `c.txt`, gives focus to the command line, releases Shift, and returns to
the list with Escape rather than a mouse click. A fresh Shift+Up must mark
only `d.txt`. Finish with `selection-stop`.
`selection-deactivation-narrow-native <report.json>` repeats that journey on a
new fixture after measuring a 720 × 540 main window. It asserts the same held
modifier, window and command-line focus transitions, exact marks and untouched
files at the narrow size. Finish with `selection-stop`.
The filter, tab, refresh, Ctrl-click and keyboard journeys also accept
`selection-filter-narrow-native`, `selection-tab-narrow-native`,
`selection-refresh-narrow-native`, `selection-ctrl-narrow-native`, and
`selection-keyboard-narrow-native`. Run each on a fresh seed, with
`FILECAT_NATIVE_TEST_APP` bound to the Git-built executable. Each variant
measures the 720 × 540 physical-pixel main window and requires the same exact
marks, focus and fixture-byte checks as its normal-width counterpart.
For FCUX-008, use a fresh `seed-selection` fixture and its launched isolated
app, then run `checksum-native <report.json>` in the interactive guest desktop.
On the unlocked Mac desktop, run `mac-ax-viewer-checksum.py BUNDLE.app REPORT.json`
with a signed validation bundle and a fresh report under `~/FileCatValidation`.
The script creates an isolated five-byte source, uses actual Hex selection and
Ctrl+K input, checks one-byte and whole-file SHA-256/CRC-32 results and byte
counts, and verifies that Compute leaves the clipboard unchanged while Copy
publishes each complete result. It preserves the original clipboard, verifies
all fixture bytes and the empty destination, and records native screenshots.
Compile `mac-send-key.swift` as `~/FileCatValidation/mac-send-key-checksum`
before running; Accessibility permission and the owner-approved capture helper
are required on this test Mac. The optional `--narrow` mode measures a
720 × 500 viewer and repeats both explicit Copy journeys. The separate
`--stale-scope` mode changes the source after a one-byte scope review, then
requires visible refusal, disabled Copy, an unchanged clipboard, an empty
destination and restoration of the original fixture byte.
The Windows runner opens `a.txt` in the viewer, selects exactly byte zero in Hex mode, reviews
the one-byte checksum scope, and verifies the complete known SHA-256, CRC-32,
byte count and source revision text. It also checks that Compute leaves the
clipboard unchanged and explicit Copy publishes the complete visible result.
The runner closes the viewer and verifies the fixture files are unchanged;
`selection-stop` then closes the isolated app.
On another fresh fixture, `checksum-narrow-native <report.json>` repeats the
one-byte result and explicit Copy after measuring a 600 × 420 logical-pixel
viewer window at its actual per-monitor DPI. The runner requests the matching
physical bounds and records both measured bounds and DPI.
The complete result and Copy control must remain visible and the source bytes
and destination must remain unchanged. Finish with `selection-stop`.
On a separate fresh fixture, `checksum-minimum-native <report.json>` requests
the viewer's 480 × 300 logical-pixel minimum. Windows can add a small
non-client frame to that requested size. The runner checks the resulting
bounded outer window, measures the checksum scope, Compute, Copy results,
Close checksums, and result field inside its frame, then verifies the exact
one-byte hashes, explicit Copy, untouched source and empty destination.
Finish with `selection-stop`.
On a new fixture, `checksum-whole-native <report.json>` begins with the same
one-byte selection, explicitly switches the scope to Whole file, and checks
the full 23-byte SHA-256, CRC-32, byte count, revision evidence, unchanged
clipboard until Copy, and unchanged fixture bytes. Finish with
`selection-stop`.
For the visible Stop route, create a fresh fixture and run
`checksum-stop-native <report.json>`. The runner adds an isolated 768 MiB file,
reviews its whole-file scope, starts Compute and presses the newly visible Stop
control. It requires a canceled result without a hash, disabled Copy, unchanged
clipboard, untouched fixture files and sampled source bytes. It removes the
large file after closing the viewer; finish with `selection-stop`.
On another fresh fixture, `checksum-changed-native <report.json>` captures a
one-byte scope, changes the source at the same length, then invokes Compute.
The viewer may notice the changed revision before or when Compute starts; in
either case it must refuse the stale scope visibly, publish no hash, disable
Copy and preserve the clipboard. The runner restores the original source bytes
and timestamp after closing the viewer; finish with `selection-stop`.
On a separate fresh fixture, `checksum-midread-native <report.json>` creates a
test-owned 128 MiB source and reviews its Whole file scope. After Compute has
consumed at least 250 ms of CPU in the exact isolated FileCat process, the
runner changes its first byte and revision while the read is underway. The
viewer must refuse the changed source visibly, publish neither SHA-256 nor
CRC-32, disable Copy and leave the clipboard and four small fixture files
untouched. The destination stays empty. The runner closes the viewer and
removes its large test file; finish with `selection-stop`.
For FCUX-007, use another fresh `seed-selection` fixture and run
`search-native <report.json>` on its launched isolated app. The interactive
runner creates a 13 MiB guest-local text file with `NEEDLE` near 1 MiB and
8 MiB, rereads the folder, opens Text view, and goes to the end. The visible
Previous button must reach the later match, then the earlier match across
more than 4 MiB, then report no earlier match. The report records both
visible offsets and checks the source markers and untouched destination.
The runner closes the viewer; finish with `selection-stop`.
For the visible long-search cancellation route, use a fresh `seed-selection`
fixture and run `search-stop-native <report.json>` on its isolated app. It
creates a test-owned 2 GiB local file, opens Hex view, enters an absent query,
then physically clicks Next and its adjacent Stop before requesting a slow
automation-tree snapshot. The runner requires `Search canceled.`, the Stop
button hidden afterward, an unchanged clipboard, all four small source files
and empty destination intact, and unchanged sampled bytes in the large file.
It closes the viewer and removes only that test-owned file; finish with
`selection-stop`. The companion blocked-read App test holds an actual content
read while checking that Stop is visibly laid out next to Next and that a
click cancels the search.
Run `search-stop-text-native <report.json>` on another fresh fixture to
switch the same large source to Text mode with F4 before starting the search.
It requires the Text mode label and the same native pointer cancellation,
clipboard and source/destination checks. Finish with `selection-stop`.
For native UTF-16LE Previous search, use a separate fresh `seed-selection`
fixture and run `search-utf16-native <report.json>`. It builds the same 13 MiB
two-hit file with a UTF-16LE BOM and encoded markers, then checks both distant
backward matches, the complete no-earlier-match status and unchanged bytes.
Finish with `selection-stop`.
For FCUX-001 cross-volume recovery, run `VM/provision-cross-volume.ps1`
inside an elevated Windows guest with `-Mode Create -ReportPath` pointing to
the guest-local `FileCatValidation/Runner/cross-volume.json`. It refuses an
existing R: drive or report, creates one 512 MiB dynamic NTFS VHD under the
guest-local validation folder, and gives the current guest user Modify access
only under `R:\FileCatValidation`. Run `seed-cross-volume <cross-fixture.json>`
in the guest and launch the isolated Git-built FileCat with the fixture's
`--data`, `--left` (C:) and `--right` (R:) paths. In the interactive desktop,
run `cross-prepare <report.json>` followed by `cross-interrupt <report.json>`.
The latter sets a 64 KiB/s rate, waits for copied bytes in journaled staging,
then terminates only the path-verified isolated app. Relaunch the same fixture
and run `cross-continue <report.json>` to check the review, retained rate and
mapping, final byte equality, excluded file and staging cleanup. Close the
isolated app with `selection-stop`, then run the provisioning script with
`-Mode Remove` and the original volume report path. Removal validates the
report and detaches/deletes only its own VHD. Verify R: and the VHD are absent.
Use fresh fixture folders for each run. The validation report and VHD live
only in the guest; do not use a host share as the transfer destination.

For a disposable direct deleted-file scan on Windows, run
`win-vhd-recovery-fixture.ps1 -ReportPath` with a new JSON path directly under
the guest's `AppData\Local\FileCatValidation`. It requires an elevated VMware
guest and refuses an existing `G:`. The report identifies the exact mounted
VHD, disk number and source SHA-256. Immediately set
`FILECAT_RECOVERY_VM_VHD`, `FILECAT_RECOVERY_VM_DISK` and
`FILECAT_RECOVERY_VM_SHA256` from that report, then run the opt-in
`VmVhdRecoveryTests` test in the guest. The test reads the selected volume GUID
and drive letter, and writes its recovered output under the guest's `C:`
validation folder. A mounted volume can reuse a deleted record quickly, so a
later missing record is not evidence that the initial scan failed. Dynamic
VMware VHDs have returned zeroed raw clusters after deletion; the test checks
that such content is marked Uncertain and does not claim original-byte
recovery. For an isolated retained-cluster check, add
`-RetainDeletedClusters` to the fixture command. The script verifies the
current NTFS delete-notify value, temporarily disables TRIM only in the VMware
guest, creates a fixed VHD, flushes the source bytes to the volume before
deleting that file, then restores and verifies the original TRIM value before
reporting success. Set
`FILECAT_RECOVERY_VM_EXPECT_RETAINED=1` for the opt-in test: a hash mismatch
then fails instead of accepting Uncertain evidence. Check the fixture report's
`NtfsDeleteNotifyRestored` field before running the test. Close the exact test
FileCat process, then run
`win-vhd-recovery-cleanup.ps1 -ManifestPath` with the original JSON path. The
cleanup verifies the exact mounted disk and removes only its own VHD folder.
For the subset case, use a fresh seed and run `clipboard-cut-two <source.xml>`
to mark and Cut both files. Then run the same Explorer navigation, Copy and
native-manifest checks for `a.txt`, followed by
`clipboard-paste-subset <report.json>`. The latter also requires `b.txt` to
remain at the source and be absent from the destination.
These modes use a fresh guest-local fixture; they never touch the host
clipboard or a user folder.

The `snapshot`, `find`, `advanced`, `tools-menu`, `settings`, `settings-tools`,
`editor-browse`, `editor-value`, and `editor-draft-cancel` modes are diagnostic
or setup actions. They do not count as passing acceptance checks. Review the
VM screenshot and JSON result together when recording native acceptance.

`win-nvda-speech.ps1 -AppPath <FileCat.exe> -ReportPath <report.json> -NvdaPath <nvda.exe> [-RecordAll]`
hears an isolated Git-built FileCat through NVDA in the interactive VM session.
Create the portable copy once from the signed NV Access installer with
`nvda_<version>.exe --create-portable-silent --portable-path=<FileCatValidation>\NVDA\portable`;
the runner refuses an unsigned copy or a second running NVDA. It starts NVDA
with a test-owned configuration (no welcome, update or usage questions) and a
debug log, and only that log's `Speaking [...]` lines decide each step. A
test-owned window stands beside FileCat: Windows' foreground lock can keep a
program launched by this background runner from becoming foreground in NVDA's
view, so the first step is a genuine title-bar switch from that window, made
only after NVDA has announced it. Keys are virtual-key input with the
extended-key flag (`SendKeys` arrow keys toggled NumLock), sent only after UI
Automation confirms keyboard focus is in the tested process. The journey moves
Down three times, Up, Home and End, tabs to the target panel and back, opens
Create folder with F7, closes it with Escape, copies the focused file with
Ctrl+C twice, opens Quick filter with Ctrl+S, types the unclosed mask `/a`,
presses Enter and closes it with Escape, opens the viewer with F3 and its
search with F7, types `char` and Enter, leaves with Escape twice, opens Copy
with F5, empties its destination, cancels it with Escape, and switches away and
back; every step
must name the expected file, parent row or field, each copy must be heard once
as "Copied 1 item to the clipboard." (the first notice after startup, then a
repeated one), the mask's message must be heard as `/` is typed and again
when Enter is refused, the viewer search must speak "1 match" alone, without
its searching and counting states, the empty destination must be heard as
"Destination: Enter a destination folder.", and the fixture bytes must be
unchanged. `-RecordAll` keeps a complete transcript after an
unmet expectation, which documents a pre-fix build.

`win-nvda-hex-search.ps1 -AppPath <FileCat.exe> -RunnerPath <FileCat.NativeAcceptance.exe> -NvdaPath <nvda.exe> -ReportPath <fresh-report.json>`
adds actual NVDA speech acceptance to `hex-editor-search-native`. Run it in
the interactive VM session at a tested display scale, with the signed portable
NVDA copy and both Git-built executables below `FileCatValidation`. It starts
one owned NVDA instance with a private log, seeds an isolated 13 MiB source,
and runs the native pointer, distant-match, Previous, changed-query and
source-integrity checks. The report passes only if the native result passes
and NVDA's `Speaking [...]` records contain the match counts in order:
`1 distinct match visited`, `2 distinct matches visited`, then
`1 distinct match visited` for the changed query. The script closes its
isolated FileCat and NVDA processes and retains the private speech log and
native report for diagnosis.

## Linux logged-in desktop

`linux-orca-speech.py APP_PATH REPORT.json KEY_HELPER [--record-all]` hears an
isolated Git-built FileCat through Orca in the logged-in GNOME session. It
refuses to run beside another Orca, starts the installed Orca with only its
debug file line-buffered (Orca block-buffers it otherwise) and judges only the
`SPEECH OUTPUT` lines Orca sent to speech. The runner never reads FileCat's
AT-SPI tree, because enumerating it would create the nodes whose focus
announcements are under test. Physical uinput keys perform the same journey as
the NVDA runner, including both copy notices and the rejected Quick filter
mask, which FileCat sends as AT-SPI announcements, and end with a switch to a test-owned X11 GTK window and back;
the report records how often the focused file was named on return. It also
checks that the marked b.txt state is spoken after the two-source rename
review closes and records repeated row descriptions for diagnosis. Orca uses
the logged-in Wayland backend even when the SSH caller supplies XWayland's
`DISPLAY`; otherwise GTK can exit on `X_QueryPointer` before speaking.
Supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to the runner process, as for the other
uinput checks. `--record-all` keeps a complete transcript after an unmet
expectation.

`linux-atspi-large-listing.py APP_PATH REPORT.json KEY_HELPER` launches an
isolated Git-built FileCat with 2,000 generated zero-byte files and two known
source files. It waits for the complete file count, measures bounded native
AT-SPI enumeration and visible row peers, then sends physical Home/Down/End
keys to the first and last generated files. The JSON receipt records counts,
query and jump times, exact focus and the app DLL hash. It checks the original
source bytes and empty target, stops only its owned app and removes its entire
owned fixture. Use the logged-in GNOME session and transient
`FILECAT_NATIVE_SUDO_PASSWORD` as for the Orca runner.

`win-ax-large-listing.ps1 -AppPath FILECAT.exe -ReportPath REPORT.json` runs in
the logged-in Windows guest against a Git checkout under its isolated
`FileCatValidation` directory. It creates the same 2,000 zero-byte files,
measures native UI Automation rows and enumeration, focuses the first generated
row, sends End to the last, then verifies the original bytes and empty target.
The receipt records the Git head, app DLL hash and bounds; the runner stops
its exact process and removes only its checked fixture path. It permits the
specific validation checkout as Git's safe directory for one read when VMware
Tools uses a different Admin token, without changing global Git settings.

`linux-atspi-ftp-large-listing.py APP REPORT.json KEY_HELPER FTP_HELPER
ftp|ftpes|sftp` extends the 2,000-file AT-SPI bound to a fresh owned remote
listing in the logged-in Ubuntu desktop. It starts a disposable loopback FTP
or explicit-TLS FTPS server, or uses the local SSH server for SFTP; each route
shows its actual connection and credential dialog. Plain FTP requires explicit
unencrypted consent, FTPS one-time certificate consent, and SFTP matches the
shown host key to independently read SSH server public keys before one-time
consent. Supply transient `FILECAT_NATIVE_SUDO_PASSWORD` and, for SFTP only,
`FILECAT_NATIVE_SFTP_PASSWORD` to the runner. It removes both from the app
environment, verifies complete remote counts, bounded peers and physical End
navigation, preserves fixture bytes, and stops its owned app/server and fixture.

`linux-atspi-modal-guard.py APP_PATH REPORT.json` runs against a Git-built,
isolated Linux `FileCat` binary below the logged-in user's
`~/FileCatValidation` directory. It creates a fresh source, destination and
data profile, opens Copy for one file, then invokes obscured toolbar and
key-bar Copy/Delete actions through AT-SPI. It requires one unchanged Copy
review, no target, and the original source hash after Cancel. The JSON report
records the app hash, fixture and four invocation outcomes. Run it with the
user's AT-SPI session bus; it needs system Python with `gi.repository.Atspi`.
If `~/FileCatValidation/dotnet-sdk` exists, the runner uses it for a
framework-dependent Git build unless `DOTNET_ROOT` was explicitly set.

`linux-atspi-copy-review.py SOURCE DESTINATION REPORT.json` tests a Git-built
FileCat running as the logged-in user. Use an isolated source folder containing
`a.txt` with `alpha\n` and `b.txt` with `beta\n`, plus an empty destination.
Set `DBUS_SESSION_BUS_ADDRESS` to that user's session bus and run with the
system Python that provides `gi.repository.Atspi` 2.0. The test focuses the
exact source row through accessibility, verifies the Copy review's complete
destination folder, cancels without changing either tree, then approves one
Copy. Its JSON result records the reviewed identities and exact target bytes;
an unselected file must stay at the source. Run it with a fresh data root and
fixture so another FileCat window cannot satisfy its checks.

`linux-atspi-clipboard-owned-move.py SOURCE DESTINATION UINPUT_HELPER
REPORT.json` checks the native owned Cut/Paste path. Use source files
`a.txt` = `alpha\n`, `b.txt` = `beta\n` and a destination containing only
`a.txt` = `alpha\n`. The runner focuses only `b.txt`, invokes Cut and Paste
through FileCat command search, requires the Cut notification, exact Move
bytes, source removal and unchanged `a.txt` files, then restores the two
fixture folders. `linux-uinput-key.py` sends its Enter events through a
short-lived Linux virtual keyboard, so the guest needs `/dev/uinput` and
`sudo` access. Give the runner a narrow passwordless `sudo` rule for this
helper, or set `FILECAT_NATIVE_SUDO_PASSWORD` only in the guest process
environment; the helper stores no credential. The test-owned Cut remains on
the guest clipboard after the run. Keep unrelated desktop windows closed so
they cannot intercept the native key.

`linux-atspi-clipboard-external-copy.py` uses the same fixture and helper,
with `linux-atspi-clipboard-owned-move.py` beside it. It Cuts only `b.txt`
in FileCat, opens that folder in Ubuntu Files, selects only `b.txt` with
physical Home/Right keys, Copies it, switches back to FileCat, and Pastes
into the destination. The test requires Copy evidence, exact target bytes,
source survival and unchanged `a.txt` files; it removes the copied target
and closes only its Files window. This covers the same-path publication
replacement that could otherwise revive an obsolete Cut. Run it from the
logged-in Wayland desktop with no other foreground dialog.
Add `--files-cut` after the report path to make Ubuntu Files Cut the same
`b.txt` path. FileCat must still import that external publication as Copy,
retain the source and match the target bytes. Both modes have been run twice
with exact fixture restoration.
Add `--filecat-two` to first mark and Cut both `a.txt` and `b.txt` in
FileCat, then replace that publication with Ubuntu Files Copy of only
`b.txt`. Combining both options uses Ubuntu Files Cut instead. Both subset
modes must copy only `b.txt`, preserve both source files and the existing
target `a.txt`, and clear the test marks during teardown. They passed twice
from the restored fixture.

`linux-atspi-clipboard-partial-move.py` uses the same exact two-file source,
existing `a.txt` destination and guest-only uinput helper. From the logged-in
desktop it selects and Cuts both files, Pastes to the destination, opens the
conflict review through AT-SPI and chooses Skip. It checks that `a.txt`
remains untouched on both sides, only `b.txt` moves with exact bytes, and a
second Paste explains the 1-of-2 partial result without replaying it. On
success it restores both fixture folders and clears Commander marks. Run it
with `DISPLAY`, the desktop AT-SPI bus and the same limited sudo setup as
the owned-Move runner. Pass `--cancel-conflict` after the report path to
choose Cancel operation instead: the fixture must remain byte exact, the
second Paste must explain zero of two completed roots, and the marks must be
cleared. The Skip route passed twice before this extension and again after
it; the Cancel route passed on the same isolated fixture.

`linux-atspi-clipboard-interrupted-move.py` uses the same exact guest fixture
and helper. Run `prepare` on the dedicated FileCatValidation instance: it
selects and Cuts both source files, Pastes into the existing `a.txt` conflict,
verifies the review action and unchanged bytes, then sends SIGKILL only to the
process whose executable and full arguments match the isolated app/profile.
Relaunch that same FileCat binary with its original `--data`, `--left` and
`--right` arguments. Run `inspect` to require the interrupted-operation notice,
exact destination, explicit Continue action and unchanged source/target bytes.
Run `review` to open that action and check retained source count, zero finished
steps, destination and Cancel/Continue controls; `cancel-review` closes it
without approving any transfer and checks the bytes again. Each phase writes
its own JSON result. The held-conflict interruption, restart, review and
Cancel phases passed on the logged-in Ubuntu VM. After Cancel, run
`continue-plan` to reopen the review and approve its recorded conservative
Skip policy. Run `inspect-continuation` to require exact final trees:
pre-existing `a.txt` remains on both sides, `b.txt` moves byte-exactly,
the UI says one done/one skipped, and recovery tracking is closed. These
continuation phases passed on the same VM.

`linux-atspi-active-copy-interruption.py PHASE FIXTURE.json REPORT.json`
checks a separate in-flight data Copy on the same logged-in Ubuntu VM. Use
the dedicated `/home/benny/FileCatValidation/active-copy-fixture.json`
manifest. `seed` creates a fresh 20 MB source and empty destination only
after the previous fixture has completed and its app has stopped. Launch the
FileCatValidation app with that manifest's exact `--data`, `--left` and
`--right` paths. `open-form` sends F5 via the physical-key helper (supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to the guest process), and `open-options`
exposes the speed field. `interrupt` sets 0.0625 MB/s, approves Copy, requires
a visible rate-limited operation, an unpublished target, a durable journal,
and a test-owned staging file whose written prefix matches the source. The
local provider may preallocate the full file: its unwritten zero tail is
checked separately. It then kills only the exact validation process.
`inspect-interruption` checks the staging and journal without a running app.
Restart the same exact profile; `inspect-recovery` checks retained intent,
destination and explicit Continue action. `review` opens the mapping;
`cancel-review` proves it can close without changing bytes or deleting the
journal. `continue-review` reopens and approves the reviewed Copy. After its
rate-limited transfer finishes, `inspect-completion` requires exact source
and destination hashes, no staging file, a Completed status and closed recovery
tracking. Each phase writes a JSON receipt; the test-owned fixture stays
under FileCatValidation and no archive handoff is involved.
After a completed fixture is stopped, `seed`, `open-form`, and `start-fast`
run a fresh unrestricted Copy on the current portable Git build on Ubuntu.
`inspect-finished-details` opens the Operations drawer if needed and requires
the visible `Average transfer speed:` label alongside exact source and target
bytes. This additional route passed with `Average transfer speed: 187 MB/s`.

`linux-atspi-viewer-find.py SOURCE REPORT.json` uses the same isolated
two-file source and the logged-in desktop's AT-SPI bus. It focuses `a.txt`,
opens Text view, exposes Find, enters `alpha` through AT-SPI editable text,
and checks Next's visible `1 match` and Previous's no-earlier-match feedback.
The test identifies the viewer by its exact X11 title and owning FileCat PID,
closes only that window through `wmctrl`, and checks that both fixture files
retain their bytes. Run it with `DISPLAY` and `DBUS_SESSION_BUS_ADDRESS` set
for the desktop user. `wmctrl` is required for graceful test cleanup.
Pass `--launch APP REPORT.json linux-uinput-key.py` to let the runner create
its own two-file fixture, launch an isolated Git-built FileCat under
`~/FileCatValidation`, send physical F3 and Shift+F3 through the uinput
helper, verify both directional no-match messages and the retained exact
count, then close its viewer and app. Grant the helper native input access as
for the Hex editor check. The JSON report records the executable hash and
fixture path.
Add `--narrow` to measure Find, Previous, Next and the count inside a
720 × 500 logical-pixel viewer. Add `--minimum` for the same source,
changed-query and physical-key journey in a 480 × 300 logical-pixel viewer.
Both modes derive physical bounds from the active GNOME display scale and
require the requested frame and all four controls inside it.

`linux-atspi-viewer-stop.py APP REPORT.json [--text] [--orca]` launches an isolated
Git-built app under `~/FileCatValidation`, creates its own 2 GiB sparse local
source and empty destination, opens Hex (or Text with `--text`), starts an
absent-query search and activates the visible Stop button through AT-SPI.
It requires the canceled status, retired Stop control, unchanged clipboard,
unchanged sampled source bytes and empty destination. The JSON report records
the executable SHA-256 and measured GNOME display scale. Text mode activates
the native Text toggle through AT-SPI; the separate 200% run also passed with
physical F4 from `linux-uinput-key.py`. The runner terminates only
its own app, closes its viewer, restores the clipboard and removes its own
large sparse source in cleanup.
`--orca` starts one test-owned Orca instance with an isolated speech log and
requires its actual speech output to include `Search canceled.` after Stop.
It refuses to run if another Orca instance is active and passes no transient
test credential to Orca or FileCat.

`linux-atspi-viewer-checksum.py APP REPORT.json UINPUT_HELPER` launches a
Git-built app on an isolated five-byte binary, selects byte zero with a
physical Shift+Left, then checks selected-byte and whole-file hashes. It
requires the complete result in the checksum entry's AT-SPI description,
SHA-256/CRC-32 and byte counts for each scope, unchanged clipboard until
Copy results, exact clipboard text after each Copy, and unchanged source and
destination. It uses the same guest-only uinput access and requires `xclip`
to independently read and restore the XWayland clipboard. Set `DISPLAY`,
`XAUTHORITY`, `XDG_RUNTIME_DIR` and `DBUS_SESSION_BUS_ADDRESS` to the logged-in
desktop. The runner terminates only its own FileCat process.
Add `--stale-scope` to change the source after opening the selected-byte
scope. The runner requires a visible and accessible refusal, disabled Copy,
unchanged clipboard and target, and exact fixture restoration.
Add `--narrow` in the Ubuntu VM at GNOME 200% to request a 1440 × 1000 physical
viewer frame (720 × 500 app units), then verify scope, Compute, Copy results,
Close checksums and the result entry remain within the frame before and after
both computations. The report retains the measured AT-SPI rectangles.

`linux-atspi-viewer-remote-stop.py APP REPORT.json UINPUT_HELPER` uses a
test-owned sparse file over loopback OpenSSH SFTP to verify held search query
and mode retirement and visible Stop. `--orca-error` separately requires
actual Orca speech of Stop and a deliberate test-fixture provider error;
`--checksum-revision` changes a same-length 256 MiB source after visible hash
progress and requires no digest or enabled Copy. All three routes verify the
host key and write their reports under `~/FileCatValidation`.
`linux-atspi-checksum-weak-revision.py APP REPORT.json UINPUT_HELPER` opens a
test-owned ZIP member (a content provider, not a code delivery archive),
restores the viewer to 480 × 300, and checks the visible length-only revision
qualification, complete result, in-frame controls and unchanged archive.

`linux-atspi-markdown-find.py SOURCE REPORT.json` uses the same logged-in
desktop and the helper script above. It creates one short, exact Markdown
fixture in the source folder, refreshes the panel, opens Page view and enters
`marker` through the accessible Find field. Two Next actions and Previous
must find rendered text while WebKitGTK reports exactly `2 matches`. The
runner verifies unchanged bytes, closes only the owned viewer window,
removes its exact test file and refreshes the panel. Both scripts must be
present together; this check also needs `DISPLAY` and the desktop AT-SPI bus.
Pass an optional `linux-uinput-key.py` path after the report to clear and
restore the query, then require physical F3 and Shift+F3 each to produce a
fresh exact `2 matches` Page result. The same owned viewer then switches to
Info and Hex; each physical shortcut must create a fresh visible match count
for that mode. `--launch APP REPORT.json UINPUT_HELPER`
creates its own exact two-file source, data profile and target under
`~/FileCatValidation`, starts the isolated Git-built app and closes only that
owned process afterward. The guest's native-input helper needs its established
sudo access; supply `FILECAT_NATIVE_SUDO_PASSWORD` only to the guest process.

`linux-atspi-hex-editor-find.py seed ROOT` creates a new 7 MiB file below
`~/FileCatValidation/HexEditorFind` with two matches more than 4 MiB apart.
Launch a separate Git-built FileCat with `ROOT/source`, `ROOT/destination`
and `ROOT/data`, then run `linux-atspi-hex-editor-find.py check ROOT REPORT.json
linux-uinput-key.py` in the logged-in session. Give the test the same native
uinput access described for `linux-atspi-clipboard-owned-move.py`. It opens
the editor through FileCat's accessible command search and a physical Enter
event because the current Wayland File menu exposes no AT-SPI action. The
check uses the exact source, process ID and editor title, exercises visible
Next/Previous, physical F3 and Shift+F3, match status and changed-query reset,
and closes the test-owned editor. It also accepts an already open test-owned
editor. `--scaled` requires the GNOME display at 200% and records the editor,
Find query, Previous/Next and count bounds; every control must remain inside
the editor window. `--narrow` adds a measured 720 × 500 app-unit window at
that display scale and repeats the same visibility and search checks.

`linux-orca-hex-search.py APP_PATH REPORT.json HEX_RUNNER KEY_HELPER ORCA_RUNNER`
adds actual Orca speech acceptance to the 200% native Hex route. All five
paths must be in `~/FileCatValidation`; pass the one-time uinput credential
through `FILECAT_NATIVE_SUDO_PASSWORD` only to this process. The wrapper
locates the logged-in user's Xwayland authorization and Wayland socket, starts
one owned Orca instance with a private speech log, launches an isolated
Git-built FileCat and runs `linux-atspi-hex-editor-find.py check --scaled`.
It passes only when the native offsets, controls, source bytes and cleanup
pass and Orca's `SPEECH OUTPUT` records contain one, two, then one distinct
match for the changed query. The JSON summary retains paths to the private
Orca log and native report, and the wrapper closes only its owned processes.

`linux-atspi-hex-resize.py APP REPORT.json` checks native Hex row reflow,
search selection, zoom and viewport anchor in both viewer and editor. It also
uses `linux-uinput-pointer.py` for a physical thumb drag down and back,
asserts no implicit byte selection, selects exact bytes 0–3 through a drag
started in content, and checks another thumb drag preserves them. Supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to this runner; it removes the value from
the environment before launching its exact Git-built FileCat process. The
uinput mouse is destroyed at the end of each viewer/editor check. At scaled
Wayland sizes the test drags in the narrow state, away from the physical
display edge, and uses bounded feedback from the Xwayland cursor position to
align the physical pointer despite compositor acceleration. The wide state
still verifies row reflow. Up to four reversible font-size steps may be needed
before a byte-group row shrinks; the runner checks that the exact Find
selection survives and that the original group count returns. The complete
viewer/editor journey passed in the Ubuntu VMware guest at 100%, temporary
150% and temporary 200%, with the original display restored afterward.

## macOS logged-in desktop

`python3 mac-ax-focus-speech.py BUNDLE.app REPORT.json` follows the macOS
accessibility focus VoiceOver speaks from. Keep `mac-ax-focus-observer.swift`
beside it; the runner compiles that read-only `AXObserver` with the matching
Xcode SDK, retries its registration until the starting app's accessibility
answers, and records every `AXFocusedUIElementChanged` with the focused
element's role, title, description, value and help. After each System Events
key it also reads the application's `AXFocusedUIElement`. It checks the
three-file Create/Delete/Rename Escape path. Pass `--decision-returns` for
a short, repeated check of those decisions and the settled original row.
The full journey matches the NVDA and Orca runners (Finder takes the
foreground for the switch step):
each move must post a notification for the expected `AXRow`, the dialog field
must be the focused `AXTextField`, each Command+C must post exactly one
`AXAnnouncementRequested` reading "Copied 1 item to the clipboard.", typing
`/` with a raw key code as the first character after Command+S and then
Return in Quick filter must each post exactly one reading "Unclosed regular
expression in mask.", the viewer search must post exactly one announcement
equal to "1 match", the emptied Copy destination must post exactly one
announcement of its error, and fixture bytes must stay exact. A TERM that an open
dialog leaves unanswered is followed by a KILL of the runner's own process. System Events sets Command without pressing a
Command key; Avalonia reports such a change as the A key, which FileCat now
ignores (before, Command+C first selected every file). The observer's `find PID
TEXT` mode is a diagnostic that searches the AX tree and so creates elements;
never use it inside a journey. Reading
VoiceOver's own speech can be read through its AppleScript `content of last
phrase` property after the owner-approved VoiceOver Utility and SSH helper
permissions. Keep Mac output muted during spoken acceptance. The narrow Undo
runner asserts VoiceOver's complete review and retained-report path phrases.
`python3 mac-voiceover-list.py BUNDLE.app REPORT.json` checks actual VoiceOver
speech of four file-row moves without a long-lived AX observer, which can
block VoiceOver's AppleScript readout. Keep `mac-ax-focus-observer.swift` beside
it: one-shot focused-element probes verify each row and that ordinary movement
does not mark it. Space then deliberately marks and clears one row. The runner
requires an isolated signed bundle and exact unchanged fixture bytes.
Add `--actions` to open the focused `b.txt` with F3 and close its viewer with
Escape. The extension requires VoiceOver to speak the opened viewer and the
restored unmarked `b.txt` row, then checks exact fixture bytes again.

`python3 mac-visual-dialog-pointer-dismiss.py BUNDLE.app REPORT.json` runs
against a newly signed Git-built FileCat bundle under `~/FileCatValidation` on
the 2880 × 1800 Retina Mac. It launches an isolated empty source/target/data
fixture, fixes the app window to 1280 × 800 points, sends F7, then uses
`cliclick` CGEvent mouse clicks on the header X and backdrop. Four approved
`FileCatCapture.app` screenshots are checked for the blue dialog accent's
open/closed sequence and for the vector X's bright-pixel center within three
physical pixels of the button center. The report records screenshot paths,
sample colors, centroid, window bounds and unchanged fixture state; only its
own FileCat process and fixture are removed. This fixed-geometry visual route
does not establish the control's accessibility label or other display scales.
`mac-ax-dialog-pointer-dismiss.py` is the complementary native AX route. Copy
`mac-ax-dialog-label.swift` beside it; the runner compiles that read-only
AXUIElement probe in its isolated fixture, verifies the exact app PID, and
requires one enabled `AXButton` named `Close dialog` with cancel help and a
usable pointer target. It then clicks the X and backdrop with native CGEvent
mouse input, requires both dialogs to close, and checks unchanged source and
target. Pass `--copy`, `--move` or `--delete` after the report to test those
reviews with an owned `b.txt` source; native AX must confirm the corresponding
approval control (and Destination for Copy/Move), while cancellation leaves
the source byte exact and the target empty. Pass `--sigterm` after the report
for a separate Create folder run that types and
verifies an unsubmitted folder name, sends SIGTERM to the exact owned app PID
while the dialog remains open, requires a bounded exit, and checks that no
folder was created. To exercise the supported 1× Mac display mode, run this
script as the child of `mac-display-run.swift 1920 1200 1920 1200`; the wrapper
restores and verifies the original display mode after the child exits. The
review modes also accept `--narrow` after the mode to resize the main FileCat
window to 640 × 480 points and require the enabled approval button to remain
inside it. The runner identifies the main window by its FileCat title and
minimum dimensions because macOS can list a transient tooltip before it. It
records the window and outside-click coordinates so a failed native route can
be distinguished from a click delivered to another app. The
runner always stops its own process and captures a
diagnostic screenshot on failure. System Events' `entire
contents` enumeration can return zero for a visibly populated Avalonia window;
the native AXUIElement children expose the control tree directly. This route
checks the accessible label and pointer behavior; actual VoiceOver speech and
other display scales need separate acceptance.
`mac-voiceover-list.py BUNDLE.app REPORT.json --copy-action` checks actual
muted VoiceOver speech when F5 opens a Copy review for the focused file.
`--move-action` and `--delete-action` exercise the corresponding F6 and F8
reviews. Each route requires its spoken title and enabled decision; Copy and
Move also require the native AX Destination field, while Delete must not have
one. It then cancels and checks restored unmarked row focus and unchanged
bytes. The report separately records whether VoiceOver speaks that restored
row automatically. The dialog probe uses the same selected Xcode SDK as the
focus observer, avoiding a Command Line Tools/compiler version mismatch.

`python3 mac-ax-finished-speed.py SOURCE DESTINATION REPORT.json` runs a
one-file Copy in the signed validation app, then requires an explicitly
labelled numeric average in the selected finished Operations details. It
narrows the main window to 760 × 600 and checks the same accessible rate,
restores the original size and exact two-folder fixture, and removes only the
test-produced destination `b.txt`. Start with `a.txt` = `alpha\n` and `b.txt`
= `beta\n` in SOURCE, only `a.txt` = `alpha\n` in DESTINATION, and no marks.

`python3 mac-ax-hex-editor-backward.py SOURCE REPORT.json [--narrow]` opens a test-owned
7 MiB binary in the signed validation app and uses the accessible Hex editor
Next and Previous buttons to cross two matches more than 4 MiB apart. It
checks the visible, bounded distinct-visited match count, F3 and Shift+F3,
the no-earlier message and count reset after changing the query. The runner
requires an unlocked desktop, verifies that the editor has no unsaved changes
and leaves the source bytes intact, and removes only its own fixture.
`--narrow` requests a 720 × 500 logical-point editor. If macOS initially
clamps the AX size, the runner drags the owned window's lower-right resize edge
with native CGEvent input to reach that size. It requires the actual final
frame to be no larger than 724 × 504 and AX bounds for Find, Next, Previous
and the truthful match status wholly inside it before the search journey.
`--narrow-ctrl` repeats that geometry and search journey but opens Find through
Ctrl+F and requires the native focused AX element to be the query field. Build
`mac-send-key.swift` with `swiftc mac-send-key.swift -o mac-send-key-find` beside
the runner: after search navigation, it compares System Events Ctrl+F with a
frontmost-app-guarded CGEvent key chord if the synthetic event misses focus.
`--narrow-voiceover` repeats the visible-control journey while Mac output is
muted and records VoiceOver's actual phrases for the first, second and
changed-query match counts. It allows the first long byte-row phrase to finish
before requesting the second polite update and fails if any count was not spoken.
Use `--pointer-probe` on a separate fresh fixture to request the same native
size, then drag the exact editor's lower-right edge toward a smaller size
with macOS CGEvent input. It records before/after AX frames, a screenshot and intact bytes,
then closes the editor without running a search. This distinguishes an AX
resize clamp from the physical window resize behavior.

`mac-capture.swift` and `mac-capture-Info.plist` build a dedicated, locally
signed screenshot helper for visual acceptance on the isolated Mac. Copy both
files into that machine's `FileCatValidation` folder, then build the app there:

```sh
capture_root="$HOME/FileCatValidation"
mkdir -p "$capture_root/FileCatCapture.app/Contents/MacOS"
swiftc -o "$capture_root/FileCatCapture.app/Contents/MacOS/FileCatCapture" "$capture_root/mac-capture.swift"
cp "$capture_root/mac-capture-Info.plist" "$capture_root/FileCatCapture.app/Contents/Info.plist"
codesign --force --sign - "$capture_root/FileCatCapture.app"
open -a "$capture_root/FileCatCapture.app"
```

For a fresh FileCat test app, use `mac-publish-git-validation.sh CHECKOUT
EXPECTED_FULL_REVISION` on the Mac. It requires a clean tracked Git checkout
inside `~/FileCatValidation`, builds that exact revision with the isolated
.NET SDK, creates a new ad-hoc signed validation `.app` and prints the DLL
hash. It refuses to replace an existing bundle and creates no ZIP archive.

Grant Screen & System Audio Recording to `FileCatCapture` in macOS System
Settings when it appears. Launch again with `open -a`; each successful run
creates a new `native-screen-*.png` in the isolated validation folder. Launch
through the logged-in desktop so CoreGraphics uses that session. The helper
captures the active display, requires an existing output folder and never
replaces another file. Keep that permission limited to the helper. macOS may
also request separate permission to bypass its private-window picker for
direct capture; grant it only with the owner's explicit approval. The owner
approved this dedicated helper's prompt on 20 October 2026, after which its
capture showed the FileCat window without a permission overlay.

`python3 mac-ax-markdown-resize.py SOURCE FileCatCapture.app REPORT.json`
uses an isolated Markdown fixture and the approved helper to record narrow
(760 × 620) and wide (1,220 × 700) native Page screenshots. It asserts the
actual window sizes, unchanged source bytes and exact fixture cleanup. Inspect
both screenshots for prose and table reflow; the runner does not claim a
pixel-layout pass by itself. The helper saves captures in
`~/FileCatValidation`, and the runner watches that folder for new PNGs.

`python3 mac-ax-viewer-mode-layout.py SOURCE FileCatCapture.app REPORT.json`
uses the same approved helper with one signed FileCat process showing the
isolated `FileCatValidation/source` panel. It opens only its own text fixture,
checks the exact viewer title and source bytes, and captures Text, Hex and
Info at approximately 720 × 500 and 1,220 × 700 logical points. Inspect all
six screenshots for wrapping, controls and scrollbars; the runner verifies
the native sizes, the exact first Hex row bytes and a larger fitted Hex row
after widening. It physically drags the Hex scrollbar down and back without
creating selection, deliberately selects bytes 0–3 in the document, and
confirms a second thumb drag preserves them before it removes its fixture.
It focuses the exact AX row and
uses FileCat's View command, which avoids an external Open handler.

`python3 mac-ax-viewer-media-layout.py SOURCE FileCatCapture.app REPORT.json`
creates a 640 × 480 PNG and a copy of the signed app's Mach-O executable in
the isolated source panel. It focuses each listing row through macOS AX and
uses FileCat's View command, avoiding the external Open handler for images.
The approved helper captures Picture and structured Info at approximately
720 × 500 and 1,220 × 700 logical points. Inspect all four screenshots for
content, controls and scrollbars. The runner checks the app identity, exact
viewer titles, window sizes and unchanged fixture bytes, then removes only
its own fixtures.

`python3 mac-ax-large-listing.py SOURCE TARGET REPORT.json` uses the existing
signed Mac validation app and `mac-ax-panel-sort-snapshot.swift` to measure a
2,002-file panel: the two original files plus 2,000 temporary empty files.
It reads a bounded direct AX tree, checks the visible row count, focused row,
ten real Down keys and source/target invariants, then removes only its own
files and refreshes the panel. Run it in both supported Retina 2× and 1×
display modes. It requires the same isolated source and empty target used by
the viewer layout runners.

`python3 mac-ax-page-copy.py SOURCE REPORT.json` opens an isolated Markdown
fixture in Page mode, clicks its accessible web content, then presses
Command+A and Command+C. It first places a unique test sentinel on the Mac
guest clipboard; the report checks that Copy replaced it with the expected
rendered heading, paragraph and table text, without Markdown markup. The
owner approved guest clipboard access. The runner captures the visible
selection through the approved helper, verifies unchanged source bytes, and
removes only its own fixture. Inspect `selectionCapture` for the selected text.

`python3 mac-ax-selection-narrow.py SOURCE FileCatCapture.app REPORT.json`
repeats the existing `mac-ax-selection-keyboard.py` pointer and Shift-range
checks with the main window at 760 × 600, captures both panels, then restores
the sampled size and position. The source must contain only the exact `a.txt`
and `b.txt` fixtures and begin with no marks. The test verifies focus, mark
count and unchanged source bytes; inspect the capture for narrow geometry.

`python3 mac-ax-copy-review-resize.py SOURCE DESTINATION FileCatCapture.app
REPORT.json` opens a Copy review for `b.txt`, checks the full destination
input, decision summary and footer actions at 640 × 480 and 980 × 680, and
captures both sizes. It cancels without submitting Copy, verifies the exact
source bytes and unchanged destination entries and bytes, and restores the
review's original size before closing. `b.txt` must be absent from the target
before the run; an existing `a.txt` is allowed. Its captures are also read
from the helper's `~/FileCatValidation` output folder.

`mac-ax-clipboard-external-copy.py` checks an actual macOS clipboard handoff
against the isolated, signed validation app. Preseed SOURCE with only
`a.txt` = `alpha\n`, `b.txt` = `beta\n`, and `c.txt` = `gamma\n` **before**
starting FileCat; DESTINATION must contain only `a.txt` = `alpha\n`. Build the
two helpers on the guest with `swiftc mac-pasteboard-files.swift -o
mac-pasteboard-files` and `swiftc mac-send-key.swift -o mac-send-key`, then run
`python3 mac-ax-clipboard-external-copy.py SOURCE DESTINATION
./mac-pasteboard-files ./mac-send-key REPORT.json`. The guest needs
Accessibility permission for the native key helper and an unlocked desktop.
The test sets a unique, test-owned clipboard sentinel, focuses `b.txt` without
invoking the row, Cut-publishes exactly its file URL, asks Finder to Copy only
`c.txt`, verifies the pasteboard generation and exact replacement URL, then
focuses the target panel and Pastes. It requires `c.txt`'s exact bytes at the
target and `b.txt` to remain at the source. It restores a test-owned clipboard
sentinel and removes both `c.txt` fixtures on success. Run only in the isolated
validation bundle: the key helper checks its bundle identifier before Cut or
Paste and Finder's identifier before Copy. CGEvent input avoids a macOS VM
System Events behavior that sent Command+A immediately before Command+X. Pass
`same-file` after the report path to test Finder Copy of the very same `b.txt`
URL. That mode starts with only `a.txt` and `b.txt` in SOURCE, still requires
only `a.txt` in DESTINATION, and removes the copied target `b.txt` after
verifying that the original source survived.

`python3 mac-ax-clipboard-owned-move.py SOURCE DESTINATION
./mac-pasteboard-files ./mac-send-key REPORT.json` uses the same signed,
unlocked validation app and helpers. Start with only `a.txt` = `alpha\n` and
`b.txt` = `beta\n` in SOURCE and only `a.txt` = `alpha\n` in DESTINATION. It
Cut-publishes exactly `b.txt`, Pastes into the target panel, waits for the
source to disappear and the target to contain the exact bytes, and confirms
both `a.txt` files stayed unchanged. It Pastes again, opens Recent
notifications and requires the explicit “already been moved” refusal while
the bytes remain unchanged. On success it restores the initial fixtures and
replaces its test-owned clipboard publication with a sentinel.

`python3 mac-ax-clipboard-partial-move.py SOURCE DESTINATION
./mac-pasteboard-files ./mac-send-key REPORT.json` uses the same two-file
fixture and signed app. It marks and Cut-publishes both exact file URLs,
Pastes into a destination that already contains `a.txt`, opens the native
conflict review and chooses Skip. It requires the original `a.txt` on both
sides, only `b.txt` moved with exact bytes, and a second Paste refusal that
states one of two items completed. It checks no replay effects, replaces its
test-owned clipboard and restores the fixture and marks on success. Keep
other foreground dialogs closed so native key events reach FileCat.
Pass `--cancel-conflict` after the report path to choose Cancel operation
instead. That route requires zero completed roots, exact unchanged source
and destination bytes, and refusal to replay the original Cut on another
Paste. It also restores the test clipboard and Commander marks.

`mac-ax-clipboard-interrupted-move.py` exercises a real process interruption
in the isolated, signed Mac validation app. Use its `seed`, `prepare`,
`inspect`, `review`, `cancel-review`, `continue-review`, `inspect-completion`
and `restore` phases with the dedicated fixture and one JSON receipt per
phase; restart the exact app/profile between `prepare` and `inspect`. The
default manifest is `~/FileCatValidation/mac-interrupted-move-fixture.json`
and the default bundle is `FileCatValidation.app`. For another signed build,
set `FILECAT_NATIVE_MOVE_FIXTURE=mac-interrupted-move-<8-hex-build>.json`
and `FILECAT_NATIVE_MAC_BUNDLE=FileCatValidation-<8-hex-build>.app` on each
phase, leaving an older fixture untouched. Both names must stay under
`~/FileCatValidation` and match the runner's restricted patterns. The
runner requires an unlocked desktop and the existing native pasteboard and
key helpers. It checks the exact two-file Cut publication, held conflict,
unchanged bytes before approval, retained journal and destination after
restart, explicit Cancel/Continue decisions, conservative one-done/one-skipped
result, closed recovery tracking and fixture restoration. Its
`reseed-after-crash` phase preserves a failed profile and journal before
resetting only this runner's exact two-file fixture for another build.

`mac-ax-active-copy-interruption.py PHASE mac-active-copy-<8hex>.json REPORT.json`
checks a real Mac data-transfer interruption in a separate 4 MiB fixture.
Set `FILECAT_NATIVE_MAC_BUNDLE` to the signed validation bundle under
`~/FileCatValidation` if it is not the default. `seed` creates an isolated
source, empty destination and profile; launch the receipt's exact executable
with `--new-instance --data DATA --left SOURCE --right DESTINATION`.
`open-form` focuses the exact source row and opens Copy. `prepare` expands
Options, sets 0.0625 MB/s, selects the modal approval rather than the main
key-bar Copy button, requires a nonzero but incomplete staged prefix matching
the source, an unpublished target and a journal retaining that exact rate and
mapping, then kills only the exact app process. `inspect-interruption` records
the durable state. Restart the same executable and profile. `inspect-recovery`,
`review`, `cancel-review`, `continue-review`, `inspect-completion` and
`stop-completed` verify the retained destination, explicit decisions, no
mutation on Cancel, byte-identical completed Copy, removed staging, closed
recovery tracking and exact-process shutdown. Keep the completed fixture and
JSON receipts in `~/FileCatValidation`; a new manifest creates a separate
fixture for another run. `inspect-window` and `inspect-form` expose bounded
read-only accessibility diagnostics when a native control differs.

`osascript mac-ax-copy-review.applescript SOURCE DESTINATION REPORT.json` runs
against a Git-built FileCat `.app` in an unlocked desktop session. Give it a
source with `a.txt` containing `alpha\n` and `b.txt` containing `beta\n`, an
empty destination and a fresh app data root. The caller needs macOS
Accessibility access. Keep a display-awake assertion active while testing;
an idle or locked display removes the app's exposed window. The script focuses
the exact row, checks both panel locations and the full Copy destination,
then verifies Cancel leaves the target empty. It reopens the review, approves
Copy, and requires exact `a.txt` bytes at the target while both sources stay
unchanged and `b.txt` is excluded. A passing JSON receipt is written only after
all UI and byte checks succeed. Run it on an isolated user-local fixture.

`python3 mac-ax-modal-guard.py BUNDLE.app REPORT.json [FileCatCapture.app] [--narrow]` runs an isolated Mac
Copy, Move and Delete review journey. It builds the bounded
`mac-ax-modal-control.swift` helper with the matching Xcode SDK and uses
direct Accessibility actions, which work even when `System Events` over SSH
exposes no window children. Each review checks its summary and destination,
tries six background toolbar/key-bar commands by AX Press, and requires one
unchanged modal, byte-exact source, empty target and safe Cancel. Use a signed
bundle and fresh report path directly under `~/FileCatValidation`; the helper
validates the executable/PID and stays inside the isolated fixture. With the
owner-approved capture helper, it records one fresh PNG per modal for visual
inspection while preserving the same no-mutation and Cancel assertions.
`--narrow` requests and verifies a 760 × 600 logical-point main window before
the three reviews; the direct helper targets only the exact titled FileCat
window even when macOS exposes another transient window.

`python3 mac-voiceover-viewer-stop.py BUNDLE.app REPORT.json [--text]` runs
the signed Mac app on its own 2 GiB sparse source, starts a Hex or Text viewer
search and presses the visible Stop through bounded direct AX. It requires
muted output and checks VoiceOver's actual last phrase for `Search canceled.`,
the unchanged sampled source, empty target and unchanged text clipboard. The
helper waits for the app's first AX window, verifies its executable/PID and
terminates only its own app; it removes the large sparse source afterward.

`osascript mac-ax-viewer-find.applescript SOURCE REPORT.json` checks the Text
viewer on that fixture's `a.txt`. It opens the viewer from the focused source
row, reveals Find, replaces any existing query with real keyboard input, and
uses Next to require the visible `1 match` status for `alpha`. It also checks
the Previous control, closes only the viewer through its accessible close
button, and verifies the source's exact bytes remain unchanged. The viewer
must be closed before starting this script; the destination may contain the
result of the Copy test.

`python3 mac-ax-selection-keyboard.py SOURCE REPORT.json` checks the native
pointer anchor and keyboard range on the same two-file fixture, with no viewer
open. It clicks `a.txt`, verifies the current-item label, presses Shift+Down
to mark that exact six-byte file, moves Up without changing the mark, then
presses Shift+Down again to clear it. It requires the source bytes to remain
unchanged. The script launches a separate `osascript` process for each input
or accessibility snapshot so the app can process the native event between
observations. Start with no marks; the test ends with no marks.

For a different macOS display mode, run the same isolated fixture and input
script through `swift mac-display-run.swift WIDTH HEIGHT PIXEL_WIDTH
PIXEL_HEIGHT /bin/bash YOUR_OWNED_TEST_WRAPPER.sh`. The four dimensions must
match a mode reported by CoreGraphics. The wrapper changes only the active
display, waits for the child, and verifies restoration of the original
logical and physical dimensions even if the child fails. Keep the Mac desktop
unlocked; record both display dimensions, the exact app build, marked files,
source bytes and empty destination in the child report.

`python3 mac-ax-panel-sort.py BUNDLE.app REPORT.json [--narrow]` launches an
isolated signed Git-built app with its own four-file source and empty target.
Keep `mac-ax-panel-sort-snapshot.swift` beside the runner; it compiles a
bounded direct AXUIElement reader using the installed matching Xcode SDK.
The test requires both panel Sort buttons and the visible menu choices in
the native tree. One physical pointer sequence reverses the source Name
order, then a physical Shift-click must select exactly `b.txt` and `c.txt`
with `c.txt` focused and a 13-byte mark summary. Keyboard selection reverses
the sort while preserving those selected identities. The target order and all
source hashes must stay unchanged. `--narrow` measures a 760 × 600 window. The
runner closes only its exact process and writes JSON under
`~/FileCatValidation`.

`python3 mac-ax-panel-filter-anchor.py BUNDLE.app REPORT.json [--narrow]`
uses the same bounded direct AX helper on a fresh, four-file Mac fixture.
It physically anchors `b.txt`, enters `*|b*` and then `*` in the source
Filter field, and physically Shift-clicks `d.txt`. The restored listing
must select only `c.txt` and `d.txt` (14 bytes) with `d.txt` focused;
source hashes and the empty target must remain exact. The optional narrow
route measures 760 × 600. Keep the Sort runner and Swift helper beside it.

`python3 mac-ax-panel-tab-anchor.py BUNDLE.app REPORT.json [--narrow]`
creates a fresh source with four files and a `second` folder containing
`e.txt` and `f.txt`. It focuses `d.txt` in the first tab, opens another
source tab with native Ctrl+T, enters `second`, and physically Shift-clicks
`f.txt` from `e.txt`. Direct AX must show three distinct tabs and exactly
the two second-folder files selected for 13 bytes, with `f.txt` focused.
It checks both source-folder hashes and the empty target. `--narrow`
repeats the route at 760 × 600.

`python3 mac-ax-panel-focus-loss.py BUNDLE.app REPORT.json [--narrow]`
uses a fresh four-file source to exercise window deactivation during a held
physical Shift gesture. It marks `b.txt`, moves focus back without changing
the mark, unmarks `b.txt` with held Shift+Down, activates Finder while Shift
remains down, releases Shift there, and returns to FileCat. The next
Shift+Down must mark only `c.txt` (8 bytes) and focus `d.txt`. Direct AX
checks the marks and focus; all hashes and the empty target stay exact.
The same run then holds Shift across a click into FileCat's command line,
releases it there, returns to the list with Escape, and requires a new
Shift+Up to mark only `d.txt` (6 bytes) with `c.txt` focused.
`--narrow` measures a 760 × 600 window. The runner releases Shift on
failure and closes only its exact FileCat process.

`python3 mac-ax-panel-refresh-anchor.py BUNDLE.app REPORT.json [--narrow]`
uses a fresh four-file source. It physically clicks `b.txt`, removes only
that owned file, creates `0.txt`, and uses Ctrl+R to reload the panel. It
then moves keyboard focus to `d.txt` and physically Shift-clicks `0.txt`.
Direct AX must show all four current files, and no deleted `b.txt`, marked
for exactly 25 bytes with `0.txt` focused. A stale numeric anchor at the old
`b.txt` row would mark a smaller range. The runner checks every resulting
source hash and the empty target; `--narrow` repeats it at 760 × 600.

`python3 mac-ax-panel-command-click.py BUNDLE.app REPORT.json [--narrow]`
uses a fresh four-file source to verify the macOS additive pointer gesture.
A physical click anchors `b.txt`, Command-click marks only `d.txt`, and
Shift-click on `c.txt` must add exactly `b.txt` and `c.txt` while retaining
`d.txt`. A second Command-click unmarks `d.txt`, leaving 13 bytes marked.
Direct AX checks every mark, focus and status; source hashes and the empty
target stay exact. `--narrow` repeats the route at 760 × 600.

`python3 mac-ax-rename-editor.py BUNDLE.app REPORT.json [--narrow]` creates a
fresh two-file source and a private editor script in an isolated profile. It
uses native pointer/Shift selection, filters one marked file, invokes Ctrl+M,
explicitly includes the hidden mark, edits the counter mask, and verifies the
scope summary and selected full-path mapping. The mapping buttons, live
position and complete selected paths stay below the scrolling form so the
review remains visible in a narrow dialog. A native capture records the
review; macOS AX still omits the virtualized preview children and reports zero
bounds for controls inside the scrolling form. With Mac output muted, the test
requires VoiceOver to speak both complete paths at the first mapping and after
Previous/Next, then sends focused Return and Space to confirm keyboard mapping
navigation. It loads the two names saved
by the configured editor, checks the explicit editor-derived status, removes
the private name list, and approves only the button inside the modal bounds.
The two resulting filenames and contents must be exact and the target empty.
`--narrow` repeats the whole route at a 760 × 600 main window. The signed
bundle, approved `FileCatCapture.app`, `cliclick`, an unlocked desktop and the
matching Xcode SDK are required.

`python3 mac-ax-viewer-backward.py SOURCE REPORT.json` creates one temporary
text file in that isolated source folder with two `NEEDLE` matches more than
4 MiB apart. It refreshes the panel, opens the file in Text view, types the
query through native keyboard input, and checks Next/Previous plus F3 and
Shift+F3 visible offsets, the exact `2 matches` status and the no-earlier-match
message. Changing the query to `first` must retire the previous count and
show `1 match` at the new offset. It compares the
complete source bytes, closes the viewer, removes only its own unchanged test
file and refreshes the listing. Begin with no viewer open and no file named
`native-backward-search.txt` in the fixture.

`python3 mac-ax-viewer-hex-backward.py SOURCE REPORT.json` repeats the same
distant-match, Previous/Next, F3/Shift+F3, visible count and changed-query
checks in native Hex mode. It creates a distinct test-owned source file,
requires exact byte offsets and unchanged content, closes the viewer, then
removes only that exact fixture and refreshes the panel. Keep the companion
Text runner beside it; both use the same isolated two-file source folder.

`python3 mac-ax-viewer-info-find.py SOURCE REPORT.json` copies only the
validation app's signed Mach-O executable into that isolated source folder.
It opens Info mode and checks the visible one-match status for `Mach-O`, the
Previous no-earlier result, then the two-match status after changing the
query to `Load commands`. Info mode describes report lines rather than byte
offsets, so the runner checks the actual visible status and unchanged copy.
It closes the viewer, removes only its exact test-owned copy and refreshes
the listing.

`python3 mac-ax-markdown-find.py SOURCE REPORT.json` creates one short
temporary Markdown file in the same isolated source folder. It opens Page
mode, reveals Find, sends Command+A/Delete and types `marker` through native
keyboard input, then checks two Next actions, Previous, F3 and Shift+F3. It
requires the visible directional lower-bound status, both accessible buttons,
unchanged Markdown bytes and a live FileCat process. The test closes the
viewer, removes only its own unchanged fixture and refreshes the listing. A
`--cleanup` fourth argument removes an exact test-owned fixture left by a
native process crash; it refuses any other file content.

`win-ax-archive-selection.ps1 -AppPath FILECAT.exe -ReportPath REPORT.json`
uses a Git-built app under the Windows guest's `FileCatValidation` folder.
It creates a disposable 2,000-member archive as listing content, verifies a
bounded native UI Automation tree, the complete marked selection while focus
moves offscreen, the marked item's original archive path in Review marked
items, and a real View action on the final member. It records app and archive
hashes and removes only its own fixture. The archive is test content; app
builds and test transfer use Git.

`python3 linux-atspi-sftp-delayed-listing.py APP REPORT.json KEY_HELPER`
uses a Git-built Ubuntu app and the isolated local OpenSSH account. It owns
2,000 remote files and a loopback SSH relay that delays server packets without
interpreting them. It requires transient `FILECAT_NATIVE_SUDO_PASSWORD` and
`FILECAT_NATIVE_SFTP_PASSWORD` environment variables, the isolated .NET
runtime under `~/FileCatValidation/dotnet-sdk`, and no other FileCat process.
The runner checks pending and complete bounded AT-SPI listings, a mark that
survives offscreen End focus, original SFTP location in Review marked items,
a real View of the last member, and unchanged source/empty target. It writes
a fresh JSON receipt and removes only its own fixture.

`win-ax-admin-notification.ps1`, `win-ax-clipboard-warning.ps1` and
`win-ax-notification-action.ps1` accept `-AppPath` and `-ReportPath` inside
the isolated, signed-in Windows VMware guest. Set 150% with the existing
`win-ax-display-scale.ps1`, verify the app's measured 144 DPI, and restore the
original scale afterward. The administrator runner must be elevated; VMware's
interactive launch has a filtered token, so run
`win-ax-admin-notification-launch.ps1` through the elevated guest launcher
with its additional `-LaunchReportPath`. It creates a temporary task for the
logged-in VM user at highest privilege, stores no password and removes the
task. The admin runner checks visible Copy/History/Dismiss, full copied text,
timed expiry and retained History. The clipboard runner copies an existing
HKCU key, requires a truthful refusal with the old clipboard kept, then
checks the warning controls, History and dismissal; it restores the guest
clipboard. The action runner saves an owned two-tab workspace, adds a third,
opens the saved arrangement and invokes the visible Restore action, checking
the two-to-three-tab transition and retained action History. Each runner
refuses a concurrent FileCat process, records the exact app DLL, writes a
fresh JSON receipt and removes only its owned fixture. The 27 October 2026
receipts are tracked under `docs/validation/evidence/ac-s2-029-*.json`.

`win-ax-nested-panel-resize.ps1 -AppPath FILECAT.exe -ReportPath REPORT.json`
uses a fresh four-panel, two-level layout in the same isolated Windows VM.
Set 150% first with `win-ax-display-scale.ps1`; the runner requires 144 DPI,
four lists with at least two visible file rows each, named horizontal and
vertical slider peers, keyboard Down/Escape and Tab/Right/Enter, a native
mouse drag on the outer splitter, and exact geometry after reopening the
test-owned saved arrangement. It verifies all 12 source hashes and the saved
JSON hash, closes its own app and removes only its owned fixture. Restore the
VM's original display scale afterward. The dated native and scale receipts
are tracked under `docs/validation/evidence/ac-s2-030-windows*.json`.

`linux-atspi-nested-panel-resize.py APP REPORT.json KEY_HELPER POINTER_HELPER`
uses the isolated Ubuntu VMware guest and a fresh four-panel saved workspace.
Set GNOME's supported 2560 × 1600 mode at 200% first. The runner checks four
lists with at least two visible rows each, named/value-bearing AT-SPI
horizontal and vertical splitters, keyboard Down/Escape and Tab/Right/Enter,
a physical pointer drag, exact restored rectangles and unchanged owned files
and workspace JSON. It refuses another FileCat/Orca process, removes only its
fixture and closes its own app. Restore the guest's original display mode
afterward. The 27 October native, selected-mode and restored-mode receipts
are tracked under `docs/validation/evidence/ac-s2-030-ubuntu*`.

`linux-atspi-remote-breadcrumb.py APP REPORT.json KEY_HELPER` runs in the
isolated Ubuntu desktop at 1920 × 1200/200% with a test-owned deep SFTP
path. Pass the sudo/uinput and SFTP credentials only as transient environment
variables. The runner independently checks the shown host-key fingerprint,
then uses physical Alt+Up, arrow/Enter, Ctrl+Tab and Ctrl+L. It records Orca
speech, exact Copy path, Open here/new tab, cancel/current-list focus and
unchanged local/remote files; it restores the prior clipboard, closes only
its owned app/Orca and deletes only its fixture. The 27 October direct receipt
is `docs/validation/evidence/ac-s2-032-ubuntu.json`.

`linux-atspi-settings-structured.py APP REPORT.json KEY_HELPER` runs the
editable Settings route at Ubuntu GNOME 200%, with a fresh test-owned data
profile, a spaced executable path and a program-run marker. It checks the
user-command labels and Tab-to-Browse route, nonexecuting argument preview,
filter exclusion, association priority, Advanced text round trip and
Cancel/reopen restoration. It preserves its sample bytes, removes only its
fixture and never runs the configured tool. The 27 October direct receipt is
`docs/validation/evidence/ac-s2-036-ubuntu.json`.

`linux-atspi-command-availability.py APP REPORT.json KEY_HELPER` checks
native local-file to Computer-drive command admission through physical menu,
search and F8 keys plus AT-SPI toolbar/key-bar state and help. It confirms
exact owned source and empty target. The 27 October direct receipt is
`docs/validation/evidence/ac-s2-037-ubuntu-parity.json`.

`linux-atspi-availability-latency.py APP REPORT.json KEY_HELPER` uses a
fresh 2,000-file guest-local SFTP folder through an owned loopback SSH relay.
Pass the input and SFTP passwords only in transient environment variables.
After verifying the server fingerprint and complete listing, it drops only
its relay and sends ten physical Down and Up strokes. Focus, bounded time,
stable command state and source/target integrity are recorded. It closes
only its app and relay and removes only its fixture. The direct receipt is
`docs/validation/evidence/ac-s2-037-ubuntu-latency.json`.

`linux-atspi-rename-slow-check.py APP REPORT.json KEY_HELPER` starts an
isolated Ubuntu app with three owned files, then attaches a path-filtered
`strace` only after the Bulk rename dialog opens. It holds the actual target
`lstat` for eight seconds, changes the rule during the hold, verifies the
new full-path mapping wins before and after the old call returns, and cancels
with physical Escape. It records the delayed syscall and source hashes,
terminates only its app/tracer and leaves the target empty. The 27 October
direct receipt is `docs/validation/evidence/ac-s2-038-ubuntu-slow.json`.
`--cancel-held` reopens the owned review, presses physical Escape before a
second held `lstat` returns and checks no late publication; `--move-held`
uses physical Alt+F7/Left/Enter to move the window during the delay. Their
separate receipts are `ac-s2-038-ubuntu-cancel-held.json` and
`ac-s2-038-ubuntu-move-held.json`. `linux-orca-rename-counter.py APP
REPORT.json KEY_HELPER` runs Orca against an invalid counter draft and
records actual spoken error, exact input, disabled approval and unchanged
bytes in `ac-s2-038-ubuntu-counter.json`.

`linux-atspi-transfer-stale-destination.py APP REPORT.json KEY_HELPER` uses
an isolated Copy form and Czech culture. It attaches path-filtered `strace`
to hold only the old target's actual `stat` for eight seconds, removes that
owned folder, enters a current folder, checks both submission controls and
corrects an invalid comma-decimal speed in the same form. The current target
must receive the exact source bytes; the old target must stay absent after
the delayed call returns. It closes only its app/tracer and records the
bounded syscall, mapping and hashes in
`docs/validation/evidence/ac-s2-041-ubuntu-stale.json`.

`linux-atspi-operations-drawer.py APP REPORT.json KEY_HELPER POINTER_HELPER`
uses a fresh Ubuntu data profile and byte-exact local Copy. It opens the
Operations drawer, resizes its focused grip using physical Down/Up and a
short-lived uinput pointer, then narrows the X11 window to 1,280×840 physical
pixels. It verifies a visible operation history and Open report action inside
the window, writes `ac-s2-042-ubuntu-drawer.json`, and closes only its own app
and input device. The Ubuntu acceptance run used a separately measured GDK
monitor scale of 2; pair its result with `OperationsDrawerLayoutTests` for the
1/5/20 interrupted-row combinations.

`linux-atspi-recovery-stop-held.py APP REPORT.json` requires a Git-built
Ubuntu app under the guest's `~/FileCatValidation`, a fresh report path there,
and the same-directory `linux-atspi-active-copy-interruption.py` and
`linux-atspi-clipboard-owned-move.py` helpers. Set
`FILECAT_NATIVE_SUDO_PASSWORD` only for the transient guest runner; it is
removed from the FileCat child environment. The runner copies owned bytes at a
bounded rate, kills only its exact child after a durable journal/staging file,
restarts that profile, and uses path-filtered `strace` to delay the source
`lstat` eight seconds. It requires Reviewing/disabled Continue/Stop before the
delay returns, invokes Stop, then checks byte-exact source/staging/journal,
unpublished target and no late approval. It closes only its own app and tracer.
The accepted receipt is `docs/validation/evidence/ac-s2-042-ubuntu-recovery-stop.json`;
narrow visual reachability passed separately. Add `--speech` with no other Orca
process to start a test-owned reader, require its actual Reviewing, Stop and
final stopped speech, and record the
`docs/validation/evidence/ac-s2-042-ubuntu-recovery-orca.json` transcript.
For a diagnostic screenshot during Reviewing, set
`FILECAT_NATIVE_CAPTURE_PAUSE=3` on the runner and capture the VM while its
owned `ac-s2-042-capture.ready` marker exists; the bounded pause still leaves
Stop before the eight-second source read returns. The 200% visual result is
`docs/validation/evidence/ac-s2-042-ubuntu-recovery-visible.png`.

`linux-atspi-find-sftp-scope.py APP REPORT.json KEY_HELPER` runs in the
logged-in Ubuntu guest with `FILECAT_NATIVE_SUDO_PASSWORD` for physical Enter
and `FILECAT_NATIVE_SFTP_PASSWORD` for its loopback SSH login. The runner
removes both from FileCat's environment, creates an isolated local/remote
fixture and data profile, independently matches the displayed SSH host key,
then opens Find from the connected SFTP panel. It requires explicit SFTP
limitation guidance, an empty Look in and disabled Find, and checks the owned
source bytes. Its accepted receipt is
`docs/validation/evidence/ac-s2-044-ubuntu-sftp-scope.json`; Registry,
result-set and narrow completion are separate open ledger boundaries.

`win-ax-background-archive.ps1 -AppPath FILECAT_EXE -ReportPath FRESH_JSON
-Provider Archive -BackgroundOnly` runs in the logged-in Windows VM. The
Git-built executable and fresh report must be inside the isolated validation
root. It creates an owned read-only TAR, marks and focuses its member, then
right-clicks list whitespace and checks location actions, disabled reasons,
mark/focus retention and unchanged source/target. `-Provider Registry` uses
an owned HKCU key with one child and removes that key after checking it.
Without `-BackgroundOnly`, the runner also probes Shift+F10 and the Menu key
on the focused item; that separate diagnostic has not passed on the Windows
guest and is not part of the accepted background receipts. Existing Ubuntu
physical Shift+F10 evidence is recorded separately in the acceptance ledger.

`linux-atspi-background-disconnected.py APP REPORT.json KEY_HELPER POINTER_HELPER`
uses the logged-in Ubuntu VMware guest, transient input/SFTP credentials and
the guest's loopback SSH service. It connects through a test-owned TCP relay,
independently checks the displayed server key, loads one owned member, drops
only the relay and rereads the same remote panel. A physical uinput right
click on listing whitespace must then expose disabled Paste/Create with
accessible Reread reasons, plus safe Reread/Copy location name. The panel
continues to name its exact remote address and failed refresh; source hashes
must stay exact. The runner kills only its own app, closes its own relay and
deletes only its own fixture. Its direct receipt is
`docs/validation/evidence/ac-s2-048-ubuntu-disconnected.json`.

`linux-atspi-archive-viewer-refresh.py APP REPORT.json KEY_HELPER` runs in
the isolated Ubuntu desktop with a transient physical-input credential. It
opens one member of an owned read-only TAR in the Text viewer, atomically
replaces that TAR, and requires Refresh to change the content without opening
another viewer. Removing the TAR before the next Refresh must leave the last
good text visible and display a readable single-sentence failure; the runner
then restores the last good archive and checks its SHA-256 and empty target.
The accepted receipt is
`docs/validation/evidence/ac-s2-052-ubuntu-archive-refresh.json`.

`linux-atspi-remote-viewer-refresh.py APP REPORT.json KEY_HELPER` runs in the
logged-in Ubuntu VMware guest with transient physical-input and SFTP
credentials. It independently checks the loopback SSH server key, remembers
that verified key only in an owned disposable profile (Refresh needs another
connection while the first source is leased), and opens a remote Text viewer.
Its owned relay delays the changed reopen: the old content must remain until
the new view is ready in the same window. Dropping only that relay makes the
next Refresh fail with a visible reason while preserving the last good view.
The runner checks exact source bytes and an empty target and removes its owned
profile and fixture. The accepted receipt is
`docs/validation/evidence/ac-s2-052-ubuntu-remote-refresh.json`.

`linux-atspi-page-viewer-refresh.py APP REPORT.json KEY_HELPER` runs in the
same isolated Ubuntu desktop with a transient physical-input credential. It
opens an owned Markdown Page in native WebKit, uses rendered Page Find to prove
an explicit same-window changed-source Refresh, removes the file to verify a
readable stale/failure state, then restores it and verifies a rendered retry.
Stale Page search is intentionally guarded until Refresh; this runner checks
the retained Page mode and stale explanation instead of expecting Find to
search stale content. The receipt is
`docs/validation/evidence/ac-s2-052-ubuntu-page-refresh.json`. Engine load
failure after replacement starts has its own accepted child in the ledger.

`PageEngineFaultHarness` is a locked, isolated native acceptance executable
for the replacement-engine boundary. It opens an in-memory Markdown Page and
fails the fourth replacement-source read, after the viewer's prefix and
preflight reads, when the native browser requests the replacement. It requires
the old rendered token and Page mode to survive, rejects the new token and
records the failure banner. Build its project in
`tests/FileCat.NativeAcceptance/PageEngineFaultHarness` with 16 workers, then
run its executable in the logged-in Windows desktop with a fresh report under
`%LOCALAPPDATA%\FileCatValidation`, or use
`linux-page-engine-fault-launch.py` in the logged-in Ubuntu guest with a fresh
report under `~/FileCatValidation`. The two accepted receipts are
`docs/validation/evidence/ac-s2-052-win-webview2-engine-fault.json` and
`docs/validation/evidence/ac-s2-052-ubuntu-native-engine-fault.json`. The
Windows browser may briefly retain its private test profile after viewer
close; the harness bounds immediate cleanup and reports its exact path for
removal after the helper exits.
