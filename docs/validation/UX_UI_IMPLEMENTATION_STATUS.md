# Severity-ordered UX/UI audit implementation

Work starts with the 13 S1 findings in the supplied [76-finding audit](FileCat_UX_UI_Audit.md), then continues into the shared S2 dialog and viewer foundations and the S3 refinements. The implementation is organized by finding below. The second batch also resolves the original test failures and verifies ordinary-user permission behavior. Native desktop acceptance remains separate from automated headless checks.

**Latest native acceptance (20 October 2026):** FCUX-003 passed two fresh
Windows 11 VM journeys. The filtered hidden mark was excluded by default;
after review, only the visible file was renamed and both file contents matched
the submitted scope. Explicit inclusion regenerated a two-row counter mapping
and renamed both files with exact contents. The repeatable
`rename-native`/`rename-hidden-native` runner checks these UI and disk results.
At a measured 760 × 560 main-window size, a further fresh fixture kept the
hidden-mark disclosure, preview, mask and Rename action accessible. It renamed
only the reviewed visible file with the exact source bytes. Scaled display,
other narrow states, spoken/keyboard review and stale external-editor
acceptance remain open. The live remaining gates are maintained in
[UX_UI_REMAINING.md](UX_UI_REMAINING.md).
The same 760 × 560 Windows window initially showed only one of two included
hidden-item mappings. Bulk rename now exposes a resize grip and groups counter,
search and case controls in an expandable section, giving the preview priority.
The corrected native journey showed both mappings before approval and renamed
both files with exact contents. Focused dialog, counter and scope tests passed
18 cases with one Unix-only skip. The complete local Release App suite at
`c8a740d2` passed 2,369 cases with 63 platform skips and zero failures using
16 workers. The two subsequent `main` CI runs, `39382446095` and
`39384598129`, passed their producer, Windows, Ubuntu, macOS and Windows ARM64
lanes. A further fresh Windows 11 VM run expanded the 760 × 560 owner to
1060 × 800, opened counter/search/case options, and dragged the review grip.
The preview widened from 658 to 844 physical pixels; both mapping rows and
Cancel/Rename remained on screen, and both renamed files retained exact bytes.
The fixture runner now closes every exact-path validation instance before a
new run, reporting whether any stale modal required forced termination.
Smaller/scaled, keyboard and spoken review remain open.
A further fresh VM fixture started with a requested 640 × 420 owner (measured
656 × 459 including native window chrome). The hidden-mark choice, name mask
and Cancel/Rename actions were visible there. After expansion, both exact
counter mappings and the advanced options remained visible through a grip
resize; approval renamed the two expected files with their original bytes.
`rename-hidden-compact-native` records the physical bounds, visible controls,
reviewed mappings, reflow and disk results. Scaled display, keyboard and
spoken review remain open.
The logged-in Ubuntu VMware desktop now runs a self-contained Linux
publish from the Git checkout against a user-local fixture. A repeatable
AT-SPI test focused `a.txt`, inspected the full destination in the Copy review,
verified Cancel left both trees unchanged, then approved Copy. Only `a.txt`
appeared at the target with the exact `616c7068610a` bytes; unselected `b.txt`
and both source files were unchanged. The test uses the native accessibility
tree and checks the disk result. The Mac ARM64 self-contained publish also
started through a signed user-local `.app` after waking its idle display;
the same build failed during Avalonia's render-timer setup while the display
was asleep. With an unlocked console and a display-awake assertion, macOS
Accessibility exposed its FileCat window, source and target lists, exact row,
Copy form and action buttons through SSH. A repeatable AppleScript native test
focused `a.txt`, reviewed the full target folder, verified Cancel changed no
target, and approved a Copy. The target had the exact `616c7068610a` bytes,
both sources remained unchanged and `b.txt` was excluded. macOS screen capture
from SSH still fails; other Mac pointer, scaling and viewer checks remain open.
The preceding `537ccb0b` CI run exposed two asynchronous test races. Its
Windows recovery test dispatched a synthetic click while the action was still
disabled after Cancel; the test now waits for the owned reservation to end and
the button to enable, as physical input must. Its ARM64 damaged-picture test
allowed 10 seconds for a worker with a 30-second decode bound; the test now
waits through that bound with a visible-state diagnostic on timeout. Both
focused Release App tests pass locally; the complete suite and subsequent CI
remain the validation gate.
FCUX-018 also passed a fresh Windows 11 VM Create-and-edit empty-file Undo
journey. Its inverse review showed the complete wrapped target path. Cancel
left the zero-byte file unchanged, and approval removed it while both source
files retained their bytes. The shared empty-folder path passed again on a
separate fresh fixture after this runner extension.
Another fresh created-file fixture changed the file after creation. The
approved Undo kept its exact changed bytes; the retained report's Copy all text
identified one kept action, the complete path and the changed-since-creation
reason. Further link inverse kinds, interruption and incomplete reports remain open.
The next native fixture exposed a Create link radio-group race: Hard link was
selected while the preview still described a symbolic link and Create stayed
disabled. Refresh now follows both check and uncheck transitions. A focused
headless test passed (2/2 link-dialog cases), and a fresh Windows 11 VM run
created a real hard link, verified shared bytes, displayed both complete paths
in the wrapped Undo review, preserved both names on Cancel, and removed only
the new name on approval. Junctions remain open.
A separate native hard-link fixture then replaced the original filename with
a different file before Undo. The inverse safely kept the created link and the
replacement with distinct exact bytes. Its retained Copy all report stated
one kept action, the complete link path and the original-name identity reason.
The complete Release App suite for the link-dialog fix passed 2,370 cases with
63 platform skips and zero failures using 16 workers. Two more fresh Windows
11 VM fixtures verified symbolic-link Undo: the normal inverse removed only
the link after a safe Cancel, and a replaced-link inverse kept both distinct
files with a retained one-kept report naming the full link path and reason.
Interrupted and incomplete outcomes remain open.
Two further fresh Windows 11 VM fixtures covered the remaining link kind:
FileCat created a real junction whose child bytes followed the target, and
its Undo review named both full paths. Cancel preserved the junction; approval
removed only that name. When a real folder replaced the junction, Undo kept
both directories and their distinct bytes, and the retained Copy all report
identified one kept action with the full path and replacement reason. The
remaining FCUX-018 gates concern held inverses, close/restart, interrupted
rename journals and large mixed or incomplete reports.
FCUX-002 also passed a native four-file sort reversal: a click on `b.txt`
survived the changed row order, and Shift-clicking `c.txt` marked exactly
those two items. A second native journey hid and revealed `b.txt`; its retired
anchor did not return, and Shift-clicking `d.txt` marked only `c.txt` and
`d.txt`. A shorter second tab also retained its own focus and Shift-click
range, marking exactly `e.txt` and `f.txt` after the first tab's `d.txt`
anchor. Other native gesture/focus routes remain open.
The fresh-file refresh route now also passed twice: after removing the
anchored `d.txt` and adding `0.txt`, Ctrl+R kept `b.txt` focused and a
Shift-click on `c.txt` marked only `b.txt` and `c.txt`. A separate guest-local
Ctrl-click journey passed: `d.txt` toggled on, `b.txt` toggled on, `d.txt`
toggled off, and the following Shift-click on `c.txt` marked exactly `b.txt`
and `c.txt` from the retained anchor on two fresh fixtures. A further two
fresh guest-local fixtures passed Shift+Down marking of `b.txt` and `c.txt`,
ordinary Up focus movement without changing marks, and Shift+Down unmarking
of both. A further fresh Win11 fixture held physical Shift while switching
from FileCat to its own Explorer window, released Shift there, and returned.
The first Shift+Down unmarked `b.txt` and focused `c.txt`; the next one marked
only `c.txt` and focused `d.txt`, with every source byte unchanged. The
native runner checks foreground ownership and the physical modifier state.
The same journey then unmarked `c.txt`, shifted focus to the command line,
released Shift there and returned to the list with Escape. A fresh Shift+Up
marked only `d.txt`, without a pointer reset on return. Narrow/scaled native
layouts and non-Windows platform input remain open. The sort-reversal and
Shift-click journey also passed in a measured 720 × 540 Windows window with
exactly `b.txt` and `c.txt` marked and all source bytes unchanged; other narrow
focus routes and scaled display input remain open. The 720 × 540 window also
passed the held-Shift Explorer deactivation and command-line focus-loss
journey, with fresh marks on return and unchanged source bytes.
FCUX-008 passed twice on fresh guest-local Windows fixtures: the native Hex
view exposed a one-byte selection at offset zero, the checksum panel offered
and used that exact scope, and the visible complete SHA-256, CRC-32, count and
revision matched the source byte. Compute did not change the clipboard;
explicit Copy published the full displayed result. The fixture files and
destination remained unchanged. A separate fresh native journey started with
the one-byte selection, explicitly chose Whole file and verified the full
23-byte SHA-256, CRC-32, count and revision. Compute preserved the clipboard;
Copy published the complete result and both fixture trees were unchanged.
Another fresh fixture used a 768 MiB whole-file source and pressed the visible
Stop action during Compute. Its result was explicitly canceled, contained no
hash, left Copy disabled and preserved the clipboard and fixture bytes.
On a further fresh fixture, changing the source after the one-byte scope
review but before Compute caused an explicit stale-scope refusal. No hash was
published, Copy remained disabled, the clipboard was untouched and the source
was restored after verification. Incomplete sources, changes during the read,
scaled layout and non-Windows native routes remain open. A fresh 600 × 420
Windows viewer also kept the complete one-byte result and Copy action visible,
with the same exact hashes, explicit clipboard change and unchanged files.
FCUX-007 passed twice on fresh guest-local Windows fixtures: the native Text
viewer opened a 13 MiB file at its end, and successive Previous button
actions revealed matches near 8 MiB and 1 MiB, then reported no earlier
match. The visible line offsets and unchanged source markers were checked.
A separate 13 MiB UTF-16LE/BOM fixture passed the same native Previous
journey: matches near 8 MiB and 1 MiB, then a complete no-earlier-match
status, with both encoded markers and the destination unchanged. The focused
Core backward-search and App viewer-search suites passed 26 and 25 cases,
respectively, using 16 workers. Editor, cancellation, changed-query,
provider-native and non-Windows routes remain open.

The current local Core and App Release suites both exited successfully with
16 workers at `757d5777`. Their platform-specific skips still need native
acceptance. Cross-platform CI is green for `c2df4a02` and `fe5d3bbe`;
`da36aefd` and `757d5777` are also green. An intermediate Ubuntu run at
`57eb810b` failed
the headless exceptional-comparison pointer test before a queued reveal
made its button hittable. That test now waits for a genuinely reachable
pointer target; its focused case and all 23 comparison-view cases pass
locally with 16 workers. Subsequent validation runs are still in progress.
An intermediate Windows ARM64 run at `0bad4066` stopped inside
`actions/setup-dotnet` with an internal CLR error before a project built;
the later `cff49384` run passed producer checks and all Windows, ARM64,
macOS and Ubuntu lanes. The final head still needs its own green receipt.
[UX_UI_REMAINING.md](UX_UI_REMAINING.md) tracks the live gate.

