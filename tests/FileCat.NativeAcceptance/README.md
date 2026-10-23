# Native Windows acceptance runner

This runner exercises actual Win32 pointer input against a running FileCat
window. It requires a logged-in Windows desktop, FileCat, and the locally
installed Windows Application Driver listening on `127.0.0.1:4723`. It does
not connect the driver to a network interface.
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
When launching the app or this runner with VMware `vmrun runProgramInGuest`,
pass `-interactive`; otherwise the process starts in Session 0 and cannot
inspect or control the guest user's desktop.
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
`~/FileCatValidation`. It reads both full selectable endpoint values and
accessible names through bounded native AX traversal, uses pointer input and
physical Down/Return to reverse direction and choose Mirror, resizes the
review to 640 × 600 logical points, captures its wrapped paths and warning,
then cancels and verifies the exact fixture trees and file hashes.
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
fixture for inspection and closes only its own FileCat instance.
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
It opens `a.txt` in the viewer, selects exactly byte zero in Hex mode, reviews
the one-byte checksum scope, and verifies the complete known SHA-256, CRC-32,
byte count and source revision text. It also checks that Compute leaves the
clipboard unchanged and explicit Copy publishes the complete visible result.
The runner closes the viewer and verifies the fixture files are unchanged;
`selection-stop` then closes the isolated app.
On another fresh fixture, `checksum-narrow-native <report.json>` repeats the
one-byte result and explicit Copy after measuring a 600 × 420 viewer window.
The complete result and Copy control must remain visible and the source bytes
and destination must remain unchanged. Finish with `selection-stop`.
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
For FCUX-007, use another fresh `seed-selection` fixture and run
`search-native <report.json>` on its launched isolated app. The interactive
runner creates a 13 MiB guest-local text file with `NEEDLE` near 1 MiB and
8 MiB, rereads the folder, opens Text view, and goes to the end. The visible
Previous button must reach the later match, then the earlier match across
more than 4 MiB, then report no earlier match. The report records both
visible offsets and checks the source markers and untouched destination.
The runner closes the viewer; finish with `selection-stop`.
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
the report records how often the focused file was named on return. Supply
`FILECAT_NATIVE_SUDO_PASSWORD` only to the runner process, as for the other
uinput checks. `--record-all` keeps a complete transcript after an unmet
expectation.

`linux-atspi-modal-guard.py APP_PATH REPORT.json` runs against a Git-built,
isolated Linux `FileCat` binary below the logged-in user's
`~/FileCatValidation` directory. It creates a fresh source, destination and
data profile, opens Copy for one file, then invokes obscured toolbar and
key-bar Copy/Delete actions through AT-SPI. It requires one unchanged Copy
review, no target, and the original source hash after Cancel. The JSON report
records the app hash, fixture and four invocation outcomes. Run it with the
user's AT-SPI session bus; it needs system Python with `gi.repository.Atspi`.

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
editor.

## macOS logged-in desktop

`python3 mac-ax-focus-speech.py BUNDLE.app REPORT.json` follows the macOS
accessibility focus VoiceOver speaks from. Keep `mac-ax-focus-observer.swift`
beside it; the runner compiles that read-only `AXObserver` with the matching
Xcode SDK, retries its registration until the starting app's accessibility
answers, and records every `AXFocusedUIElementChanged` with the focused
element's role, title, description, value and help. After each System Events
key it also reads the application's `AXFocusedUIElement`. The journey matches
the NVDA and Orca runners (Finder takes the foreground for the switch step):
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
VoiceOver's own speech additionally needs "Allow VoiceOver to be controlled
with AppleScript", which macOS only enables after an administrator
authorization prompt; leave that to the owner.

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
target. It captures a diagnostic screenshot on failure. System Events' `entire
contents` enumeration can return zero for a visibly populated Avalonia window;
the native AXUIElement children expose the control tree directly. This route
checks the accessible label and pointer behavior; actual VoiceOver speech and
other display scales need separate acceptance.

`python3 mac-ax-finished-speed.py SOURCE DESTINATION REPORT.json` runs a
one-file Copy in the signed validation app, then requires an explicitly
labelled numeric average in the selected finished Operations details. It
narrows the main window to 760 × 600 and checks the same accessible rate,
restores the original size and exact two-folder fixture, and removes only the
test-produced destination `b.txt`. Start with `a.txt` = `alpha\n` and `b.txt`
= `beta\n` in SOURCE, only `a.txt` = `alpha\n` in DESTINATION, and no marks.

`python3 mac-ax-hex-editor-backward.py SOURCE REPORT.json` opens a test-owned
7 MiB binary in the signed validation app and uses the accessible Hex editor
Next and Previous buttons to cross two matches more than 4 MiB apart. It
checks the visible, bounded distinct-visited match count, F3 and Shift+F3,
the no-earlier message and count reset after changing the query. The runner
requires an unlocked desktop, verifies that the editor has no unsaved changes
and leaves the source bytes intact, and removes only its own fixture.

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
scope summary and selected full-path mapping. A native capture records both
preview rows because macOS AX currently omits the virtualized preview children
and reports zero bounds for the visibly rendered modal body. The test
activates Previous and Next through AX and then sends focused Return and Space
to confirm keyboard mapping navigation. It loads the two names saved
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
