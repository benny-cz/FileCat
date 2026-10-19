# Native Windows acceptance runner

This runner exercises actual Win32 pointer input against a running FileCat
window. It requires a logged-in Windows desktop, FileCat, and the locally
installed Windows Application Driver listening on `127.0.0.1:4723`. It does
not connect the driver to a network interface.
When launching the app or this runner with VMware `vmrun runProgramInGuest`,
pass `-interactive`; otherwise the process starts in Session 0 and cannot
inspect or control the guest user's desktop.

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
`legacy-stop <report.json>` gracefully closes only the isolated VM build after
the review; it verifies that build's executable path before closing it.
The fixture uses a real journal with its transfer-intent fields removed and
CRC rewritten, so startup follows the legacy path without inventing a ZIP or
modifying user data.

For native Windows clipboard interchange, run `seed-clipboard
clipboard-fixture.json` in the guest and launch the current Git-built FileCat
with its reported `--data`, `--left` and `--right` paths. Keep that seed report
beside every following report. The fixture contains `a.txt` and `b.txt`.
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
Run `rename-stop <report.json>` to close only the path-verified isolated app
before starting another fixture.
For FCUX-002, run `seed-selection <fixture.json>` and launch the Git-built app
with its reported source, destination and data paths. Run
`selection-native <report.json>` in the interactive guest desktop: it clicks
`b.txt` as the range anchor, reverses the name sort, then Shift-clicks `c.txt`
with native input. Only `b.txt` and `c.txt` may be marked, and the fixture
files must remain unchanged. `selection-stop <report.json>` closes that
path-verified app after the check. On another fresh fixture,
`selection-filter-native <report.json>` clicks `b.txt`, filters it out,
restores the full list and Shift-clicks `d.txt`. The retired anchor must not
return; only the fallback range `c.txt`–`d.txt` may be marked. Other FCUX-002
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
For FCUX-008, use a fresh `seed-selection` fixture and its launched isolated
app, then run `checksum-native <report.json>` in the interactive guest desktop.
It opens `a.txt` in the viewer, selects exactly byte zero in Hex mode, reviews
the one-byte checksum scope, and verifies the complete known SHA-256, CRC-32,
byte count and source revision text. It also checks that Compute leaves the
clipboard unchanged and explicit Copy publishes the complete visible result.
The runner closes the viewer and verifies the fixture files are unchanged;
`selection-stop` then closes the isolated app.
For FCUX-007, use another fresh `seed-selection` fixture and run
`search-native <report.json>` on its launched isolated app. The interactive
runner creates a 13 MiB guest-local text file with `NEEDLE` near 1 MiB and
8 MiB, rereads the folder, opens Text view, and goes to the end. The visible
Previous button must reach the later match, then the earlier match across
more than 4 MiB, then report no earlier match. The report records both
visible offsets and checks the source markers and untouched destination.
The runner closes the viewer; finish with `selection-stop`.
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