**Implementation checkpoint (19 October 2026):** All 76 numbered audit findings
and eleven subsequently queued bugs have an implemented code path and regression
coverage. The complete local Release runs passed 4,428 Core cases (150 platform
skips) and 2,354 App cases (63 platform skips), with zero failures, using 16
workers where the test runner supports them. The [cross-platform CI run at
`2a7c758e`](https://github.com/benny-cz/FileCat/actions/runs/39203860375)
passed producer checks, Windows, Ubuntu, macOS and Windows ARM64. The
[`8578a7d3` CI run](https://github.com/benny-cz/FileCat/actions/runs/39227087344)
passed Ubuntu, macOS and Windows ARM64; Windows failed one existing 70-step
Undo lifecycle test after its 15-second small-job deadline, while its other
test projects passed. The same case passed locally, and the complete Core
suite passed 4,428 cases with 150 skips. The bulk case now has a bounded
60-second deadline and monotonic timing with progress diagnostics; all 14
focused Undo lifecycle cases pass. The final
dialog-focus correction passed 28 focused App cases twice and the complete
2,346-pass App rerun; seven focused viewer-refresh cases passed after waiting
for the replacement page's visible rows. Native provider, scaling,
non-Windows clipboard and assistive-technology acceptance remains open.
Page-mode Find now shows WebKitGTK's rendered-page total when complete, or a
labelled lower bound beyond 1,024 matches. Windows WebView2 and macOS WKWebView
continue to show a truthful directional lower bound with the pinned browser
interfaces. After restarting Codex, native Windows Computer Use became
available. The live WebView2 Markdown viewer displayed Find, Previous/Next and
the truthful directional lower-bound status, and expanding its window expanded
both prose and table columns. F5 refreshed a changed Markdown file and cleared
its changed-file banner. Native narrow-window pointer and cross-platform
acceptance remain open. Maximizing, restoring and reopening exposed a transient
window-size capture. A second live run showed that an early maximized-sized
event precedes the window-state notification, so normal-size persistence now
waits for stable geometry and discards pending sizes on a state change. The
revised Windows build preserved a 1,592 × 1,414 window through maximize,
restore, close and reopen, and the saved dimensions stayed unchanged after
closing both windows. A Linux CI run exposed a
second WebKitGTK detail: Next/Previous reports a directional count, so the
initial rendered-page total is now cached across navigation. The Linux lane
passed on the next CI run. Native Windows Registry Find expanded and shrank
using its resize grip, retaining visible controls at the smaller size. Advanced
criteria and Ignored folders also expanded and shrank by pointer, with their
inputs reachable by scrolling at minimum size. A
temporary NTFS file's alternate stream was deleted with the Delete key;
Windows confirmed its removal on disk and FileCat's parent Streams cell was
blank immediately on returning to the folder, without an app restart.

The current `684a9e7e` build was also deployed directly from the Git checkout
to a running Windows 11 VMware guest. A guest-local native acceptance runner
used Win32 pointer input against FileCat's **Find files** resize grip: its
window grew from 1,056 × 759 to 1,226 × 879 physical pixels and returned
exactly to 1,056 × 759. The same drag passed while Advanced criteria was open;
its eight checked inputs/actions remained visible in the guest's native UI
Automation tree afterward. The VM test did not take focus from the host
desktop. The [`684a9e7e` CI run](https://github.com/benny-cz/FileCat/actions/runs/39232794548)
passed producer checks and all four platform lanes. Native scaling and
assistive-technology checks remain open.

The guest also passed native FCUX-036 editor checks. Windows' **Choose editor
program** picker opened from the Tools tab, and Cancel returned to the same
draft. Selecting `C:\Program Files\dotnet\dotnet.exe` displayed that spaced
program path and the sample filename in FileCat's nonexecuting invocation
preview. Canceling Settings and reopening it restored the original editor
value. The VM runner records these assertions without committing a Settings
change. The next guest run also passed the editor keyboard path:
Tab from the program field focused Browse, Enter opened the Windows picker,
Escape closed it, and Settings remained available. Remaining FCUX-036 native
work is broader keyboard navigation, scaling and assistive technology.

The same guest passed repeatable native Markdown Page checks against this
tracked status file. Find, Next and Previous remained available, and the
rendered-page status reported “At least 1 match · total unavailable in Page
mode” after searching for `FileCat`. A pointer drag expanded the viewer from
996 × 739 to 1,346 × 919 physical pixels. The table grew from 916 to 1,251
pixels, while the prose block preceding the implementation checkpoint became
25 pixels shorter as its lines reflowed. The runner returns the viewer to its
original bounds after measuring. A second pointer drag narrowed the viewer
from 996 to 696 pixels. The document contracted from 980 to 680 pixels, and
the keyboard-focusable table region from 901 to 601 pixels; its table retained
916 pixels of content behind local horizontal scrolling, while the prose block
grew from 118 to 144 pixels as it wrapped. Native scaling and
assistive-technology acceptance remain open.

For high-severity FCUX-001, the Windows 11 guest also ran a real NTFS recovery
review in an isolated `--data` instance. The fixture used FileCat Core's journal
writer to seed an interrupted filtered move with `*.jpg`, read-back
verification, and Queue intent. Startup surfaced one interrupted operation;
Continue move displayed the original source/destination mapping and options,
plus recovery's conservative Skip rule. Enter activated the default Cancel,
leaving the journal and both source files intact. On a second review, Continue
move transferred `photo.jpg` to the destination and left excluded `notes.txt`
at the source. This verifies the native filtered-move path on NTFS;
supported nonlocal-provider cases remain open.

A further guest run interrupted an active, rate-limited Core move by
terminating only its test process after copied bytes were recorded. The
journal remained recoverable, the source files survived and the destination
file was unpublished. An isolated FileCat desktop instance surfaced the
interrupted job and identified one partial staging file for cleanup. Its
review preserved the `*.jpg` filter, read-back verification, rate choice,
Queue start and Skip recovery policy. Continue completed the 8 MiB JPG move,
kept the TXT at source and removed the partial staging file. A separate
guest fixture then killed the FileCat desktop process itself while a 32 MiB
copy was active. A nonzero marker in retained staging proved copied content,
not merely preallocation; the journal preserved the 64 KiB/s choice and the
target remained unpublished. Relaunch displayed the partial-file cleanup
and conservative Skip policy. Continue reproduced the complete source bytes,
kept the copy source and cleared staging. Supported nonlocal-provider cases
remain open.

Two further fresh Windows 11 VM fixtures exercised the actual FileCat desktop
copy from guest-local C: to a disposable NTFS VHD mounted as R:. The VM runner
created a 32 MiB JPG with a known marker, excluded an adjacent TXT file, set a
64 KiB/s speed limit, then terminated only its path-verified isolated FileCat
process after journaled staging contained copied bytes. Both interrupted
journals retained the rate and recoverable intent; neither target file had
been published. After relaunch, the recovery review showed the destination,
Skip policy and partial-file cleanup, while its full-review disclosure showed
the source mapping and retained rate. Continue reproduced every source byte
at R:, kept the C: source and excluded TXT, and removed staging. The VM
provisioning script then detached and deleted its own VHD; R: and that VHD
were verified absent. The runner and guarded provisioning script are tracked
under `tests/FileCat.NativeAcceptance`. This closes the native Windows
cross-volume copy case; nonlocal-provider cases remain open.

After the report that the recovery dialog still read as a wall of text, the
review now gives its action a distinct selectable emphasis, makes the conflict
choice stand out, shows consequential effects as bullets, and separates the
expanded full review into visible sections without dropping any original
wording. The focused interrupted-transfer UI cases (14) and small-window
dialog viewport cases (27) pass with 16 workers. Scaled Windows and spoken
native acceptance of the revised hierarchy are still open. The revised
Git-built dialog was also deployed to the isolated VM: its native
`legacy-highlight` check passed the action, one-root mapping, bulleted effects,
safe initial Cancel focus, full-review disclosure and pending-approval state.
A VM-only screenshot was inspected to confirm the visual grouping. A follow-up
made field keys and the entire source/destination path distinct, with the final
filename additionally bold. The path uses the theme's link color, including in
the generic multiline confirmation body; selectable text and its native UIA
name appear exactly once. Resizing substantial reviews, confirmations and
alerts now reflows their content across the available width instead of keeping
an old 620-DIP cap. The same resize control was added to Undo and finished-
history review, operation issue details, Registry import review and recent
notifications. Registry import shows its scope and file on separate emphasized
lines. The affected focused tests pass: 31 viewport, 14 interrupted-transfer,
8 dialog-focus, 38 interrupted-rename, 9 Undo UI (one filesystem skip), and 5
notification cases, all with 16 workers. The rebuilt VM's `legacy-highlight`
check passed with safe initial Cancel focus and no duplicated lines; its
`legacy-resize` check measured the destination text expanding from 635 to 855
pixels wide and reflowing from 56 to 19 pixels tall, then restoring the dialog
while approval remained pending. Native scaled and spoken review of these
dialogs, and native inspection of the additional custom review windows, remain
open.
The Undo review now wraps the full inverse paths and irreversible-effect rows
inside its virtualized lists; finished-history rows use the same bounded
wrapping presentation. Registry import's read-only change preview also wraps
long Registry paths and reflows when the review is resized. Focused App checks
pass 43 cases with one filesystem skip, including real multi-line Undo rows,
width-driven reflow, and Registry preview line-count reflow. The Win11 guest
then completed an isolated FileCat Cut/Move/Undo journey: the native inverse
row was 662 × 98 px initially and 863 × 42 px after expansion, with the full
source and target in its UI Automation name. Cancel kept the move; explicit
approval restored the source bytes, removed the target and preserved the
unrelated file. This covers the simple Windows Move inverse and the reported
Undo path wrapping regression. A second fresh VM fixture verified that the
finished inverse remains selectable in Operations: Undo report opened, and
Copy all published all 447 characters with both exact paths and the restored
outcome. Other inverse kinds, scaled and spoken review, and restart ownership
remain open.
Another fresh guest fixture completed the Windows Recycle Bin inverse. The
native F8 review named the Recycle Bin while both source files still existed;
approval removed only the selected file. Ctrl+Z named its exact original path,
Cancel left it recycled, and explicit approval restored its original bytes.
The unrelated file and destination stayed unchanged. The retained Undo report
was reopened from Operations and Copy all produced all 255 characters with
the restored path and outcome. This closes the native Windows simple Recycle
Bin inverse case; other inverse kinds, restart/interruption ownership, scaled
and spoken review remain open.
A further fresh guest fixture completed the visible-only bulk rename followed
by Undo. The native inverse row named both complete paths and occupied 98 px
over multiple lines. Cancel preserved the reviewed new name and bytes;
approval restored the exact original name and content while leaving the other
source file and destination unchanged. This closes the simple Windows rename
inverse case; multi-item partial outcomes and interruption remain open.
The Undo action row now uses the same themed selectable review text as other
long safety decisions. Complete quoted absolute paths receive the link color;
directory segments remain emphasized alongside the final name. The wrapped
row retains its exact accessible wording. The focused 63-case App review,
Undo, archive-warning and report run passed 62 with one filesystem skip using
16 workers. A fresh Win11 guest screenshot confirmed both complete paths are
visibly blue across wrapped lines, while the primary action and Cancel remain
clear. The VM Move → Cancel → approved Undo and retained-report journey passed
again with the styled row.
At `b9457bad`, the complete Windows Release solution build passed with zero
warnings and errors. The 16-worker solution test run exited successfully:
4,429 Core passed/150 platform skips, 2,369 App passed/63 skips, 2,370 Remote
passed/156 skips, and 423 Windows platform passed/46 skips, with zero failures
in every project. Cross-platform CI for this code revision is still pending; these
headless results do not close the native scaling and assistive-technology gates.
A separate fresh Win11 VM fixture passed the created-empty-folder inverse:
FileCat's F7 created the folder in the destination listing, the Undo review
exposed its complete path in a 662 × 60 px wrapped row, Cancel kept the folder,
and approval removed it. Both unrelated source files retained their exact
bytes. The native runner first caught an active-panel mistake in its own F7
gesture; focusing the destination list corrected it before the successful
rerun.
A separate completed Move fixture checked the other resizable long-path
review: Clear finished history. The native history row reflowed from 662 × 60
px to 936 × 42 px when widened, Keep history remained reachable, and Cancel
preserved the moved file and exact bytes. Narrow/scaled and spoken checks for
this review remain open.
A broader local App attempt on the preceding head exposed an assertion that
read `TextBlock.Text` after the issue detail became styled inlines. The test
now reads the rendered runs and passes with the focused report checks. That
complete attempt was stopped after its test host stopped making progress, so
it is not counted as a complete pass; cross-platform CI remains the full gate.
The preceding Ubuntu CI run exposed four archive-warning test cases where
`SelectableTextBlock` had plain `Text` and an empty inline collection. The
assertion now reads actual styled runs when present and plain text otherwise;
all 21 focused archive-warning and operation-report cases pass on Windows.
The preceding `b0c2023a` cross-volume/review batch passed producer policy and
all Windows, Ubuntu, macOS and Windows ARM64 CI jobs. The expanded dialog batch
awaits its own final-head CI run.

The Windows 11 guest also completed FCUX-069 against Explorer rather than a
simulated clipboard. FileCat Cut published an exact one-file shell drop;
Explorer Copy of that same path replaced it. The VM checked the native
`CF_HDROP` manifest, then FileCat Paste copied the file into the right-hand
destination while preserving the original and matching its bytes. This
journey exposed an Avalonia Windows conversion that advertised two file items
for Explorer's one-file drop but resolved one. FileCat now reconciles that
specific extra-wrapper case against the complete, bounded native manifest,
the resolved path and the clipboard sequence. Any conflicting or wider
incomplete set is refused. A second fresh VM fixture Cut both `a.txt` and
`b.txt` in FileCat, replaced that clipboard with Explorer Copy of only
`a.txt`, and confirmed Paste copied only `a.txt` while preserving both
originals. Cross-platform native clipboard exchange and Windows
preferred/performed effects remain open.

**Earlier implementation record:** FCUX-068 shows queue movement only for queued jobs, disables the
first queued job with a reason and preserves synchronization step order; 51 related
App and 28 job-engine Core cases pass, with one Core platform skip. FCUX-067 keeps
support actions within About, names their exact folder targets and opens them
in new tabs; 46 related App cases pass. FCUX-066
rejects a last-tab close before touching recent history; 30 related App cases
pass. FCUX-065 preserves valid one-panel workspaces
on reload; 43 related App cases pass. FCUX-064 restores saved normal window size and
position within an available display, including after a maximized session; 33
related App cases pass. FCUX-063 connects subordinate Settings controls to their
parents and makes column-editor actions reflect the draft; 58 related App cases
pass with one platform skip. FCUX-062 gives colliding tabs short location
qualifiers and distinct lock-mode presentation; 39 related App cases pass. FCUX-061's
clipped-cell hover and keyboard/context-menu inspection pass 59 combined list
and profile cases. FCUX-060's wider viewer/editor rerun passed 342 cases with 14
platform skips; its complete App rerun passed 2,320 cases with 63 platform skips
and zero failures. The complete App run on the pinned FCUX-059 snapshot passed
2,317 cases with 63 platform skips and zero
failures; its related viewer/comparison/editor/Settings/list run passed 365 cases
with two skips. The complete Core run passed 4,427 cases with 150 platform skips.
Native provider,
Shell, page-engine, scaling and assistive-technology acceptance remain open.
FCUX-055 separates Fit, 100% and custom picture zoom states.
Twelve focused and 74 related App cases pass, with 12 platform skips in the
related set. This supplies the zoom state needed for FCUX-052 snapshot refresh;
native scaling and assistive-technology acceptance remain open. FCUX-051 reads
the Windows client-area animation setting,
macOS accessibility reduced motion, and GNOME's animation setting when its
desktop tools are available. A three-second active-theme probe updates motion
without reopening FileCat. Startup and reactivation hold the effect still until
the preference read finishes. The explicit setting and inactive-window pause
always win; unsupported desktops use FileCat's own setting. Thirty-two related
App cases pass, including simulated live preference changes and a Windows API
read. Native macOS/GNOME preference toggles and screen-reader acceptance remain
open. FCUX-050 keeps Psychedelic motion in the decorative backdrop.
Menus and work surfaces now have opaque colors, focus/grid brushes stay fixed,
and no content-sampling glitch overlay sits above files or viewers. The visual
clock freezes when effects are disabled. Three focused and 54 related App cases
pass; native color-cycle, scaling and reduced-motion acceptance remain open.
FCUX-049's two focused and 116 related App cases pass; native high-contrast
and theme-transition acceptance remain open. FCUX-048's five focused and 63 related App tests pass
with one platform-specific skip; native pointer, clipboard and provider
acceptance remain open. UXBUG-009's
focused and 44 related App cases pass. The first complete App run after
UXBUG-009 recorded 2,246 passes, 63 skips and one assertion taken before two
interrupted-review button bindings settled. That test passes in isolation; its
accessibility assertion now waits for Loaded bindings and still fails if names
remain empty. The 59-case related rerun passes. The complete App rerun through
FCUX-049 passes with 2,254 passes, 63 skips and no failures. Subsequent
FCUX-050/051/055 changes pass a complete App rerun with 2,276 passes, 63 skips
and no failures. The complete rerun through FCUX-052 passes 2,285 cases with
63 skips and no failures; the complete rerun through FCUX-054 passes 2,296
cases with 63 skips and no failures.
UXBUG-008's
240 related App and 111 related Core cases pass, and the full Core Release suite
passes with 16 workers (4,427 passed, 150 skipped). Native viewer acceptance
and an exact Page-mode Find total remain open. UXBUG-007's
72 related App cases and complete App Release suite pass (2,242 passed,
63 skipped); native pointer/scaling acceptance remains open. UXBUG-005/006
focused regressions and FCUX-047's complete App run also pass.
FCUX-036 native acceptance remains open. Advanced text import/export now shares
bounded strict-UTF-8 reading and atomic writing across saved filters, user
commands and associations; 30 related App cases pass with one platform skip.
The editor program picker, token
insertion and a no-write invocation preview are implemented with focused
regressions. The saved-filter list/detail editor and Advanced text route are now
implemented. Association ordering, per-field validation, an Advanced text route,
and a nonexecuting first-match preview are implemented with focused regressions.
F9 user commands have a structured nested-menu editor and a lossless Advanced
route; Settings search navigates among labels and explanations across tabs.
The Keyboard tab opens the searchable reference and captures shortcut changes
inside the Settings draft. Command and association rows offer argument tokens
and nonexecuting previews. Native interaction acceptance remains open. UXBUG-001,
the reported Markdown Page table-width bug, is fixed: tables use available viewer width and scroll within
their own keyboard-focusable region on narrow windows. The [session workflow](UX_UI_SESSION_WORKFLOW.md)
records the wide/narrow geometry check and remaining native acceptance.

Markdown Page search is implemented as UXBUG-002 with the text viewer's
find/next/previous/stop controls. Windows WebView2 and headless viewer tests
pass; native Linux and macOS search acceptance remains open in the
[session workflow](UX_UI_SESSION_WORKFLOW.md).

FCUX-037 toolbar commands now use the same availability and refusal reasons as
menus, the function-key bar and command search. Active listing changes are
coalesced, while tab-history changes update Back and Forward. Disabled buttons
retain a hoverable explanation and accessible help text. Focused toolbar and
related accessibility regressions pass; native screen-reader and large-listing
latency acceptance remains open.

FCUX-038 bulk rename now reviews the full accepted row set through a virtualized
list, with Problems and Changed filters, path search, next-problem navigation and
full source/target details. A collision beyond row 2,000 is directly reachable.
FCUX-039 moves pure expansion and device-scheduled existence checks off the UI
thread. A changed rule or scope retires its previous generation immediately;
the final job admission rechecks destinations.
FCUX-040 rejects invalid or out-of-range counter fields without changing their
text. Inline validation keeps Rename unavailable, while valid zero and negative
steps retain their documented meaning and arithmetic overflow blocks affected
rows.

FCUX-041 keeps destination syntax and provider-capability errors beside the
field without closing the transfer form. Start and Queue share one asynchronous
validation result, so the displayed folder/new-name mapping is the submitted
mapping. Slow filesystem probes use the device scheduler; editing and Cancel
remain responsive. Focused tests cover retained options, both buttons, mapping,
stale probes and cancellation. Native network-path and assistive-technology
acceptance remain open.

UXBUG-003 addresses the reported PE Info scrollbar-thumb freeze. Rapid thumb
positions now coalesce to the latest location on a bounded 32 ms timer, rather
than synchronously shaping every intermediate viewport. The report's line count
is computed when the report changes instead of on each scroll. Portable tests
exercise repeated value changes and real thumb dragging down and back up; a
local headless check also passed with the supplied `SignalSetup.exe`. The user
confirmed the Windows desktop scrollbar drag on that EXE now works.

FCUX-042 keeps the operations drawer resizable without losing the file panels,
scrolls recovery notices and shows actions for one selected interruption. At
640 × 420, a complete operation row and Open report remain visible. The speed
graph expands on demand. UXBUG-004 removes the old fixed prose width, so
Markdown text and tables both use the added space when the Page window grows.
A rendered browser check measured both at 505 px narrow and 1,441 px wide.

FCUX-043 shows the active recovery phase and disables conflicting actions for
that journal. Stop closes the active approval or new-transfer form and retires
preparation without submitting a job. After approval, cleanup stops at a safe
step; its completed effects are retained in a saved report with the journal
open. A newly admitted job keeps its own lifecycle even if Stop arrives at its
admission callback. Focused tests cover held reads, approval, the transfer form,
one completed deletion and admission.

FCUX-044 leaves Find without a local scope when opened from SFTP, FTP, archive,
or other unsupported views. It explains the provider limit and asks for an
explicit local folder, drive set, or home selection before search. A saved
search that supplies local folders names itself as the source of that choice;
result-set snapshots keep their own scope. Registry retains its separate Find.

FCUX-045 keeps Find's completion and qualification lines visible below the
actions and places the changing folder path in secondary, elidable progress.
No-match, stopped, ignored, inaccessible, warning, and regex-timeout outcomes
have distinct summaries. An empty result view names the captured criteria and
scope and offers Edit criteria and Review search log. Hiding the last result
reports an empty list without claiming the search never matched.

## Changes and verification map

| Audit finding | Implemented behavior | Regression coverage | Remaining acceptance |
| --- | --- | --- | --- |
| FCUX-001 — interrupted transfer intent | Journal a versioned, bounded snapshot of the destination, source mappings, resolved filter definitions, layout/name, verification, preservation, rate and queue choices. Continue shows that scope and uses conservative Skip. Legacy/incomplete records require a newly reviewed transfer. Existing source/staging/content guards remain in force. Filtered same-volume folder moves cannot bypass traversal through a rename shortcut. | `RecoverableTransferIntentTests`, `RecoveryTransferConflictTests`, `InterruptedTransferIntentUiTests`, existing `Interrupted*` tests, `MaskTests`. A Windows 11 guest resumed a journal-seeded filtered NTFS move, a genuinely interrupted Core worker, and an actually terminated FileCat desktop process. The desktop copy's retained staging contained a nonzero content marker, its journal retained the rate, and Continue reproduced all 32 MiB while cleaning staging. Two fresh guest-local C:-to-R: NTFS copies passed the same real interruption and recovery checks; the disposable VHD was removed afterward. A guest legacy journal refused equivalent continuation; Cancel retained its source/journal and an explicitly reviewed new copy used the chosen mapping. | Supported nonlocal-provider behavior remains open. Large or unsupported records deliberately cannot promise equivalent continuation. |
| FCUX-069 — clipboard operation authority | Cut belongs to the current exact FileCat clipboard publication and complete ordinal resource identities. External/unverifiable publications import as Copy. Running/completed/partial moves cannot replay the original Cut; later publications retain their own lifecycle. On Windows, an Explorer file drop that Avalonia advertises with one extra wrapper is accepted only after its converted paths agree with a bounded native `CF_HDROP` list and the clipboard sequence stays stable. | `ClipboardIntentTests`: controlled asynchronous publications, incomplete/mismatched native manifests, changing sequence, owned-Cut refusal and actual source/destination manifests. A Windows 11 VM completed both Cut A → Explorer Copy A and Cut A+B → Explorer Copy A, then FileCat Paste; exact native manifests, source survival, selected destination bytes and exclusion of B passed. | Native clipboard exchange on macOS and Linux and preferred/performed Move effects remain open. |
| FCUX-070 — delayed Paste destination | Capture destination and invoking tab before clipboard reads. File Paste uses that location or explains refusal; tab closure cancels it. Text navigation also respects newer navigation/Paste intent, including pending provider preparation. | `ClipboardIntentTests`: held reads, navigation, panel/tab movement, closure, source survival and actual request destinations | Native clipboard/backend timing. |
| FCUX-002 — range anchor identity | Bind range anchors to the listing store and item identity, resolve through the current view, and retire unavailable/foreign anchors. Reset gestures on focus/window deactivation. | `FileListSelectionTests`: real key/pointer events across sort, filter, tab and focus transitions. Native Win11 sort, filter, refresh, tab, Ctrl-click, Shift+arrows, held-Shift window deactivation and command-line focus-loss journeys passed on isolated fixtures; sort/Shift-click and held-Shift/focus-loss also passed at 720 × 540. | Other narrow routes, scaled input and non-Windows platform focus routing. |
| FCUX-003 — hidden marks in rename | Show hidden count and explicit unchecked inclusion control. Capture both scopes, regenerate counter/name mapping when scope changes, and reject stale external-editor results. Preserve marks and the original operation origin across tab navigation. | `BulkRenameScopeTests`, existing `BulkRenameDialogTests`: inclusion/exclusion, counters, navigation, actual filesystem results. The resizable review keeps common fields and complete preview visible while advanced controls expand on demand. Native Windows excluded/included-hidden rename and both 760 × 560 scope reviews passed with exact files and bytes. A requested 640 × 420 owner retained the scope choice, mask and approval controls; expansion kept both mappings visible and submitted both exact file bytes. | Scaled display, other small layouts, spoken/keyboard review and stale external-editor results; the separate preview size/validation findings FCUX-038/039 and shared small-dialog layout FCUX-011 are covered below. |
| FCUX-004 — background decisions/focus | Pending decisions remain in Operations with an explicit Review action. They cannot stack over unfinished user input. Skip is the safe initial operation-decision action; consequential generic confirmations focus Cancel. Recovery move, cleanup, and newly selected replacement policies use that safe default. Dialog focus/Enter callbacks belong to the current session; canceled/resolved decisions retire their dialog. Find's queued startup focus also yields to an active dialog, newer focus, deactivation or closure. | `BackgroundDecisionTests`, `DialogFocusOwnershipTests`, `FindInitialFocusTests`, recovery confirmation tests, existing `OverlayDialogLifetimeTests`, `FindRemainingScopeTests`, operation scope/center tests | Native focus, automation and scaled-layout acceptance. |
| FCUX-005 — synchronization identity | Keep separate complete, selectable Source and Target fields, full accessible identities, and target-specific Mirror wording synchronized with direction. | `SyncEndpointTests`: same-name endpoints, both directions, long paths and actual submit direction | Native layout at supported scaling; S2 plan-choice/group-cancellation lifecycle remains separate. |
| FCUX-018 — tracked Undo | Reserve one immutable inverse review atomically. Filesystem inverses become typed Undo jobs through normal scheduling/activity/close handling, with complete retained per-action reports, irreversible remainder, safe cancellation boundaries and unresolved rename journals. Registry inverses share the exclusive approval boundary and retain their guarded Registry job/issue path. Original-history removal cannot orphan a running inverse. The review's selectable full-path rows wrap, reflow and emphasize entire pathnames with the theme color. | `UndoLifecycleTests`, `UndoSafetyTests`, `UndoLifecycleUiTests`, existing bulk rename/link/recovery tests. Native Win11 FileCat Move → Cancel → approved Undo restored exact source bytes and identity, with a reflowing full-path UIA row. Another fixture reopened the inverse's retained report and copied its complete text. A third verified Recycle Bin review, Cancel, byte-for-byte restoration and complete report copy. A fourth verified Rename Undo with full path mapping, Cancel and exact restoration. Fresh fixtures verified created-empty-folder and zero-byte-file inverses with wrapped full paths, Cancel and safe removal; the folder case passed again after the shared runner extension. A changed created-file fixture was kept with exact bytes and a retained report naming the full path and reason. A native hard-link inverse kept shared data on Cancel and removed only the created name on approval, with both full paths visible. A radio-group regression test prevents stale symbolic-link preview after choosing Hard link. A changed-original native hard-link inverse kept both names and distinct bytes with a retained report naming the full path and identity reason. Native symbolic-link inverse removed only the link; a changed-link fixture kept both distinct files with a retained one-kept report and full path. Native junction inverse removed only the junction; a replacement-folder fixture kept both distinct directories and reported one kept action with full path. The complete Release App suite passed 2,370 cases, 63 platform skips, 0 failures with 16 workers. | Held-inverse close/restart ownership, dependent rename interruption and journal recovery, failures beyond report row 30, mixed or incomplete outcomes, scaling and spoken checks. |
| FCUX-006 — Settings data preservation | Preserve untouched structured tool definitions. Share a lossless argument/field grammar with explicit empty tokens and quote errors. Keep all launch properties, exclusion masks, comment-marker names and complete submenu structures. Invalid edits stay in the same draft with field/line feedback; protected profiles are inspectable without edits or theme side effects. | `ToolTextRoundTripTests`, `ToolAssociationTests`, user command tests, `SettingsPreservationTests`, existing `SettingsDialogTests`; tests inspect the actual non-executing launch plan and persisted settings | A structured tool editor (FCUX-036) remains a separate S2 improvement. |
| FCUX-007 — Previous text search | Search the full declared preceding range using bounded reverse windows, encoding-aware context/overlap and strict progress. Continuation belongs to the query/source/encoding/mode and active content surface; a visible Stop action cancels owned work. | `ContentSearchBackwardTests`, `ViewerSearchScopeTests`, viewer/editor search lifetime tests: distant/chunk-boundary matches, UTF-16, malformed sequences, overlaps and caller ownership; native 13 MiB UTF-8 and UTF-16LE Previous journeys passed on Windows | Native editor, Stop, changed-query, provider cancellation and non-Windows input routing. |
| FCUX-008 — checksums | Review explicit whole-file or selected-byte scope, including one byte; retain complete selectable hashes and actual/expected counts. Copy is explicit. Stop, short reads, failures and revision evidence remain visible without overwriting the clipboard. | `ViewerChecksumScopeTests`, existing checksum lifetime/revision tests: scope, immutable request, narrow results, explicit Copy, cancellation, failures and revision changes; native one-byte/Copy journey passed twice and at 600 × 420, and deliberate whole-file/Copy, 768 MiB Stop and stale-scope refusal journeys passed on Windows | Native incomplete source and mid-read mutation, other narrow states, scaled layout and non-Windows clipboard checks; sources without strong revision evidence remain explicitly qualified. |
| FCUX-009 — concealed comparison text | Replace trimmed primary cells with a virtualized aligned text surface, synchronized horizontal scrolling and optional wrapping with matched pair heights. Difference navigation reveals the actual changed glyph. Complete lines up to the explicit shaping budget of 16,384 UTF-16 code units appear in the primary surface; exceptional lines have an explicit full-line action into paired, selectable bounded parts. Retire source associations, queued reveal ownership and text layouts on replacement/close. Keep exact equality and comparison computation independent. | `CompareTextViewTests`, `CompareLineDetailsTests`, `ComparisonReplacementRetirementTests`, existing comparison equality/source-change/navigation/lifetime cases. The width/length matrix checks the actual glyph viewport, not just source strings. Windows VM `compare-native` verifies both complete 2,004/2,005-character source values, second-difference navigation, source-faithful Copy and a wrapped selected row intersecting the viewport; a captured desktop view shows both differing suffixes in the primary surface. | Native font fallback, pointer selection, spoken automation and macOS/Linux acceptance remain open. Full-document selection/search remains a larger content-surface target; complete source lines remain inspectable and selectable through their detail action. |
| FCUX-010 — file list accessibility | Provide bounded virtual item peers with stable identity, focus versus mark semantics, selection/action/scroll/bounds support and coherent cached events. Give each cached item a lightweight attached control owner so Avalonia's Windows bridge can convert its logical rectangle to screen coordinates; detach that owner when its peer retires. Provide a complete paged marked-item review, including hidden/offscreen marks and original locations for result-set items, and avoid synchronous metadata I/O in accessibility queries. | `FileListAutomationTests`, `FileListSelectionTests`, `MarkPagingTests`, `MetadataPeekTests`, updated `AccessibilityTests`; native Windows `accessibility-list` verifies nonzero source-row rectangles and separate mark/focus states on a live two-file fixture. | Windows UI Automation geometry and mark/focus inspection pass in the Win11 VM. Native screen-reader speech, macOS/Linux bridge behavior and large-listing latency remain open. Large selection uses the explicitly explained paged review rather than constructing unbounded native peers. |

### Related corrections included

FCUX-034 validation-session preservation and FCUX-035 protected-profile wording/editability are addressed in Settings because they are part of the safe-save path. The clipboard and drag branches of FCUX-071 both refuse incomplete mixed-provider export. The S2 findings have their own coverage below.

## S2 continuation

| Audit finding | Implemented behavior | Regression coverage and remaining acceptance |
| --- | --- | --- |
| FCUX-011 — dialog reachability | Reserve header/footer space and scroll the body within the actual host client area. Adapt forms, Settings pages, bulk-rename fields and secondary actions without replacing controls or drafts. Reveal the exact focused caret through nested scrolling. Keep complete proposed conflict names in selectable body text, escaping invisible characters through the existing filename display contract. | 21 `DialogViewportTests` at the 640 × 420 DIP minimum and three font sizes; all 173 affected dialog, Settings, operation-scope, focus and lifetime cases pass together. Native scaling/font/input/assistive-technology checks remain open. |
| FCUX-012 — viewer geometry and selection | Store selection as source byte boundaries with decoded-source/display mapping. Preserve that identity through wrapping, resize and font changes. Give Text/Info and Hex horizontal access and reveal the current source glyph/byte on both axes. Explicitly retire incompatible source/encoding selections. Keep source segments, highlighting and text layout work bounded. | `TextSourceMappingTests`, `ViewerGeometryAndSemanticsTests`, plus existing wrapping/search/checksum/lifetime cases. Tests cover supported minimum/wide widths, 13/18/24/32 fonts, wide byte offsets, reflow, source replacement and resource ownership. Native font and input acceptance remains open. |
| FCUX-013 — theme state contrast | Give destructive buttons a stable danger fill/text pair in normal, hover and pressed states. Keep hidden/system/unavailable names readable on the actual panel while retaining their dimmed icon cue. Draw selected and search-hit glyphs in paired foregrounds in Text and Hex views; derive changed/diff fills and text from each palette, including current difference variants. Binary differences retain underlines and the current outline; text comparison already has changed-span underlines and +/− line signs. | `ThemePickerTests` checks all seven palettes plus the current System resolution, 4.5:1 essential text pairs, 3:1 focus boundary and live danger button states. `SelectedGlyphContrastTests` checks committed selected/search glyph layouts, hex brush resolution and the list's actionable dim brush. The 124-case Windows theme/viewer/comparison/list regression selection passes. Native rendered-pixel, grayscale, scaling and assistive-technology acceptance remain open. |
| FCUX-014 — safe extension cells | Apply the safe filename display policy to the actual Name and split Extension cell presentation, result-set Folder/Kind/Details cells, and drive labels in the list/status. Hovering a name with hidden characters gives the complete safe filename and a spoken description; focus status and the accessible row description retain that evidence. Raw entry names, extension sort keys and operation identities are unchanged. | `FileListNamePresentationTests` exercises Details, Full, Brief and custom cell profiles, provider-derived cells, layout controls, valid Unicode, the actual row tooltip/focus status, accessible descriptions and raw identity. The 71-case Windows list/profile/automation/accessibility/formatter selection passes. Native glyph rendering and screen-reader speech remain open. |
| FCUX-071 — complete selection export | The existing clipboard preflight preserves the previous publication on refusal. Drag now captures the complete marked scope, refuses mixed local/provider selections before transfer, stages supported archive-only files within the existing 100-item/64-MiB budget, and converts every requested path before beginning a native drag. Failed conversion, changed source tab, cancellation and unexpected provider errors retire unhanded staging; accepted transfers retain staged files for the existing sweep. Refusals state the full selected count and offer F5/F6. | `DragExportTests` exercises actual local/ZIP contents, origin marks, hidden marks, complete mixed refusal, unsupported sources, conversion failures, cancellation, handoff and the 5,000-item cap. `DragStagingTests` exercises staging quotas/refusals and cleanup, including unexpected provider failure. Native interapplication drag effects, cursor feedback and platform clipboard interoperability remain open. |
| FCUX-075 — truthful drop feedback | Hover offers Copy or Move only for a ready filesystem target with transfer capability. Drop rechecks that same tab/location, rejects incomplete or nonlocal file payloads as a whole, and verifies the folder still exists before activating the panel or opening review. Unsupported and changed targets explain how to choose a local destination. An accepted review uses the captured destination even if its panel later navigates. | `DropTargetAdmissionTests` covers local effects, archive/Registry/working-set refusal, failed/disconnected local listing, changed or vanished target without panel activation, complete payload admission, and an actual reviewed copy to the captured folder. Native drag cursor/effect and cross-application interoperation remain open. |
| FCUX-015 — live selected-job issues | Bind the drawer to a stable observable issue list. The selected job's new retained issues append in 256-item UI batches, while its selection and scroll remain in place. Job change dispatches coalesce, and progress polling checks only issue count. Opening or selecting a job reconciles its current retained report; completion catches up with issues added between dispatches. | `OperationCenterTests` observes the actual bound ListBox through two late conflict issues, preserved issue selection, no collection reset on progress refresh, final count, reopening, a 270-issue bounded initial load, and scroll retention during append. `JobIssueSliceTests` checks exact bounded thread-safe slices. Native rendering and assistive-technology acceptance remain open. |
| FCUX-016 — truthful operation status | A pause request reads “Pausing — waiting for a safe point” until the worker acknowledges it; its action reads “Keep running” and the taskbar stays in running state until then. Canceled, failed and interrupted jobs keep their settled fraction below 100%, show outcome and unknown/remainder counts, and omit a success-like percentage. Completed-with-issues keeps outcome counts without an unqualified 100% label. Planned bytes are labelled as accounted for; actual copied/read data remains separate, so skips and same-volume moves do not imply transfer speed. | `JobProgressViewTests` covers deterministic pause/acknowledgement, taskbar mapping, all stopped states, incomplete discovery, completed-with-issues, same-volume logical credit and the existing verified-copy ETA/progress route. Existing `JobDataPaceTests` protects actual-data pacing. Native taskbar, long-running worker and scaled-layout acceptance remain open. |
| FCUX-017 — complete ordinary operation evidence | A selected issue opens full selectable source/target paths and reason, exact Copy controls and local Reveal where the path is known to be local and still exists. Copy report includes every retained detail up to a 4 MiB clipboard budget; Save report streams all retained details through a same-folder temporary file and atomic replacement. Independent severity/total counters keep counting beyond the 10,000-detail cap, with explicit omissions. Transfer rows distinguish conflict-policy Skip, failed copy, published copy with source kept, and source deletion refusal; local, nonlocal and unknown path kinds remain distinct. | `OperationReportTests`, `JobIssueSliceTests`, `TruthfulOutcomeTests` and existing `OperationCenterTests`: exact hidden-character paths/reasons, modal copy and report, cancellation preserving an old saved report, 10,005-issue cap, and actual transfer effects. Native clipboard, reveal, large-report layout and assistive-technology acceptance remain open. |
| FCUX-019 — history and Undo disclosure | Drawer actions name finished-history retirement and explain that reports and unused Undo steps leave this session. A confirmation lists affected rows when an unused/active Undo or report with problems is present; Cancel keeps them. Clearance applies only to the reviewed snapshot, so jobs finishing during the dialog survive. Active inverse work remains owned. Global Undo names the most recent undoable action and warns when newer finished actions are outside its scope; the existing inverse review keeps full paths and partial-restoration warnings. | `UndoLifecycleUiTests` exercises cancel/approve, late completion, routine no-prompt removal, global Undo after a newer copy, and UI removal during an active inverse. `OperationCenterTests` covers a failed report. Existing 18 `OperationHistoryRetirementTests` and 38 Core retirement/Undo ownership cases pass. Native Win11 long-path history row reflow and Keep history cancellation passed. Narrow/scaled, keyboard and assistive-technology acceptance remain open. |
| FCUX-020 — interrupted recovery review | “Hide for now” leaves the journal and files intact and the notice returns on startup. “Mark reviewed…” explicitly stops future recovery tracking after a deliberate confirmation, especially for temporary rename intents, without changing files. A full retained-path report can be inspected/copied/saved before closure; a bounded durable reviewed report is written before any journal end record. Cleanup and continued/new transfers also retain deleted, kept, unreviewed, unresolved and submitted-scope evidence. Closed records are inspectable without mutation controls; review-limit and missing-source counts are explicit. | `InterruptedRenameTests`, `InterruptedCopyCleanupTests`, `InterruptedSource*Tests`, `InterruptedDemandTests`, `RecoveryReviewStoreTests`: hide/restart, cancel/approve, duplicate admission, storage refusal, full report copy, kept partials, unresolved rename, 200-record cap, manifest scope and existing cleanup/continuation guards. Native process-interruption, accessibility and scaled-layout acceptance remain open. |
| FCUX-021 — synchronization review and lifecycle | Rebuilding a plan retains only explicit choices for the same comparison entry, direction and action, and announces the rebuild. Permanent-deletion consent names the target and clears when direction, mode or included removals change. Each submission atomically admits an independent group with captured endpoints, review revision, child IDs and aggregate outcome. Operations offers Stop this synchronization as well as scoped Cancel this step. Removals wait for all transfers; an unexpected failure or cancellation holds them for explicit review, complete pending-path inspection, continuation or Skip. Expected policy skips do not hold removals. Completed work and ordinary compared-target guards remain visible. | `SyncReviewStateTests`, `SyncGroupTests`, `SyncGroupUiTests` cover option changes, consent, identical-endpoint plans, delayed transfer/Stop, child cancellation, failure/review, expected Skip, removal-only plans, unrelated work and UI scope. A 210-case affected Core selection has 209 passed and one platform skip; all 42 affected App cases pass. Native layout, accessibility and real interruption acceptance remain open. |
| FCUX-022 — current navigation intent | Each tab invalidates and cancels its previous preparation or SFTP home connection when a newer destination, Back, Forward, Escape or closure takes ownership. A prepared result commits under its original generation without starting a second intent. Delayed success and failure callbacks cannot change a newer location, banner, marks, focus or history; cancellation and closure suppress queued connection errors. Current failures explain the error while keeping the listing. | `NavigationIntentTests` uses held provider and SFTP connector callbacks, including a connector that ignores cancellation, a second pending destination, Back/Forward, Escape and tab closure. The 56-case related App selection passes 55 with one platform skip; 44 navigation/clipboard/link cases pass 43 with one platform skip after the final ownership guard. Native network mounts, keyboard and connection-pool acceptance remain open. |
| FCUX-023 — current filesystem capacity | Resolve the displayed directory's final path, query the containing Unix mount or Windows volume mount point/UNC share, and keep that identity independent of the IO scheduler key. Explicit Refresh and completed mutations force a recheck; active-tab expiry refreshes external changes. Coalesce slow checks, discard late results and label pending, unavailable or expired data. | `FileSystemCapacityTests` covers mount boundaries, share roots and a real local capacity query; `CapacityFeedbackTests` and `PanelFreeSpaceTests` cover distinct location identities, copy/delete refresh, expiry/activation, slow coalescing, failed and late results, and rendered panel status. Focused Core 7/7 and App 7/7 pass. Native mount/share, disconnection and screen-reader acceptance remain open. |
| FCUX-024 — safe workspace Save as | Suggest an unused name and validate filenames without lossy sanitization. A matching saved name, including a case-only alias on Windows, receives an explicit replacement review with saved/current panel and tab counts. Cancel leaves the main and backup bytes unchanged. New records use atomic no-overwrite publication; approved replacement checks the reviewed digest immediately before publication and retains atomic backup behavior. Failed publication reopens the same draft. | `WorkspaceSaveSafetyTests` covers default name, invalid names, replacement cancel/approve, case aliases, concurrent changes and draft preservation. `JsonFileStoreNewTests` covers no-overwrite and late replacement refusal; affected tests and Release build pass. Native keyboard/layout and independent-process races remain open. |
| FCUX-025 — previous workspace restoration | Capture one bounded prior arrangement before opening a named workspace, then expose Restore previous workspace in Tools, command search and the completion notification. Restoring swaps the one prior slot with the current arrangement and preserves supported tab order, lock roots, targets, active panel/tab and split tree without consuming Recently Closed. Count transient views from live tabs, disclose their loss before switching, and adjust saved active-tab indices after omission. | `WorkspaceRestoreTests` covers 40 tabs/four panels, proportions, target links, active state, notification action, repeated restore, Recently Closed independence, the 512-tab bound and transient refusal/omission. The 50-case affected App selection passes. Native keyboard, scaled layout and assistive-technology acceptance remain open. |
| FCUX-026 — reachable listing columns | A horizontal viewport and scrollbar expose all configured columns while preserving stored widths. Header, rows, hit tests, resize edges, tooltips and the rename editor use the same offset; vertical and horizontal bars reserve space for each other. Left/Right, horizontal/Shift+wheel and accessibility Scroll controls navigate it. Name retains a font-scaled minimum; tab/profile changes reset and resize clamps the offset. | `HorizontalColumnTests` covers 320/480/640/800 DIP panes, larger text, two fill columns, vertical overflow, scrolled sort/resize, rename, keyboard, wheel, automation and compact-state transitions. Existing profile, list smoke, automation and toolbar tests pass in a 42-case selection. Release build has zero warnings/errors. Native touchpad, scaled layout and screen-reader acceptance remain open. |
| FCUX-027 — sort focus continuity | An explicit sort carries a one-shot reveal for its requested listing revision and focused item. A newer scroll, focus, navigation, tab switch or view revision suppresses it. Background refresh still preserves manual scroll. | `SortFocusContinuityTests` covers sort toggles, metadata order, delayed scroll/focus/tab changes; `FileListSmokeTests` protects refresh scrolling and `FileListSelectionTests` protects range anchors. A 28-case related App selection and the focused Core revision test pass. Native input and screen-reader acceptance remain open. |
| FCUX-028 — filter draft validation | Invalid mask text and caret stay in the focused field while the applied filter remains. A field-adjacent, bounded validation message is linked through automation HelpText and an assertive live region. Repair clears only that error; Escape, blur, tab switch and navigation discard a draft. All-items masks retain unrelated location and listing warnings. | `FilterDraftValidationTests` covers invalid quote, regex and saved reference, correction, Escape, blur, tab switch, accessibility properties, Registry context and a real working-set banner. Existing `PathLineTests`, working-set, quick-search, operation, rename and 40 Core mask tests pass; the 27-case related App selection and Release build pass. Native screen-reader and scaled-layout acceptance remain open. |
| FCUX-029 — notification lifetime and review | Publish message, severity, action and repeat count as one state. Info expires after six seconds unless hovered/focused; errors require explicit dismissal. A fixed Copy text/History/Dismiss control row and selectable, height-bounded text support inspection. Equivalent messages within thirty seconds share a count; distinct notices use polite or assertive live regions. Tools and command search open the newest 100 retained groups after dismissal or other commands. | `NotificationCenterTests` covers severity transitions, repeated messages without duplicate live-text changes, stale expiry, focus/hover retention, copy, Escape, action visibility, history and the 100-group bound. A 54-case related App selection and full Release build pass with zero warnings. Dialog tests scope their buttons to the overlay so notification controls cannot collide with modal actions. Native screen-reader announcement, clipboard and scaled-layout acceptance remain open. |
| FCUX-030 — measured panel geometry | Add-panel availability, execution, drag previews and cramped feedback use the measured workspace host. Unmeasured startup gives an explicit refusal. A compact address row moves free space and filtering beneath the editable location and target role while retaining full role semantics. The 260 × 240 DIP panel minimum leaves at least two list rows in the tested compact layout. Cramped restoration explains the reversible F11 route, including a one-panel warning. | `PanelViewportLayoutTests` covers measured admission with chrome changes, narrow and restored address geometry, F11 recovery and actual list capacity. `DockingTests` and `PanelLayoutTests` protect drag constraints and tree math. The 16-case affected App selection, six Core layout cases and Release build pass. Native 100%, 150% and 200% scaling, screen-reader and pointer acceptance remain open. |
| FCUX-031 — keyboard panel resizing | An explicit Panels → Arrange and command-search action focuses the nearest splitter. Arrows adjust eight DIPs, Shift adjusts 32, Tab chooses another boundary, Enter keeps the proportion, and Escape restores only the live layout snapshot. Pointer and keyboard update the same saved split tree under the same minimum sizes. A temporary strip and accessible splitter name show orientation, neighboring panel numbers and percentage; splitters stay out of normal Tab navigation. | Four `PanelKeyboardResizeTests` cover adjustment, cancel, save/reload, nested orientations, both minimums, stale tree/workspace replacement and Commander keys outside resize mode. `CommandSearchTests` checks discovery; `PanelKeysTests` verifies the menu icon. The 35-case focused layout selection and Release build pass with zero warnings. The complete App suite passes 2,155 cases with 62 platform skips. Windows VM `panel-resize-native` passed twice: command search focused the named splitter, Right moved it eight physical pixels and changed its announced share from 50 to 51 percent, then Escape restored its exact rectangle and list focus. | Native spoken screen-reader feedback, nested layouts and scaled-display acceptance remain open. |
| FCUX-032 — keyboard ancestor menu | Alt+Up, Navigate and command search open a single standard menu based on the same provider-derived segments as pointer breadcrumbs. Each ancestor offers Open here, Open in new tab and Copy path with its full safe target in the accessible name. The drawn path names the current location and explains Alt+Up/Ctrl+L. Escape returns to the current listing; stale menu actions refuse changed tabs/locations, and closure never restores an old row coordinate. | `BreadcrumbMenuTests` drives Alt+Up, Right/Enter for all three actions, clipboard publication, Escape, stale-action refusal, listing focus, Ctrl+L and the no-ancestor refusal. Existing `PathLineTests`, command search and menu-icon tests pass in a focused selection and the complete App suite passes. Windows VM `breadcrumb-native` passed twice: Alt+Up focused the full-path ancestor, Right exposed all three full-path actions, and Escape returned focus to the file list. | Native spoken screen-reader interaction and non-filesystem provider paths remain open. |
| FCUX-033 — bounded breadcrumb presentation | A long root, leaf or one-part path falls back to a root cue, explicit ellipsis and distinguishing leaf tail. Truncation preserves grapheme clusters and complete safe-display escapes. A gap has no hidden navigation target, and visible link geometry uses only contiguous fragments. Full safe path is available in tooltip and accessible name; exact raw Copy remains. | `PathLineElisionTests` covers one/two/many parts at 120/240/480 DIPs, UNC and provider-style roots, suffix identity, emoji/combining marks, escaped bidi/control characters and mapped hit tests. The existing pointer and keyboard breadcrumb tests pass. Core regression passes 4,413 with 150 platform/fixture skips; complete App regression passes 2,155 with 62 skips. Native typography, screen-reader and scaling acceptance remain open. |
| FCUX-076 — Copy representation and completeness | Separate source text from displayed text, preserve original decoded tabs and logical line endings, and capture explicit selection/visible scope. Show retained selectable evidence for the one-MiB budget, partial/short reads and failures. Serialize owned clipboard publications, reject changed-source evidence and keep cancellation limits truthful. Preserve the report's complete original Copy all route. | `ViewerSourceCopyTests` and source-mapping/geometry cases cover multibyte boundaries, giant-line continuations, control characters, caps, short reads, delayed writes, cancellation, source revisions and explicit scope transitions. Native clipboard interoperability remains open. |
| FCUX-072 — binary comparison byte ranges | Alt+B and a labelled action select an exact left or right source interval from decimal or hexadecimal offset and decimal count. Invalid, overflowing, out-of-range and stale requests keep the draft or retire; aligned gaps never become bytes. A bounded selectable preview and read-only document value expose side, source, offset, extent and copied representation. Copy belongs to its current source/selection, checks available revisions, serializes clipboard publication, and keeps the existing 4 MiB spaced-hex/text contract. | `HexCompareRangeTests` and existing comparison/accessibility tests: 79 Windows cases pass together, including 24 focused comparison cases. Native screen-reader, clipboard and 640 × 320 DIP scaling acceptance remain open. |
| FCUX-073 — viewer semantics | Publish semantic values after the current content state commits. Include source-selection extent, loading/read failures, Hex position/column and actual editing state. Retire old request/source ownership and avoid synchronous source I/O from peer queries. | `ViewerGeometryAndSemanticsTests`, `HexEditorSemanticsTests` and existing accessibility/lifetime coverage. The descriptive value stays read-only rather than promising an unsupported binary SetValue API. Native automation/screen-reader bridges remain open. |
| FCUX-036 — advanced Settings editing | Saved filters, user commands and associations have structured list/detail editors, ordering, item-level validation and lossless Advanced text. Program Browse, argument-token insertion, nonexecuting invocation and first-match previews, Settings search, and keyboard remapping work inside the draft. Every Advanced text editor offers UTF-8 file import/export without committing until OK; import is strict and bounded at 8 MiB, and export atomically replaces a confirmed local target. | `SettingsPreservationTests` covers structured/raw round trips, previews, field errors, search and Cancel. `AdvancedTextFileExchangeTests` covers UTF-8 BOM/invalid bytes, size limits, atomic replacement, cancellation and accessible actions in all three editors. The combined related Settings run passes 30 cases with one platform skip. The Windows 11 VM runner passed the native picker Cancel, spaced executable path, invocation preview, Settings draft restoration, and Tab/Enter/Escape Browse path checks. | Native broader keyboard, scaling and assistive-technology acceptance remain open. |
| FCUX-037 — toolbar availability | Refresh toolbar buttons from the same `GetAvailability` admission used by menus, key bar and command search. Track active-tab history, focus, marks, location and switch state. Disabled buttons expose the current refusal in a hoverable wrapper and automation help text; execution still rechecks admission. | `ToolbarAvailabilityTests` covers active-tab Back/Forward, item and provider changes, key-bar/palette agreement, reason exposure and stale invocation. The related 35-case toolbar, tooltip, key-bar, search and accessibility selection passes. Full App Debug 2,170 passed/62 skipped began before the Page-search and toolbar changes; full Core Release 4,417 passed/150 skipped. | Native screen-reader, pointer tooltip and rapid large-listing/remote-provider acceptance remain open. |
| FCUX-038 — complete bulk-rename review | Bind all accepted planner rows to an explicitly virtualized list. Offer Problems and Changed filters, path search, next-problem navigation and selectable full old/new path details. The reviewed and submitted row arrays are the same. | `BulkRenameReviewTests` exercises 3,001 rows with a blocking collision at row 2,501, full paths, filtering, next-problem navigation and bounded realized controls. Existing rename scope, execution and dialog viewport tests pass in a 33-case selection (one platform skip). | Native screen-reader, scaled-layout and maximum-size responsiveness acceptance remain open. |
| FCUX-039 — responsive rename validation | Separate pure expansion/plan collision checks from target existence checks. Compute and group candidates off the UI thread; use bounded per-device workers for existence, cancel queued work and ignore superseded generations. Show an accessible checking state, disable Rename until validation, and recheck current targets before job admission. The runner still refuses replacement at execution. | `BulkRenameAsyncPreviewTests` holds a target probe while editing and canceling, verifies a newer preview wins, and makes a target appear between preview and confirmation. Core `BulkRenameTests` covers pure candidates, swaps, checked collisions and cancellation. The related App selection passes 38 with one platform skip; seven focused Core cases pass. | Native slow-network, maximum 50,000-row responsiveness and shutdown timing acceptance remain open. |
| FCUX-040 — exact bulk-rename counter validation | Parse start and step as signed 64-bit integers and digits as 1–12 without fallback or clamping. Show field-adjacent, accessible errors while preserving text; empty intermediate input gets neutral guidance. Negative steps count down, zero repeats the value, and checked planner arithmetic marks overflow as a blocking row problem. | `BulkRenameCounterValidationTests` covers malformed/overflowing start and step, invalid/overflowing digits, blank input, zero/negative steps and exact preview values. Core `BulkRenameTests` covers counter overflow with and without a counter token. Eight focused App and eight Core cases pass. | Native focus, screen-reader announcement and scaled-layout acceptance remain open. |
| FCUX-041 — transfer destination validation | Show destination errors beside the field and keep the form, options, and scope intact. Start and Queue use the same asynchronous resolver and captured folder/new-name mapping. Device-scheduled filesystem probes cannot block editing or Cancel; stale results cannot authorize submission. | `TransferDestinationValidationTests` covers unsupported providers, retained options, both buttons, a single-item rename mapping, a held probe, correction, and cancellation. Existing operation and interrupted-transfer tests wait for destination validation before submission. Focused cases pass; an earlier quiet complete App attempt was stopped before completion. | Native network path, focus, screen-reader, and execution-time destination-change acceptance remain open. |
| UXBUG-003 — PE Info scrollbar drag | Throttle thumb-driven seeks to one latest viewport about every 32 ms, retire pending seeks on source or other navigation changes, and cache the report's line count for status updates. | `ViewerGeometryAndSemanticsTests` covers 80 rapid scrollbar values, repeated pointer moves down/up through the thumb, and stale seek retirement. A temporary local headless check against the supplied SignalSetup.exe passed; the sample-specific test is not committed. The 66-case related viewer suite passes. | The user manually confirmed the Windows desktop drag on the supplied EXE works. |
| FCUX-042 — operations drawer layout | Persist a vertically resizable drawer while preserving usable workspace; bound and scroll recovery review with one selected item, keep a complete operation row and Open report action at 640 × 420, wrap controls, and collapse the graph until requested. | `OperationsDrawerLayoutTests` covers 1/5/20 interruptions, keyboard navigation, small/wide windows, a selected operation and report entry, pointer/keyboard resize, saved height and shrink/expand restoration. Nine focused cases pass. | Native high-DPI, pointer, keyboard and screen-reader acceptance remains open. |
| UXBUG-004 — Markdown prose width on resize | Let prose blocks use the article's available width, matching table regions as the Page window changes size; retain article padding and table-local horizontal scrolling. | Forty-eight `MarkdownTests` cases pass. Headless Chrome measured prose and table regions at 505 px in a narrow window and 1,441 px in a wide window, with no page-level horizontal overflow. VMware native pointer tests widened the table by 335 px while prose shortened by 25 px; narrowing the viewer kept its document within the window and the table in a keyboard-focusable horizontal scroll region. | Native scaling and assistive-technology acceptance remain open. |
| FCUX-043 — recovery preparation state | Show Reviewing, Awaiting approval, Applying, and Submitted states on the interrupted row; disable conflicting actions and offer Stop. Stopping before approval closes that review's dialog and preserves the old journal. Stopping after approved cleanup retains completed effects and an open-journal report. A submitted job has independent ownership. | `InterruptedDemandTests` adds six focused cases for held open/read calls, approval, new-transfer form, an already deleted partial, and Stop in the job-admission callback. The complete App Release run passes 2,232 with 63 skips after correcting drop-test timing and the collapsed review section. | Native delayed-device, keyboard/screen-reader status, and shutdown timing acceptance remain open. |
| FCUX-044 — explicit Find scope | Keep local, result-set, and Registry search scopes distinct. Unsupported SFTP, FTP, archive, and virtual locations open Find with no local root, explanatory guidance, and a disabled Find action until the user chooses a local scope. Loaded presets identify their own local scope; result-set snapshots ignore preset folders. | Twelve `FindOriginScopeTests` cases pass across an actual This PC command, unsupported providers, deliberate local choice, saved presets, and result-set scope. The related 47-case Find/Registry selection and complete App Release run pass. | Native provider navigation and assistive announcement acceptance remain open. |
| FCUX-045 — Find completion feedback | Give the completion state and search qualifications separate wrapping lines, keep changing path/counters secondary, and expose the existing log beside the summary. Replace the generic empty-folder text with the search's captured criteria/scope, Edit criteria, and Review search log. Distinguish stopped, excluded, inaccessible, warning, regex timeout, and manually emptied results. | `FindFeedbackTests` covers the state matrix; `FindWindowTests` exercises narrow zero-result layout, keyboard log access, and hiding the last result. The 98-case Release Find selection and complete App Release run pass. | Native narrow-window, keyboard, and screen-reader acceptance remains open. |
| FCUX-046 — Find shortcut contexts | Register Find criteria/results actions with stable IDs and preserve their default keys. Resolve actions through the keymap, show effective gestures in menus and hints, explain panel overlaps and same-context conflicts in Settings, and keep text editing, path suggestions and dialogs ahead of Find dispatch. Find-only actions appear as unavailable outside Find instead of offering a no-op. | New Core keymap and App Find/Settings cases pass. The related 56-case App selection passes; the complete Core Release run passes 4,420 with 150 skips using 16 workers. The complete App Release run passes 2,232 with 63 skips. | Native keyboard and shortcut editor acceptance remains open. |
| FCUX-047 — fractional scroll and one-row paging | Accumulate precision-wheel fractions into whole rows in file list, text, hex and binary comparison; reset them at source/layout/navigation changes and list boundaries. Keep PageUp/PageDown moving with one visible row and retain list rename alignment. | Fifteen focused App cases and 117 nearby viewer/list regressions pass. The complete App Release run passes 2,238 with 63 skips. | Native conventional-wheel, precision touchpad and inertial-input acceptance remains open. |
| UXBUG-005 — viewer window size | Remember a deliberate normal viewer size in profile settings and restore it for text, Markdown, PE Info, hex and picture modes. Clamp invalid or oversized values to the active display; an older untouched viewer cannot overwrite a newer size. Debounce normal geometry and discard any pending sample when state changes, including an early maximized-sized event. | Three `ViewerWindowSizeTests` cases cover validation, screen bounds, reopen across viewer modes, restart persistence, concurrent windows and maximize/restore transitions. Native Windows retained a 1,592 × 1,414 viewer through maximize, restore, close and reopen, with the saved normal dimensions unchanged. | Native multi-monitor and DPI acceptance remains open. |
| UXBUG-006 — stale Streams count after deletion | After confirmed Delete removes a stream or attribute, retire only the parent file's cached `hidden` metadata before refreshing the side-data list. Requery the provider when the parent row appears; partial failures also requery so the count reflects actual remaining data. | `HiddenDataUiTests` primes and reads the Streams/Attributes cell, presses Delete, confirms the side item is gone and file contents survive, then checks the parent row's count decreases by one. The related 105-case App selection has 89 passes and 16 skips. A native Windows Delete-key check removed a temporary NTFS stream on disk, and the parent row's Streams cell was blank immediately after navigation back. | Native Unix attribute acceptance remains open. |
| UXBUG-007 — resizable search dialogs | Registry search, Advanced criteria, and Ignored folders dialogs expose a visible drag grip with keyboard arrows and larger Shift steps. Criteria and results fit the available width; lists gain height when expanded. Host shrink clamps the card and later expansion restores its requested size. | `SearchDialogResizeTests` covers drag events, keyboard resize, host shrink/expand, reopening, Registry results and narrow layout, ignored-folder results, and the Advanced grip. Seventy-two related App cases pass. The complete App Release suite passes 2,242 with 63 skips. Native Windows pointer checks expanded and shrank all three dialogs; Advanced criteria and Ignored folders kept their input reachable by scrolling at minimum size. | Native scaling and assistive-technology acceptance remain open. |
| UXBUG-008 — visible viewer Find results | Text, Hex and Info show a background match count, visible Previous/Next buttons, and a Stop count action. Counting is limited to 64 MiB for local sources or 8 MiB for nonlocal sources and 10,000 hits; partial totals are labelled as lower bounds. Page uses WebKitGTK's rendered-page count, preserving an exact result through directional end-of-page searches and labelling its 1,024-match limit as a lower bound. Windows and macOS retain a truthful directional lower bound until their native Find interfaces can supply a total. Query, source and mode changes retire stale counts. | Seven `ContentSearchCountTests` cases cover overlap, encoding, read, limit and cancellation boundaries. `ViewerFindControlsTests`, `ViewerPageSearchTests` and `LinuxPageEngineTests` cover controls, exact/limited/unknown result states, keyboard parity, source ownership and the native GTK count signal. The focused Windows App selection and Linux native CI integration pass. Native Windows WebView2 showed Find, Previous/Next and the labelled directional lower bound. | Native macOS and remaining keyboard/assistive-technology acceptance remain open. Exact totals on Windows and macOS require a suitable native engine count API. |
| UXBUG-009 — Details selects active operation | Reopening the operations drawer selects the current active job when the retained selection has finished, then scrolls the selected row into view. While the drawer stays open, progress refreshes and newly added work preserve a deliberate history selection. | `OperationCenterTests` reproduces eight finished jobs, a scrolled history, a new waiting job, Details click, row visibility, and selection preservation after another job arrives. A second case uses a genuinely Running job after eight completed rows. Both focused cases pass; the earlier 44 related App cases pass. | Native pointer, keyboard, and screen-reader acceptance remain open. |
| UXBUG-010 — highlight consequential recovery confirmations | Put interrupted-transfer choices and consequential effects in separate themed, selectable callouts. Choices identify the action, source availability, destination, one-root name mapping, filter, conflict policy, verification and start mode. The Important effects callout identifies incomplete legacy intent, omitted sources, partial-file deletion, Skip/replace/rename effects, move source removal and unresolved review work when present. An emphasized action and conflict choice, bulleted effects, and a disclosure with visually sectioned complete mapping and safety notes make the full review scannable; Cancel is the default for consequential actions, including a new transfer from an incomplete legacy record. Cleanup retains its complete visible review. | The focused `InterruptedTransferIntentUiTests` and six `DialogViewportTests` cases pass, including move, partial cleanup, legacy new copy, keyboard disclosure and minimum-window font sizes. The Windows 11 VM displayed the revised emphasized action, conflict choice, bulleted amber effects and sectioned full review, with Cancel focused and approval pending; `legacy-highlight` and `legacy-cancel` native assertions pass. | Native scaling and screen-reader announcement of the revised hierarchy remain open. |
| UXBUG-011 — highlight long safety confirmations | Reuse the themed, selectable key-points summary for drive/disk recovery scans and searches. It names the source or amount read, read-only scope, mounted-drive risk, stop action and safe recovery destination while keeping every original detail below. The confirmation to stop recovery reminders now calls out that no files change, where the bounded report remains, and any rename or review-limit warning. Explicit caller-supplied facts avoid guessing which sentence matters; consequential actions keep Cancel as their default. | `DialogFocusOwnershipTests` checks summary order, full selectable detail and safe Enter behavior. `RecoverySafetyTests` verifies the scan caller supplies source and destination facts; `InterruptedRenameTests` verifies its rename warning. Eight focused cases pass with six platform skips using 16 xUnit workers. Three `DialogViewportTests` cases pass at 640 × 420 DIP with 14, 21 and 28 point UI fonts, keeping full details reachable and footer actions visible. | Native scan-dialog layout, scaling and assistive-technology acceptance remain open. |
| FCUX-048 — listing background context menu | Right-clicking empty listing space opens location actions without changing Commander marks or the focused row. The menu uses a captured tab and location for Paste and New folder even if another panel becomes active, refuses stale tab navigation, rechecks capabilities, and shows disabled reasons on unsupported locations. The Windows item shell menu remains limited to item requests. | `BackgroundContextMenuTests` exercises empty and nonempty pointer routes, dismissal, menu contents, actual Paste and New folder destinations, another active panel, stale navigation, and disabled location feedback. Five focused and 63 related App cases pass with one platform skip. | Native pointer placement, clipboard interoperability, read-only archive/Registry/working-set/disconnected providers, keyboard route and assistive-technology acceptance remain open. |
| FCUX-049 — live viewer theme changes | TextViewer and HexView own theme subscriptions only while attached. Applying a new palette re-resolves semantic text, background, selection, search and edit brushes in the existing controls. ReportWindow no longer duplicates TextViewer's theme notification. | `ViewerThemeLifecycleTests` switches open controls through Classic, Dark, High Contrast, DOS and back while checking brush resources, selection and position; attach/detach checks verify static-event release. Two focused and 116 related App cases pass. | Native theme transition, system high-contrast, quick-view, editor and screen-reader acceptance remain open. |
| FCUX-050 — Psychedelic effect boundaries | Use opaque Psychedelic menu, panel, card and functional bar colors. Keep movement in the backdrop behind content; remove the overlay that copied and shifted rendered text/picture slices. Focus, grid and border brushes no longer animate. Pause the effect clock with the theme animation preference or inactive app so repainting stays static. | `PsychedelicSurfaceTests` checks functional alpha and menu contrast, samples 13 moving backdrop phases, verifies decoration stays below content and the disabled clock freezes, and switches themes back to Psychedelic. Three focused and 54 related App cases pass. | Native full-cycle menu/icon contrast, transparent picture content, system reduced-motion, window scaling and assistive-technology acceptance remain open. |
| FCUX-051 — desktop motion preference | Query Windows client-area animation, macOS accessibility reduced motion, and GNOME `enable-animations` through a bounded, absolute-path system tool. Probe while an animated theme is active and recheck on reactivation; hold motion still until the first read. The app checkbox off and inactive-window pause always win. Unsupported or failed providers leave the explicit app control authoritative. Settings and capability text state the supported desktops and correct Psychedelic effect description. | `MotionPreferenceTests` covers the policy matrix, GNOME desktop/value parsing, live preference transitions, the disabled/inactive clock and the Windows native API read. Thirty-two related theme and Settings App cases pass. | Native Windows/macOS/GNOME preference toggles, unavailable provider behavior, high contrast and assistive-technology acceptance remain open. The poll can lag a change by one interval plus its bounded provider read. |
| FCUX-055 — picture zoom state | Separate Fit and 100% buttons with a visible zoom percentage. A custom 50%, 125% or 200% scale leaves both modes unselected. The 100% command returns to original-scale dimensions; Fit remains a distinct mode even when a small image happens to display at 100%. Keyboard Z retains the Fit/100% toggle. | `PictureViewerTests` exercises buttons, zoom text, custom levels and small-image equality; picture revision checks stay green. Twelve focused and 74 related App cases pass, with 12 platform skips in the related set. | Native pointer, scaling at 100/150/200%, decoder-cap labeling and assistive-technology acceptance remain open. |
| FCUX-052 — refresh changed viewer content | Refresh/F5 reopens the exact provider item, stages new bytes and mode-specific decoding, then replaces Text, Info, Picture or Page in the same window. A persistent banner marks stale snapshots; failures retain the last valid view and show the reason. Picture zoom and approximate pan, text/Info scroll, and selections on an identical revision are preserved. Page waits for native load before committing; cancellation and late provider results retire their sources safely. | `ViewerRefreshTests` covers text, Info, Picture, Page, provider-only sources, failed reopen/load, selection and scroll, close during open, and close during metadata admission. `ViewerProviderAdmissionTests` verifies the original item/device worker and late-source disposal. Nine focused cases pass; related viewer suite passes 326 cases with 12 platform skips. | Native archive/remote reopen and capability behavior, WebView2 page-load rollback, scaling and assistive-technology acceptance remain open. Providers without revision evidence require explicit Refresh to detect a change. |
| FCUX-053 — clear quick-view feedback | A selection-owned preview state reports Loading after the debounce and no-flicker delay, then Text, Hex, Picture or a specific message. Cancel/Retry and Open full viewer are visible actions. Empty, unsupported and denied results explain themselves; picture decode failure and the 64 MiB automatic cap keep Hex visible with a reason. Preview pixels scale with the pane up to 1,024 physical pixels. A late source, thumbnail or decoder result cannot publish over a newer selection. | `QuickViewLifetimeTests` covers held loading, rapid selection, Cancel/Retry, empty/full-viewer action, full-viewer cancellation, unsupported/denied results, damaged and oversized pictures, pane-size cap, and source ownership. Fourteen focused and 68 related quick-view/picture cases pass. | Native Shell thumbnail fallback, slow local/remote storage, small-pane layout, keyboard and assistive-technology acceptance remain open. The full viewer can have a higher decode budget than automatic quick view. |
| FCUX-054 — usable Viewer Go to | Replace the small unvalidated top-level input with the shared viewport-bounded prompt, visible Go/Cancel buttons, a wrapped label, and inline validation. Invalid and out-of-range offsets, percentages and line prefixes keep the draft open. Valid decimal/hex/percent positions and seekable line targets navigate after confirmation; nonseekable lines explain their limitation before scanning. Modal ownership blocks other viewer shortcuts until dismissal. | `ViewerGoToDialogTests` covers the pure parser and bounds, a 480 × 300 dialog with pointer correction, Enter line navigation, Escape, and a nonseekable source. Four focused and 338 related viewer/dialog cases pass, with 12 platform skips in the related set. The complete App rerun through this finding passes 2,296 tests, 63 platform skips and zero failures. | Native pointer, keyboard, scaled layout, screen-reader announcement and remote seek capability acceptance remain open. A line beyond the document is reported after the asynchronous scan. |
| FCUX-056 — compare source selection and Find | Virtualized per-side cells support drag, Shift+click and keyboard selection. Copy selection/line/difference actions use original source text, omit gutter numbers, explain gaps and normalize selected line joins to LF. A collapsible Find bar searches both or one shown side, optionally changed rows, with distinct match buttons and F3 shortcuts. Canceled searches cannot publish after query, view or window replacement. The main toolbar retains room for full-line details at 640 DIPs. | `CompareTextInteractionTests` covers source and gap copy semantics, pointer and keyboard selection, long offscreen suffixes, search scope/count/navigation and cancellation. The related comparison suite passes 81 tests, zero failures. | Native pointer/context-menu, keyboard, screen-reader and scaled-layout acceptance remain open. The search stores at most 50,000 positions and marks additional matches with `+`; exceptional lines use full-line details for selection. |
| FCUX-057 — truthful update-check privacy wording | Privacy now labels the preference “Automatically check for updates” and explains the once-daily startup gate, notify-only result, no update package download or installation, and explicitly requested Help checks and network locations. The update-service comment, architecture plan and servicing ADR use the same scope. No automatic/network behavior changed. | `SettingsDialogTests` checks visible wording and saved preference; `UpdateCheckTests` checks disabled and recent scheduled checks without network. Both focused cases and 29 related Settings/Update cases pass, with one platform skip. | Native scaled Settings layout, screen-reader wording and manual network-check acceptance remain open. |
| FCUX-058 — command and Places hierarchy | Ten stable primary toolbar actions have short labels where useful; a right-anchored More menu groups every remaining classic toolbar command. The original full toolbar remains an Appearance choice. File's Archives, Inspect and Working sets submenus make secondary tasks scannable while command IDs and shortcuts stay unchanged. Compact Places shows a small stable set with pinning; its More menu retains every omitted or overflowed destination and can clear pins. The full Places row remains an Appearance choice. | `ToolbarHierarchyTests` checks the declarative model, grouped menus, pointer More action, width 640/1280 geometry and saved classic preference. `PlaceBarHierarchyTests` covers compact/pinned order, complete More discovery, pin persistence, clear pins and full-bar restoration. The original full-bar lifetime and keyboard tests run in classic mode; 71 related tests pass, plus six focused model/persistence tests after the final place-key refinement. The complete App run found an outdated drive-menu order assertion; it now checks both Drive properties and the appended Pin action, and the related cases pass. | A fresh complete App run is in progress. Native four/six-panel density, pointer, scaled layout and assistive-technology acceptance remain open; primary membership is an initial design hypothesis pending usage research. |
| FCUX-074 — command-search Help | The no-result hint names the effective Help binding; pressing that binding in search opens Keyboard reference. A named focusable button supplies a visible keyboard and screen-reader route. Dismissing Help returns query, caret, selection and focus; launching a command from Help follows that command. Search preserves selection across deferred same-query updates. | `CommandSearchTests` covers default and remapped Help, no-result and selected-result return, visible named button keyboard activation and the distinct Enter/F2/Escape paths. All 26 command-search cases pass; 28 including the drive-menu regression. | Complete App rerun and native keyboard, screen-reader and popup-size acceptance remain open. |
| FCUX-059 — readability preferences | Appearance now separates Compact/Comfortable interface density, the existing list font size, and a saved content size for text, hex and comparison. Comfortable increases chrome text, target spacing and file-list row height without scaling icons. New viewer, comparison and hex editor windows start at the preferred content size; named Text −/+ controls and Ctrl+minus/plus adjust an open view within 9–32 logical units. | `ReadabilityTests` covers bounded settings, persisted content size, independent list size, switching density in an open window, 640-DIP primary/Settings action placement, and visible/keyboard controls in all three content windows. Six focused cases pass. Related App run: 365 passed, two platform skips. Pinned earlier FCUX-059 App snapshot: 2,317 passed, 63 skips. | Complete App rerun on final density tokens and FCUX-060 and the Core run are pending. Native 100/150/200% scaling, screen-reader and actual render/readability acceptance remain open. |
| FCUX-060 — viewer/editor command hierarchy | Viewer modes and core navigation occupy a stable primary row with an explicit Text/Hex/Info/Page/Picture label. Viewer and editor Find rows open on demand and keep their query when hidden; secondary checksums, source copy, byte editing, Save As and patch actions live in visible More menus. Save/Undo/Redo stay in the editor's primary row; all existing keyboard actions remain. | `ViewerToolbarHierarchyTests` covers mode labels, Find focus/query return, More action reachability, checksum action and 640×420/1280×720 viewer geometry. Related Page Find, Picture and hex editor F6 tests assert the new visibility, mode and focus routes. Two focused hierarchy cases, six combined mode/Page cases and 342 related viewer/editor cases pass with 14 platform skips. The complete App rerun passes 2,320 cases with 63 platform skips. | Native pointer, screen-reader, minimum-size wrapping and maximized layout acceptance remain open. |
| FCUX-061 — clipped listing cell inspection | Hovering a clipped Name, split Extension, Folder or Details cell shows the complete safe value; ordinary short cells remain quiet. The focused row has a read-only resizable full-value dialog from Ctrl+Shift+I or the item menu. Rendered text and clipping state are cached for the visible viewport; pointer movement never asks a provider for metadata. Scroll, sort, refresh, tab changes and metadata updates retire old tooltips. | `ClippedCellInspectionTests` covers both text-layout paths, clipped and ordinary hover, split extension, provider Folder/Details safety, keyboard and actual main-window dialog, provider-call absence and stale tooltip retirement. Six focused and 53 related list/profile cases pass separately, and the final combined 59-case selection passes. | Native pointer, assistive-technology and large provider-value acceptance remain open. |
| FCUX-062 — tab identity and lock modes | Tabs with equal displayed names gain the shortest distinguishing parent, drive, archive or remote cue; exact duplicate locations gain an instance number within their collision group. Ordinary unique names stay compact. Navigation locks and return-to-root locks have different visible markers plus full safe path, lock meaning and saved root in tooltips and accessible text. Labels recalculate on navigation, rename, tab collection changes and lock changes. Root revisits use owned navigation so title/path update together. | `TabStripTests` covers local/archive/remote collisions, duplicate insertion/move/close, visually equal escaped names, live lock mode and accessibility bindings, actual return-to-root navigation, existing hidden-name safety and overflow. The related 39-case tab/workspace/docking/navigation selection passes. | Native tab-width, assistive-technology speech, pointer drag and remote/archive naming acceptance remain open. |
| FCUX-063 — dependent controls and list-editor actions | Folder sizing on slow locations and Shell pictures on slow drives are indented and follow their parents immediately without erasing saved subordinate choices. The column editor disables New/Duplicate at the profile cap, Remove for the only profile or required Name column, edge Up/Down actions, and Add for an existing column. Disabled actions expose specific hover and accessible reasons, including on disabled controls; availability updates with draft selection and edits. | `DependentControlsTests` covers both supported/unsupported child options, live Behavior Settings state and Cancel, profile limit/sole-profile and first/last actions, Name protection, duplicate/available column transitions and unapplied draft isolation. The combined Settings/column/dialog selection passes 58 cases with one platform skip. | Native disabled tooltip, keyboard, pointer and assistive-technology acceptance remain open. |
| FCUX-064 — safe main-window restoration | Save normal bounds separately from maximized state; restore validated physical position and logical size to the best matching available display. Removed monitors recenter the window, changed DPI and oversized rectangles clamp to usable work area, and malformed dimensions fall back to the first-launch geometry. A short normal-state debounce avoids saving intermediate maximize geometry. | `WindowPlacementTests` covers secondary/removed displays, DPI and invalid/offscreen input, and an actual headless normal → maximized → close → reopen → restore cycle. Three focused and 33 related App cases pass. | Native Windows, Linux and macOS window-manager behavior, multi-monitor movement, DPI changes and manual restore acceptance remain open. |
| FCUX-065 — one-panel workspace restoration | A valid saved one-panel workspace restores exactly as one panel with its original layout tree and no guessed target. The two-panel default is used only for missing or wholly unusable state. Invalid saved target IDs are cleared; valid targets in larger layouts remain. F5/F6 in one-panel mode ask for a destination. | `WorkspaceRestoreTests` covers actual remove/save/load round trip, default and unusable-state fallback, invalid/valid targets, and an F5 dialog with an empty destination. The 43-case workspace/target/docking/operation-scope selection passes. | Native one-panel docking and keyboard/pointer transfer acceptance remain open. |
| FCUX-066 — last-tab history guard | The panel model checks close eligibility before recording a closed state; a panel's sole tab remains unchanged. Command and tab-menu availability use the same rule, including after a tab moves between panels. | `TabStripTests` sends repeated real middle-clicks to the only tab after recording a genuine closed tab, checks history and reopening, then exercises command, menu and middle-click closes after a panel move. Eight focused and 30 related tab/workspace/navigation cases pass. | Native pointer and assistive-technology acceptance remain open. |
| FCUX-067 — About support actions | Copy details and precise settings/local-data folder actions live inside About while Close remains the dismissal action. Copy reports completion inline; opening either displayed folder creates a new tab without replacing the working one. Identical normalized paths produce one Open data folder action. | `AboutDialogTests` verifies exact support text, live status, retained dialog, accessible path help, both folder targets, new-tab preservation and identical-path collapse. `AboutLogoOwnershipTests` verifies repeated non-dismissing actions and final image disposal across themes. 31 focused and 46 related App cases pass. | Native clipboard and keyboard/screen-reader acceptance remain open. |
| FCUX-068 — meaningful queue movement | “Move earlier in queue” is visible only on queued jobs. The first queued job and fixed-order synchronization steps show a disabled action with an explanation; running/finished jobs do not offer it. The manager verifies eligibility under its lock and reports whether an actual swap occurred; successful moves announce the change. One queue snapshot updates all rows without per-row manager scans. | `JobEngineTests` covers first/eligible/no-op/finished jobs, order swaps, and synchronization order; `OperationCenterTests` covers actual selected-row enablement, disabled tooltip, finished-row hiding, destination preservation and notification. 28 job-engine Core cases pass with one skip; 51 related App cases pass. | Native queue timing, pointer and assistive-technology acceptance remain open. |

The three viewer findings share source mapping and content ownership and are
implemented together. The [viewer notes](UX_VIEWER_SOURCE_GEOMETRY.md),
[dialog notes](UX_DIALOG_VIEWPORTS.md) and [comparison notes](UX_COMPARE_LINE_DETAILS.md)
describe the contracts and their precise limits.

## Test results

The [current execution report](UX_UI_BATCH2_TEST_RESULTS.md) and [machine-readable results](UX_UI_BATCH2_TEST_RESULTS.json) distinguish original-source and first-batch baselines, focused tests, complete regression runs, native skips, diagnostic failures and their corrections. The [first-batch report](UX_UI_TEST_RESULTS.md) remains unchanged as historical execution evidence. The source builds use the repository's pinned .NET SDK **10.0.101**, with locked package restore. No dependency versions or lock files were upgraded.

The four final complete suites contain **9,616 cases: 8,960 passed, zero failed
and 656 explicitly skipped**. All bind to the same successful solution build,
with unchanged source/runtime inventories before and after execution and actual
zero exits. All 531 Core/App cases added since the original sources passed;
no baseline cases were removed. The seven runner/evidence/packaging Python groups
also passed all 227 checks. Native prerequisites and the exact skip reasons stay
visible in the execution report.

The original 31 App failures pass under a verified ordinary Linux identity with
worker privilege refusal and journal permission checks intact. The Core device
assertion now verifies the existing early non-regular-file refusal, with stronger
regular-file budget coverage. Ordinary-user execution also exposed and fixed the
valid-XDG-data-directory/no-profile trash case. See the [runner, security probes
and resource configuration](LINUX_UNPRIVILEGED_TESTS.md) for reproducible evidence.
Successful automated execution does not substitute for native desktop acceptance.

### Windows checkout checkpoint — 18 October 2026

The current checkout builds with .NET 10.0.101 in Release with zero warnings and
errors. A focused Windows run passes all 11 App cases covering the four failures
found during a broader App attempt: culture-sensitive number formatting,
realization of the archive folder prompt, and the deliberate Operations review
route for a background decision. A focused Core run passes all 203 cases for the
changed recovery, Undo, search, selection and settings models. The Core fixture
uses normalized path identities for a hard-link conflict and an Undo assertion.

The complete Windows App and Core attempts both stopped after their test hosts
became idle; neither is recorded as a passing complete suite. After FCUX-013,
a focused 124-case Windows theme, viewer, comparison and list selection run
passed with zero failures. After FCUX-014, 71 affected Windows list, profile,
automation, accessibility and formatter cases passed. After FCUX-071,
59 affected Windows App cases passed with one platform-specific skip, and all
6 focused Core staging cases passed. After FCUX-075, all 5 focused Windows
drop-target App cases passed; the combined drop, drag, operation-scope and
clipboard selection passed 54 with one platform-specific skip. After FCUX-015,
22 affected Windows App cases passed with one platform-specific
skip, and both focused Core issue/pace cases passed. After FCUX-016,
19 related Windows App cases and both Core issue/pace cases
passed. After FCUX-017, the solution Release build passed with zero warnings,
23 related Windows App cases passed with one platform-specific skip, and 23
Core issue/transfer cases passed. After FCUX-019, 24 related Windows App cases passed with one platform-specific
skip, all 18 App history-retirement cases and 38 Core retirement/Undo cases
passed. After FCUX-020, 168 related Windows App continuation/cleanup cases
passed with two POSIX-only skips, 38 rename/recovery cases passed, and both
recovery-store/report cases passed. After FCUX-021, the solution Release build
passed with zero warnings, 209 affected Core cases passed with one platform
skip, and 42 affected App cases passed. After FCUX-022, 55 related App cases passed with one platform-specific skip;
the final ownership guard passed 43 navigation, clipboard and link cases with
one platform-specific skip. The earlier
complete Linux suite results remain in the linked execution reports. Windows
native interaction and assistive-technology acceptance remain open.

## Reproduction

From the solution directory, with the pinned SDK installed:

```sh
dotnet restore FileCat.slnx --locked-mode
dotnet build FileCat.slnx -c Release --no-restore
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.Remote.Tests/FileCat.Remote.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj -c Release --no-build --no-restore
```

Use an ordinary desktop user for platform acceptance and picture-worker tests.
For the full suites on a runner with a 1.5 GiB managed-heap cap, add
`--settings docs/validation/ux-resource-bounded.runsettings`. This limits overlap
between xUnit collections while retaining tests' internal concurrency and all
original inputs/assertions. The original four-thread configuration also remains
available. The [Linux runner instructions](LINUX_UNPRIVILEGED_TESTS.md) describe
how to execute already-built assemblies safely from a root-only container without
changing system accounts or checkout permissions.

## Review notes and next work

- The [clipboard/search/checksum notes](UX_CLIPBOARD_CONTENT_FIXES.md) describe ownership, test coverage and native boundaries in detail.
- The [recovery/Undo notes](UX_RECOVERY_UNDO.md) describe persisted transfer intent, inverse admission, retained reports, cancellation and journal safety boundaries.
- The [selection/accessibility notes](UX_SELECTION_ACCESSIBILITY.md) describe the virtual peer and complete-selection review contract and native checks.
- The [comparison notes](UX_COMPARE_LINE_DETAILS.md) explain the virtualized primary surface, complete-line detail, shaping budgets and replacement ownership.
- The [viewer continuation notes](UX_VIEWER_SOURCE_GEOMETRY.md) explain source-relative selection, copy completeness, semantic updates and resource gates.
- Close the native acceptance gates before declaring all S1 acceptance complete.
- Complete the remaining native acceptance in the audit's physical severity order, preserving each finding's individual criteria. FCUX-034/035 were completed as part of the earlier Settings work; FCUX-036 native acceptance remains open. The session workflow records the subsequent bug queue, including the reported PE viewer scrollbar-drag fix that the user confirmed on Windows.

The [session workflow](UX_UI_SESSION_WORKFLOW.md) records what the user's next
“continue” means: resolve any outstanding validation failures, then resume the
highest-severity unfinished work with regression tests. The current Git checkout
and `main` history are the delivery record. Each verified implementation and its
tests are committed and pushed to `origin/main`; validation results and outstanding native
acceptance are recorded in this repository.
