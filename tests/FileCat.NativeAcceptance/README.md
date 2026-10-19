# Native Windows acceptance runner

This runner exercises actual Win32 pointer input against a running FileCat
window. It requires a logged-in Windows desktop, FileCat, and the locally
installed Windows Application Driver listening on `127.0.0.1:4723`. It does
not connect the driver to a network interface.

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
the visible key-choice heading, new-copy action, one-root name mapping, full
review heading and approval button in their expected native reading order.
The fixture uses a real journal with its transfer-intent fields removed and
CRC rewritten, so startup follows the legacy path without inventing a ZIP or
modifying user data.

The `snapshot`, `find`, `advanced`, `tools-menu`, `settings`, `settings-tools`,
`editor-browse`, `editor-value`, and `editor-draft-cancel` modes are diagnostic
or setup actions. They do not count as passing acceptance checks. Review the
VM screenshot and JSON result together when recording native acceptance.
