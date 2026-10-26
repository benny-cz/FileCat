# Severity-ordered UX/UI audit implementation

**26 October 2026, acceptance-ledger reconciliation:** The authoritative
[remaining-scenario ledger](UX_UI_ACCEPTANCE_LEDGER.md) now distinguishes
individual native/provider acceptance from the audit-wide final CI gate.
The tables and dated paragraphs below retain implementation and historical
test evidence; their older “remains open” statements are snapshots from the
named build, not a second current backlog. The
[remaining-work index](UX_UI_REMAINING.md) links only open ledger scenarios.
The audit's pairwise and equivalent-route stopping rules apply: an unrelated
commit or open parent finding does not invalidate a passing native result.
Earlier “still open” notes below are preserved as dated/build-specific
checkpoints. In particular, the 25 October `15a1a9c5` empty-VHD scan and the
later unflushed fixture failure do not supersede the 26 October `663b6fdd`
fixed-VHD exact-byte recovery. `DEF-019A` now names only optional
reconfirmation on the owner's existing G:, outside required closure.

<a id="evidence-ftp-ambiguity-20261026"></a>
**26 October 2026, FTP/FTPS ambiguous-result reconciliation (FCUX-001/020):**
`AC-S1-001B` was stale. The real pyftpdlib fixture already performed backup
rename, stage publication or backup deletion and returned an ambiguous FTP
451 afterward. All nine combinations across FTP, explicit FTPS and implicit
FTPS passed on Windows (focused 9/9, full `FtpJobTests` 108/108): each checked
the exact public destination, retained backup/stage, local Move source and
open journal. Separate native Windows crash-recovery journeys had already
shown Copy and Move stage identity, conservative cleanup review and report
for each of the same three transports. Those results form a risk-based
pairwise acceptance of ambiguous server replies and native recovery review;
repeating all nine ambiguity cases through the UI would add no distinct
consequential boundary. The separate SFTP route is reconciled below.

<a id="evidence-recovery-closure-20261026"></a>
**26 October 2026, SFTP and Explorer Cut acceptance reconciliation:** The
FCUX-001 audit outcome is accepted. A real OpenSSH connection with the
`TryReplace=false` test adapter exercised the same non-atomic FileCat branch
for a late same-size/time target change and ambiguous backup, publish and
cleanup responses (4/4); exact public, backup, stage, Move-source and journal
states passed. Separate native live-SFTP interruption and reviewed cleanup
proved the recovery UI route. The still-unrun *different server without the
atomic extension* is an interoperability refinement, not another original
audit gate without evidence that the accepted branch differs. `AC-S1-001A`
closed under the audit's equivalent-route and pairwise stopping rule.

<a id="evidence-clipboard-windows-20261026"></a>
`AC-S1-069A` was also stale: Windows Explorer replaced FileCat Cut A and
Cut A+B with Copy of A; actual `CF_HDROP` manifests and destination/source
bytes proved Paste was Copy, while a fresh Explorer Cut carried native
`Preferred DropEffect=2` and FileCat reported performed Copy after importing
it. Canceled conflict reported no performed effect. Deterministic tests
cover clipboard replacement timing and actual submitted job kind. This
closes the Windows Explorer subscenario; Finder/GNOME external intent is
reconciled below.

<a id="evidence-clipboard-desktops-20261026"></a>
**26 October 2026, external desktop clipboard and delayed Paste closure
(FCUX-069/070):** The remaining native interoperation evidence is already
present. Finder Copy replaced a FileCat Cut, including the *same file URL*
with a newer pasteboard generation; the import ran Copy and left the original.
GNOME Files replaced FileCat one- and two-file Cuts with both Copy and Cut of
the same path; all four native variants passed twice, imported as Copy,
preserved source bytes and wrote the exact selected destination bytes. The
Windows Explorer Cut and Copy results are reconciled above. This is the
supported safe fallback for external Move intent that FileCat cannot
authenticate; the actual operation is visibly a Copy, and no external source
is silently removed. A future platform-specific external Move protocol is
optional and would need separate authority and effect feedback.
`ClipboardIntentTests` exercises the *production* Paste command with held
reads for Copy and Cut, same-tab navigation, panel/tab movement, closure,
destination disappearance, clipboard replacement, pending provider
navigation and relative text; assertions cover submitted kind, captured
destination, source survival, exact manifests and user-visible refusal.
Ordinary native Paste interoperation passed on all three desktops. Those
deterministic timing boundaries plus native backend exchange satisfy the
audit's pairwise/equivalent-route rule; a three-platform artificially held
native owner matrix adds no uncovered consequential boundary. FCUX-069 and
FCUX-070 are accepted, with the native protocol limitations stated here.

<a id="evidence-decision-closure-20261026"></a>
**26 October 2026, background-decision focus acceptance (FCUX-004):** The
remaining `AC-S1-004` row conflated the original S1 decision hazard with
Mirror/recovery dialog layout, which is tracked under S2. The production
window `BackgroundDecisionTests` places a queued Enter into an unfinished
prompt while a background Replace conflict waits: the prompt keeps its
draft/focus, the job stays AwaitingDecision, and explicit review focuses
Skip. The same tests prove no duplicate review and safe initial Enter for
metadata-loss, read-only Delete and decryption warnings. Focus ownership,
nested/superseded callbacks and deliberate focus movement have separate
regressions. Native Copy/Move/Delete modal-command guards passed on Windows,
Ubuntu and Mac. The narrow native FTP/FTPS/SFTP replacement reviews exposed
their full target/risk and safe choices with NVDA, Orca and muted VoiceOver,
while Cancel/Skip preserved exact source and destination bytes. Together
these cover the S1 queued-input/default/action and spoken decision outcome;
repeating Mirror or recovery dialogs does not reopen FCUX-004. Their distinct
long-path, resizing and hierarchy boundaries stay in `AC-S2-011/019`.

<a id="evidence-passed-routes-20261026"></a>
The stale Mac 1× Filter item is **accepted**: the 24 October signed arm64
candidate with DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`
passed the 760 × 600 native screenshot, complete inline error, muted
VoiceOver field/error/return speech and unchanged fixture
(`mac-filter-return-1x-visual-r16.json`, `mac-filter-return-1x-r15.json`).
Windows 150%/200% and Ubuntu 200% Filter routes also passed as recorded below;
FCUX-028 is not held open for another Mac 1× run.

<a id="evidence-local-stop-20261026"></a>
Local Text/Hex visible Stop
is **accepted** on Windows and Ubuntu at 100%, 150% and 200%, plus Mac
Retina with actual muted VoiceOver `Search canceled.` speech. The remaining
FCUX-007 scenario is remote cancellation/query ownership/provider error,
not local Stop reconfirmation. Native Markdown Page search and prose/table
resize are **accepted** on Windows WebView2, Ubuntu WebKitGTK and macOS
WKWebView at their recorded sizes/builds; the remaining ledger case is one
untested scale/accessibility boundary, not a rerun of those same routes.
Truthful directional Page match lower bounds are accepted on Windows/Mac;
an engine-dependent exact total is optional.

<a id="evidence-mac-pointer-20261026"></a>
**26 October 2026, Mac narrow Copy X-click diagnostic (UXBUG-016):**
The signed `be7b2a30` app passed the normal native Copy/Move/Delete modal
guard again on an unlocked Retina Mac. A subsequent Git-tracked pointer
runner at a 640 × 480 main window found a 30 × 30 enabled AX button named
`Close dialog` in Copy but timed out after a native click. The owner-approved
`FileCatCapture.app` diagnostic image showed an unrelated macOS iCloud alert
covering the X; a FaceTime alert followed its dismissal. Both were cleared
through their visible Later buttons. The single bounded retry on the *same*
signed build passed native X and backdrop clicks, retained its accessible
in-window approval button and left source and destination unchanged
(`mac-dialog-pointer-copy-narrow-be7-retry-20261026.json`). The first report
(`mac-dialog-pointer-copy-narrow-be7-resume-20261026.json`) remains as
historical invalid/occluded evidence. This closes `DEF-016A`; it did not
demonstrate a FileCat regression or justify repeating the previously passed
Move/Delete pointer routes.

**26 October 2026, VoiceOver speaks viewer Stop (FCUX-007):**
The signed isolated Mac app with DLL SHA-256
`65c05d26f0cfff585e903768fd3acbf70941b181e0dd8e5831a1acec6ca0e4d6`
passed native Hex and Text searches over separate test-owned 2 GiB sparse
files. The bounded direct AX helper activated visible Stop, and muted
VoiceOver's actual last phrase became `Search canceled.` in both modes. Two
consecutive runs of each mode passed after the runner waited for FileCat's
first AX window. Each retained its source length and sampled zero bytes,
empty target and original text clipboard; the exact app process exited and
the large source was removed. Remote provider and spoken error routes remain
open.

**26 October 2026, Orca speaks viewer Stop (FCUX-007):**
The Ubuntu 100% native Hex and Text long-search Stop journeys now also start
one isolated Orca instance and inspect its actual speech output. Orca spoke
`Search canceled.` after the visible Stop action in both modes; the visible
status, retired Stop, clipboard sentinel, sampled 2 GiB source bytes and
empty target passed in the same runs. The runner rejects an existing Orca,
keeps its debug log in the test-owned fixture and excludes the transient sudo
credential from Orca and FileCat. The VM was restored to 1280 × 800, 100%
after VMware reset its display. Remote/provider error speech remains open;
Mac spoken Stop passed later on the signed app.

**26 October 2026, narrow Mac transfer reviews (FCUX-004, UXBUG-010):**
The direct AX modal guard resized the exact signed FileCat main window to
760 × 600 logical points on the Retina Mac, then passed Copy, Move and Delete
with all six background-command attempts per review, unchanged exact source
hash, empty target and safe Cancel. The owner-approved capture helper recorded
all three reviews. Visual inspection found each dialog within the narrowed
host, its full selected destination path where applicable, effect summary,
options and Cancel/Queue/approval buttons readable and reachable; Delete kept
its Recycle Bin wording and close X. The direct helper identifies the exact
titled main window when AX also reports a transient second window. Other
scaled and longer-path Mac dialog visuals remain open.

**26 October 2026, Mac modal visual capture (FCUX-004, UXBUG-010):**
The owner-approved `FileCatCapture.app` recorded Copy, Move and Delete reviews
inside the same direct AX modal guard. An unrelated FaceTime prompt initially
covered their center; it was dismissed through its visible Later button, then
the guard passed again and fresh Retina captures were inspected. Copy and Move
show a readable title, exact selected destination pathname, effect text,
options and separate Cancel/Queue/approval controls. Delete shows the Recycle
Bin effect with Cancel and Delete. The close X is visible and centered in all
three headers. Other scaled, longer-path and other-dialog visuals stay in the
backlog.

**26 October 2026, Ubuntu viewer Stop at original display scale (FCUX-007):**
The same isolated Git-built app passed a second native long-search Stop route
at measured 1280 × 800, GNOME 100%. Hex and Text each used a test-owned 2 GiB
sparse local source; Text switched via its accessible toggle. Both searches
showed `Search canceled.`, retired Stop, preserved the clipboard and sampled
source bytes, and left the empty target untouched. The runner now removes the
transient sudo credential from the FileCat child environment and restricts its
report to the validation root. Its earlier 200% Text journey used physical F4;
the 100% route establishes the native toggle action. The later 150% and Orca
cancellation runs passed; other providers and spoken errors remain open.

**26 October 2026, direct Mac modal-command guard (FCUX-004):**
The `mac-ax-modal-guard.py` runner now uses a bounded direct Accessibility
helper instead of `System Events`. On the signed isolated app whose DLL SHA-256
is `65c05d26f0cfff585e903768fd3acbf70941b181e0dd8e5831a1acec6ca0e4d6`,
two full native runs passed Copy, Move and Delete. Each review kept exactly
one Cancel control and its expected destination/summary while direct AX Press
tried all six background Copy/Move/Delete toolbar and key-bar actions. All
18 attempts per run left the same single review, exact source hash and empty
target; Cancel retired each review without changing the fixture. The Delete
review correctly names its Recycle Bin effect. Other Mac modal focus and
decision routes remain open.

**26 October 2026, Ubuntu native viewer Stop (FCUX-007):**
A new `linux-atspi-viewer-stop.py` native runner opened an isolated Git-built
FileCat over a test-owned 2 GiB sparse local file. At measured GNOME 200%
scale, both Hex and Text started an absent-query search and activated the
visible Stop through AT-SPI. Both runs showed `Search canceled.`, retired the
Stop button, preserved clipboard content and sampled source bytes, and left
the empty destination untouched. Text mode switched through a physical F4
from the bounded uinput helper. The executable SHA-256 in both receipts was
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`.
The Ubuntu VM returned to its original 1280 × 800, 100% display state.
Other provider, scale and spoken Stop outcomes remain open.

**26 October 2026, Mac accessibility transport diagnostic:**
The Mac still returned zero child elements through `System Events`
for both Finder and FileCat over SSH. A bounded direct Accessibility API
helper on the same isolated FileCat process read one window, traversed 496
elements and found the exact `a.txt` source row. This isolates the failure
to the AppleScript route, not the product's accessibility tree. This was a
diagnostic; the direct AX modal-command guard results are recorded above.

**26 October 2026, macOS inline Rename CI timing (FCUX-010):**
The macOS App lane on the earlier `98eb47dd` head failed one headless smoke
assertion: Escape correctly restored `sample.txt`, but a synthetic AX focus
request ran after the 100 ms stale-request guard had expired and therefore
deliberately focused the parent row. The guard condition is now a directly
testable pure function; seven deterministic cases cover the immediate and
100 ms boundary, invocation, store identity, same row and focus ownership.
The smoke test still verifies Escape restoration and deliberate navigation
after expiry without assuming a CI scheduler will deliver the synthetic AX
request within 100 ms. Both focused tests passed on Windows. Native signed
Mac focus/VoiceOver journeys had already passed; the corrected macOS CI lane
on `ae781ad0` passed. That run's producer, Windows, Ubuntu, macOS and Windows
ARM64 lanes all completed successfully. The final-head gate remains open while
the audit continues.

<a id="evidence-openssh-nonatomic-20261026"></a>
**26 October 2026, real OpenSSH non-atomic recovery transport (FCUX-001):**
The isolated Ubuntu VM checked out committed `7d1cd84b` from a Git bundle
and ran the new `OpenSshNonAtomicRecoveryTests` through a user-mode OpenSSH
server with fresh host/client keys. A test adapter reported the atomic-rename
extension as unavailable while preserving the real SFTP connection and file
operations. Four controlled faults changed same-size/time target content just
before backup rename or reported failure after the server completed backup
rename, incoming-stage publication or backup cleanup. All 4/4 cases passed:
the public path, backup, stage, local Move source and open journal matched each
phase, and no unapproved version was silently published. Windows compiled the
test and correctly skipped the four Unix-only cases. The guest log is
`sftp-recovery-test.log` under `~/FileCatValidation`; an actual server without
the extension and native recovery-prompt review remain open.

**26 October 2026, late non-atomic remote replacement race (FCUX-001):**
After a server moves the approved old destination to its journaled recovery
backup, FileCat now rereads that backup and checks the approved metadata and
content hash **before** publishing the incoming stage. It also checks that no
other client created a new entry at the public destination name. A mismatch,
read failure or new public entry leaves the incoming stage and saved old
version intact, retains the local Move source and open recovery journal, and
shows all three paths for reconciliation. The immutable metadata snapshot is
captured before the rename so a live provider entry cannot silently change
the comparison baseline. Controlled late-mutation tests cover same-size/time
content replacement, metadata changes and a new public item across SFTP,
FTP, explicit FTPS and implicit FTPS; the focused non-atomic matrix passed
27/27 on Windows. The complete Windows Remote suite then passed 2,454 tests
with 164 platform skips (2,618 total) using 16 MSBuild workers. A server
without compare-and-swap can still change the public name after this final
check. The loopback pyftpdlib server now also mutates the exact old destination
to different bytes of the same length and timestamp during its `RNFR` backup
command. Real FTP, explicit FTPS and implicit FTPS jobs all refused
publication, retained the changed backup and incoming stage, preserved the
local Move source, and left an open recovery journal (3/3 focused tests).
The complete real `FtpJobTests` group then passed 99/99 on Windows.
The same server fixture can now perform backup rename, stage publication or
backup deletion and then return FTP 451 instead of success. Nine real FTP,
explicit FTPS and implicit FTPS jobs preserved the correct public, backup,
stage and local Move source for each resulting state and left an open journal
for reconciliation; the focused matrix passed 9/9. Later races, native
recovery review and SFTP provider uncertainty remain open. The full real
`FtpJobTests` group passed 108/108 on Windows after this fixture extension.

**26 October 2026, exact-head Ubuntu checksum scopes (FCUX-008):**
The isolated Ubuntu guest ran the updated `linux-atspi-viewer-checksum.py`
against the Git-built `cff83525` app (SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`,
DLL SHA-256 `bc286bd5324c47890d539173ca996cb48ee98837d8dd8264c9c23395a69b7b6d`).
The runner now focuses the exact source-panel row through its current full
AT-SPI description and starts the app with the pinned .NET root. Native Hex
viewer checks passed both one-byte selected range and whole-file SHA-256 and
CRC-32, complete bytes-read reporting, explicit Copy matching the visible
result, unchanged source bytes and restored clipboard. A separate run changed
the selected source while its review was open: Compute refused the stale
scope, showed no hash, disabled Copy and preserved the clipboard. Reports:
`checksum-cff83525.json` and `checksum-stale-cff83525.json` in the guest
validation folder. A third native run at 200% display scale and a measured
1,440 × 1,000 physical-pixel viewer passed the selected-range and whole-file
scopes; scope, Compute, Copy, Close and selectable results all stayed within
the viewer frame. Its report is `checksum-narrow-cff83525.json`; the guest
display was restored to 100% afterward. Other provider, incomplete-read and
narrow-layout routes remain open.

**26 October 2026, checksum result height at narrow viewer sizes (FCUX-008):**
A 150% Ubuntu VM attempt exposed a checksum-results field outside a measured
720 × 500 logical viewer. VMware then reset the guest display from 150% to
100% while the test was running, so that scaled observation alone cannot
establish a stable 150% regression or its closure. The viewer now bounds the
scrollable, selectable result field by the current viewer height and grows it
again when the window expands. A headless resize/result test passed with the
focused checksum suite, 14/14 on Windows. A fresh Ubuntu Git worktree at
`f6230a2d` plus this viewer fix produced an app DLL SHA-256 of
`31f5c59037098ef67d32ecba6b561ee2bd3c5a35c101cc0ce8d0a821299efc16`.
At a stable 100% scale, its native 720 × 500 viewer passed one-byte and
whole-file SHA-256/CRC-32, complete counts, explicit clipboard Copy, unchanged
source bytes and in-frame bounds for all checksum controls. Guest receipt:
`checksum-narrow-100-fixed.json`. The guest's recorded display state is again
1280 × 800 at 100%. Stable 150% native validation and other provider failure
routes remain open.

**26 October 2026, minimum-size checksum follow-up (FCUX-008):** A new
300-pixel-height headless case exposed that the checksum result still crossed
the bottom edge; at 480 × 300, the wrapped action row pushed it completely
out of view. The result now contracts to a scrollable 40-pixel minimum. At
minimum width and height, the duplicate heading and source caption collapse;
the viewer title and complete selectable result retain source identity, and
the scope control exposes the source as accessible help text. Four headless
width/height combinations (480/720 × 300/500) pass, including expansion and
restoration. A fresh Ubuntu Git build with DLL SHA-256
`98a89f32e5a83ea5f5eacd3f03947bd5f5167849bf5c91d1dd24be5df6b22561`
then passed the native 480 × 300 viewer at 100%: one-byte and whole-file
hashes, exact read counts, explicit clipboard Copy, every checksum control
inside the measured frame and unchanged source bytes. Guest receipt:
`checksum-minimum-100-fixed.json`. The Windows Release App suite before this
minimum-size refinement passed 2,490 tests with 63 platform skips; the
refined four-case layout test passed afterward. A fresh Windows VM Git build
at `65725e40` (app SHA-256
`580B26F00C742E190C4AEED2AC18CF53F25194AA8FE99B6BFB2AF554DF6F32B7`)
then passed the tracked `checksum-minimum-native` journey at 100% scale. Its
480 × 300 logical-pixel request produced a bounded 496 × 339 Windows outer
frame; measured scope, Compute, Copy, Close and results stayed inside the
viewer. The one-byte SHA-256/CRC-32 and byte count were exact, Copy published
the full visible result, Compute left the clipboard untouched, and the source
and destination fixtures stayed unchanged. The guest receipt is
`checksum-minimum-scale100-20261026-min2.json`. Other native scales and
provider failure routes remain open.

**26 October 2026, scaled Windows minimum checksum viewer (FCUX-008):**
The same isolated Git-built `65725e40` app passed the minimum-size native
journey at Windows 150% (144 DPI, measured 742 × 506 outer pixels) and 200%
(192 DPI, measured 986 × 671 outer pixels). At both scales the scope,
Compute, Copy, Close and result controls stayed inside the actual viewer
frame. The one-byte hashes, exact read count, clipboard preservation until
explicit Copy, copied full result, untouched fixture and graceful isolated
app exit all passed. Windows Display Settings confirmed each selected scale
and was closed after use; the guest was restored to its original 100% scale.
Receipts: `checksum-minimum-scale150-20261026-min1.json`,
`checksum-minimum-scale200-20261026-min1.json` and
`checksum-minimum-scale-restore100.json`. Ubuntu stable 150% and other
provider failure routes remain open.

**26 October 2026, Ubuntu 200% minimum checksum viewer (FCUX-008):**
The isolated Git-built Ubuntu app DLL SHA-256
`98a89f32e5a83ea5f5eacd3f03947bd5f5167849bf5c91d1dd24be5df6b22561`
passed the native `--minimum` journey at a measured GNOME 200% display scale.
Its 480 × 300 logical request produced a 960 × 600 physical viewer frame.
Scope, Compute, Copy, Close and selectable results stayed inside that frame
for both one-byte and whole-file SHA-256/CRC-32 runs; byte counts, explicit
Copy, unchanged source and empty destination passed. Guest receipt:
`checksum-minimum-200-current.json`. A later display-state read recorded the
guest back at 1280 × 800 and 100%; VMware can still reassert a prior scale
when a new interactive program starts. Stable 150% and other provider
failure routes remain open.

**26 October 2026, exact-head Ubuntu Text viewer Find (FCUX-007):**
The isolated Ubuntu GNOME guest ran `linux-atspi-viewer-find.py` against the
Git-built `cff83525` app (SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`).
The runner now waits for the source panel's complete `a.txt, file, ...` row
description and passes its pinned .NET root to the app host. Native AT-SPI
opened the Text viewer, found `alpha`, showed `1 match`, exercised visible
Previous and Next, then physical F3 and Shift+F3 with the correct directional
boundary messages. The viewer closed and both source files retained their
exact bytes. Guest report: `text-viewer-find-cff83525.json`. The full Windows
Remote suite on this head passed 2,442 tests with 164 platform skips; the
five-lane CI run was green. Other provider, scale and spoken Stop/error routes
remain open.

**26 October 2026, narrow Ubuntu Text Find (FCUX-007):** The tracked native
runner now measures the Find field, Previous/Next buttons and exact count
inside a 720 × 500 physical-pixel viewer. A Git-built Ubuntu app from
`f6230a2d` plus the checksum layout fix (DLL SHA-256
`31f5c59037098ef67d32ecba6b561ee2bd3c5a35c101cc0ce8d0a821299efc16`)
passed at stable 100% scale: `alpha` showed `1 match`, Previous/Next and
physical F3/Shift+F3 returned the correct directional boundary messages, the
count stayed visible, both source files retained their exact bytes, and the
viewer closed. The measured native frame was 720 × 500, with every search
control inside it. A repeat then changed the query to `beta`: the old
`1 match` count retired, a fresh Find reported `0 matches` and the correct
no-later-match message, and the fixture stayed byte-exact. Guest receipts:
`text-find-narrow-100-current.json` and
`text-find-narrow-query-100-current.json`.
Remote-backed, other scale and spoken provider-error/Stop routes remain open.

**26 October 2026, minimum-size Ubuntu Text Find (FCUX-007):** The native
AT-SPI runner now accepts `--minimum` and requires a bounded viewer frame
after requesting 480 × 300 physical pixels. On the isolated Git-built Ubuntu
app, the measured frame was exactly 480 × 300 at stable 100%; Find, Previous,
Next and the `1 match` status all remained inside it. Physical F3 and
Shift+F3 produced the directional boundary messages. Replacing `alpha` with
`beta` retired the old count and showed `0 matches`; both source files stayed
byte-exact and the viewer closed. The guest receipt is
`text-find-minimum-100-verified.json`. Other scales, remote-backed searches
and spoken provider-error/Stop routes remain open.

**26 October 2026, scaled Ubuntu minimum Text Find (FCUX-007):** The native
runner now derives physical window size from `gdctl` and requires a measured
frame near the 480 × 300 logical request before checking its controls. At
measured GNOME 200% it passed with a 960 × 600 frame: Find, Previous, Next
and the exact count stayed inside it; physical F3/Shift+F3 returned the
correct directional boundaries; replacing `alpha` with `beta` retired the
old count and showed `0 matches`; source bytes remained unchanged. Guest
receipt: `text-find-minimum-scale200-current.json`. During an attempted
post-200% repeat, VMware changed the display back to 200% as the interactive
app started; that repeat was measured as another 200% run and is not used as
100% evidence. The earlier `text-find-minimum-100-verified.json` remains the
measured 100% receipt. Stable 150%, remote-backed and spoken Stop/error
routes remain open.

**26 October 2026, current Ubuntu Orca speech (FCUX-004/007/010/031/040):**
The isolated GNOME guest ran `linux-orca-speech.py` against the Git-built
`cff83525` app (app SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`,
DLL SHA-256 `bc286bd5324c47890d539173ca996cb48ee98837d8dd8264c9c23395a69b7b6d`).
The runner now passes its pinned `dotnet-sdk` root to the framework-dependent
app host in VMware sessions. All 49 physical-key and actual Orca speech steps
passed: both panel Tab directions, file focus and marked state, Copy/Move and
Delete review entry/return, rejected Copy destination, inline and Quick filter
errors, viewer find count and miss, boundary resize, and complete two-item
rename mapping paths. The unchanged source hashes and empty target were
checked. The guest report is `orca-speech-cff83525-r2.json`; provider errors,
viewer Stop and other decision routes still need spoken acceptance.

**26 October 2026, exact-head Ubuntu modal command ownership (FCUX-004):**
The fresh Ubuntu VM Git build of `cff83525` (app SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`)
passed the native AT-SPI Copy-review guard. Its runner now waits for the
source row and identifies it within the source panel by the complete
accessible description (`a.txt, file, ...`) rather than the older exact
name. Background toolbar and key-bar Copy/Delete accessibility actions all
left one review and its Destination field intact; Cancel closed it, the
source SHA-256 stayed
`713e5c5a199714f532fa8992ee58eb3dde1fd2c2a351f8dd53bc37d1e576c395`,
and the target stayed absent. The guest report is
`modal-guard-cff83525-r5.json`; no test FileCat process was left running.
Other modal decisions and actual Orca speech remain open.

**26 October 2026, Ubuntu Copy/Move/Delete modal command ownership
(FCUX-004):** The tracked AT-SPI runner now opens each of the three reviews
against one disposable source file. During each review, it invokes the
obscured Copy, Move / rename and Delete toolbar controls and Copy, Move and
Delete key-bar controls. All 18 background actions left the same single review
open, including its exact source summary or Recycle Bin prompt; no destination
field disappeared or extra review appeared. Cancel closed each review. The
source SHA-256 remained
`713e5c5a199714f532fa8992ee58eb3dde1fd2c2a351f8dd53bc37d1e576c395`
and the target stayed absent. The Git-built `cff83525` app SHA-256 was
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`;
guest receipt `modal-guard-current.json`. Other modal decisions and actual
Orca speech still need native acceptance.

**26 October 2026, Windows Copy/Move/Delete modal ownership (FCUX-004):**
The expanded `win-ax-modal-guard.ps1` ran on the isolated unlocked Windows VM
against its Git-built `2f1f43c9` app (EXE SHA-256
`90EBAA1835475477AD8FE1D3611E6AE94652A3E121D2887399F46020014A4519`).
Each review kept exactly one source summary or Recycle Bin prompt after UI
Automation invoked all six obscured toolbar/key-bar Copy, Move and Delete
commands: 18 background attempts total. Cancel closed each review, the exact
source SHA-256 stayed
`713E5C5A199714F532FA8992EE58EB3DDE1FD2C2A351F8DD53BC37D1E576C395`,
and no target appeared. The guest receipt is `modal-guard-expanded-current.json`.
Final-head native replay and other decision routes remain open.

**26 October 2026, Mac panel Tab and restored splitter focus (FCUX-010/031):**
The native AX journey exposed a panel switch that briefly focused the target
row, then let a late accessibility request move focus to the old panel's Filter
field. FileCat now consumes the Tab/Shift+Tab panel command and keeps the
chosen panel's short-lived keyboard ownership through duplicate routed Tab
events. The existing panel editor automation peer rejects only stale focus
requests for the old panel, while a new physical key or pointer press permits
intentional navigation. Ten consecutive native `--tab-only` journeys passed
both directions with unchanged fixtures on a signed Mac candidate. The full
journey then exposed the same 60 ms focus reversal after Escape restored a
panel splitter; its automation peer now rejects a stale request for that
retired splitter while allowing a fresh user navigation. Both subsequent full
muted Mac focus and VoiceOver journeys passed on candidate DLL SHA-256
`A4762CEFABAC473B52CD03B99D57091FBDBE22A0D0780C00C56342915460ACF7`.
Headless regressions cover Tab consumption, stale and deliberate panel-editor
AX focus, stale and deliberate splitter AX focus, and the existing pointer
focus guard. The remaining native scale and nested-boundary checks are in the
remaining-work file.

**26 October 2026, remote non-atomic Replace recovery (FCUX-001):** After an
upload's final destination metadata and content check, a server may report
that atomic replacement is unavailable. The fallback rereads the target and
its approved content and refuses an observed intervening change or removal.
It then durably journals a unique hidden backup name, moves the approved old
version there, publishes the incoming stage, and removes the backup only
after checking that it still represents the old version. A failure or
uncertain server result at either rename retains the incoming stage, keeps
the local Move source, and leaves an open recovery journal with the public,
backup and stage paths. A backup cleanup failure also retains the journal
but reports that the incoming file was published. The recovery prompt now
distinguishes the previous destination version from incoming upload debris
and warns against deleting either before reconciliation. Controlled tests
cover SFTP, FTP and both FTPS modes, target changes with unchanged size/time,
and failures before or after each server transition. A remote link uses the
same recovery backup only when that server renames the link itself; servers
known to rename the link's target refuse replacement without changing either
item. All 95 `RemoteEditTargetPublicationTests`, the existing link-replacement
test, and four recovery report tests pass on
Windows with 16 MSBuild workers. The real FTP/FTPS `FtpJobTests` suite passed
96 cases. A server without compare-and-swap can still change the target
between FileCat's final check and rename; native late-race and failure
acceptance remain open.

**26 October 2026, macOS inline Rename focus (FCUX-010):** A clean signed
Git build reproduced Escape briefly restoring `c.txt` before native AX
focused an older `a.txt`, `b.txt` or parent row. Instrumentation showed an
`AX` focus request for row zero 26 ms after the editor closed, while the
listing model still held `c.txt`. `FileListControl` now ignores a mismatched
native focus request for at most 100 ms after returning to the exact renamed
item; the guard is tied to the same store identity, leaves Invoke untouched
and expires so intentional VoiceOver row navigation works. The Mac headless
regression checks both immediate stale focus and later explicit navigation.
Ten consecutive native `--decision-returns` journeys passed on the isolated
Mac with signed candidate DLL SHA-256
`4B00FD9D7F917DF09682A952DA9B299D1419AA460A133D75450C390CA88485B3`;
each canceled Create folder, Delete and Rename without changing fixture
bytes. The tracked journey now also checks VoiceOver's actual last spoken
phrase with Mac output muted: three further runs spoke `c.txt table row (4 of
4)` after Rename cancellation. Full CI on the pushed head remains pending.

**26 October 2026, owned FTPS CI fixture (FCUX-001 validation):** The macOS
`915ec37d` CI lane failed after deleting an owned FTPS staging object because
Python's `ftplib` raised `SHUTDOWN_WHILE_IN_INIT` while unwrapping the final
TLS listing data channel. The Mac reproduced this under 16-worker load (3 of
64 fixture runs failed); the server stayed alive, and the error was in the
client's data-channel shutdown after `DELETE`. The inspector now closes the
out-of-sync control session and checks the final remote manifest on a fresh
certificate-pinned FTPS session. Only this precise TLS shutdown error is
retried, at most five times with short backoff; other TLS, certificate and
manifest failures still fail. The deterministic explicit and implicit FTPS
tests inject that shutdown after a real remote deletion, and the original
FTP/FTPS fixture plus both injected cases pass. The isolated Mac then passed
128 complete fixture runs with 16 workers. Full five-lane CI on the pushed
repair remains pending.

**26 October 2026, macOS Tab focus and CI diagnostics:** A repeated native
`mac-ax-focus-speech.py` run found that one Tab command can switch to the
target file row, after which macOS focuses Location or Filter in the old
panel and reactivates it. A headless test reproduced the managed transition,
but signed Mac candidates with 120 ms and 350 ms delayed focus restoration
still allowed another native traversal. Moving the command after Tab key-up
passed five native forward checks but later failed the reverse Tab route.
Those candidates were not shipped. The tracked Mac AX runner now offers
`--tab-only` to require both directions and unchanged fixture content; the
later keyboard ownership repair is recorded above. The separate inline
Rename cancellation failure is addressed in the entry above. CI for
`915ec37d` failed its macOS owned-FTPS stage
fixture while the isolated Mac passed 20 consecutive repetitions. The Python
fixture now reports captured child stdout/stderr and server exit status on
failure, which exposed the TLS teardown error reproduced and addressed above.

**26 October 2026, macOS checksum generation CI (FCUX-004/008):** The
`64fdac75` macOS App lane had one failure among 2,547 cases: the
`ChecksumDialogGenerationTests` worker did not post its held continuation
within 20 seconds for the Replace/SHA-512 case. The test had used a
`FileShare.None` handle to force a read error on every desktop, while FileCat's
Unix checksum reader uses a native regular-file open with different sharing
semantics. The generation test now keeps the real file-error route on Windows
and a real successful worker completion on Unix; both must be ignored after
replacement or closure, and the separate read-failure suite still tests
errors. The 20-second guard remains. All six revised cases passed on the
isolated Mac (`mac-checksum-generation-8508db4b.log`) and Windows VM
(`.local-win-checksum-generation-r1.json`). The four separate Mac
`ChecksumDialogReadFailureTests` also passed
(`mac-checksum-read-failure-8508db4b.log`). Full five-lane CI on the revised
head is pending.

**26 October 2026, actual muted Mac VoiceOver viewer Find (FCUX-007):**
The tracked `mac-voiceover-list.py` now has a `--viewer-find` journey. The
isolated Mac published a directly signed ARM64 app from exact Git revision
`8508db4b338a65a5ce08fffdc493bffa58d5b6f5`, with DLL SHA-256
`8622f77cd3c8bb567df604cd19d4487bd70e56caa8cbddddd7d7fa0bbeb9104c`.
With the output muted, VoiceOver's actual last-phrase interface spoke
`1 match`, then `"aaa" was not found after this position.` after a changed
query in the Text viewer. The native route closed the viewer and preserved
all three source hashes and the empty destination
(`.local-mac-voiceover-viewer-find-8508db4b.json`). A separate AX focus
journey on the same app stopped before its Find step because a Tab briefly
focused the target parent row then settled on Filter. That focus transition
remains an independent native acceptance issue; it does not qualify the
passed VoiceOver Find result.

**26 October 2026, native Ubuntu spoken viewer miss (FCUX-007):** The
tracked `linux-orca-speech.py` journey now replaces a successful Find query
with an absent term and requires Orca's actual `SPEECH OUTPUT` to contain
`"aaa" was not found after this position.` On the isolated Ubuntu VM, Orca
50.2 passed all 49 native keyboard, dialog, review and speech steps with
the Git-built app DLL SHA-256
`73307647c4b3511af942958d1d13bf7d7e575c1127dfff4961d454de3c7de0c9`.
The build had zero warnings or errors; the report's source hashes and empty
target remained exact (`.local-ubuntu-orca-search-outcome-r1.json`). Native
Stop/error speech on Ubuntu and the remaining Mac outcomes remain open.

**26 October 2026, native Windows spoken search Stop (FCUX-007):** The
tracked `win-nvda-hex-search.ps1` journey now runs the native viewer Stop
case after its Hex editor Find route. On the isolated Windows VM, signed
portable NVDA 2026.2 spoke `Search canceled.` after the physical Stop click
in a 2 GiB viewer search. The native runner independently required the Stop
button to hide, the clipboard and source bytes to remain unchanged, and the
target to remain empty. The report
`.local-win-NvdaHexViewerStop-r2.json` passed the three editor match-count
utterances, the new Stop utterance, both native routes and cleanup, with
the Git-built App DLL SHA-256
`D74B8708FFC089672545451C5801F59B83E28A0101EE4670AD82A4E43299C092`.
The first run's native and NVDA logs showed the same outcome, but its VM-only
interactive launcher stopped waiting before the wrapper wrote a report; the
launcher deadline was extended for the complete second run. Native provider
error speech and Stop outcomes on the other desktops remain open.

**26 October 2026, spoken viewer Find outcomes and Ubuntu CI timing
(FCUX-007):** The viewer now makes completed misses, Stop outcomes, provider
read failures and invalid Hex patterns polite live status changes. In-progress
search and ordinary position updates remain silent. The visible status text
supplies its changing accessible name, so the screen reader hears the actual
outcome. Seven focused App cases passed in the isolated Windows VM: a complete
miss and all six Text/Hex held-read Stop/failure combinations
(`.local-win-search-outcome-r1.json`). The complete search-scope, Find-control
and live-region naming classes then passed all 37 cases in the same VM
(`.local-win-search-outcome-r2.json`). A native Windows journey using signed
portable NVDA 2026.2 then heard the completed `"aaa" was not found after this
position.` phrase from its speech log and passed the rest of its keyboard,
filter, dialog and review checks (`.local-win-SearchOutcomeNvda-r1.json`).
Native Stop/error speech and the other desktop routes remain open.

An earlier Ubuntu CI lane for `3ae11467` failed only the existing stalled
theme-helper timing assertion: 3.68 seconds elapsed against a 3-second
deadline on the loaded runner, even though the 500 ms process timeout had
returned null. The regression now stalls its helper for 15 seconds and allows
10 seconds of test wall time, retaining a clear failure margin for a genuine
blocking wait and checking the helper process is gone. The focused Ubuntu VM
test passed in 687 ms (`.local-ubuntu-theme-deadline-r1.log`). The final-head
five-lane CI gate is pending.

**26 October 2026, viewer Stop versus provider read failure (FCUX-007):**
`ViewerWindow.FindAsync` now treats a read, access or decode exception as a
canceled search when the current search token was canceled. A genuine source
error without Stop still appears as `Search could not finish:` with its
diagnostic. The held-source App test covers both outcomes in Text and Hex,
checks the visible Stop beside Next during the blocked read, and requires Stop
to hide afterward. All six cases passed in the isolated Windows VM
(`search-stop-headless-20261026-r5.json`). Native remote-provider timing and
spoken cancellation feedback remain open.

**26 October 2026, Ubuntu scaled Hex acceptance (UXBUG-020):** The combined
native `linux-atspi-hex-resize.py` runner now performs its physical thumb
drags in measured narrow viewer/editor windows, away from the display edge.
Its bounded pointer feedback compensates for the accelerated relative uinput
motion in a scaled Wayland/Xwayland session. The runner also permits up to
four reversible content-size steps before requiring the discrete Hex row to
shrink, then verifies the original row and exact Find selection return. On a
fresh Ubuntu VM login, the complete physical viewer/editor journey passed at
temporary GNOME 200% (`ubuntu-hex-narrow-thumb-scale200-r4.json`) and, after
another fresh login, at temporary 150%
(`ubuntu-hex-narrow-thumb-scale150-r1.json`; Xwayland used native 2× app
scale). At 200%, the viewer thumb moved to byte 5,220 and back to zero; the
editor moved to byte 5,664 and back. Both retained a deliberate selection
through a further thumb drag. The same search, resize, zoom and viewport
checks passed at 150%; exact source hashes and empty targets were required.
VMware returned the desktop to its original 1280 × 800, 100% mode after the
150% run. A final graceful restart and 100% rerun passed the full journey
(`ubuntu-hex-narrow-thumb-scale100-r4.json`). Earlier scaled attempts stopped
at VM pointer alignment or a one-step zoom assertion before completing the
FileCat journey. Windows 100%/150%/200% and Mac 1×/Retina journeys are
recorded below; this closes the queued Hex resize/scroll bug's native desktop
acceptance across those tested environments.

**26 October 2026, scaled Windows mask and Text search follow-up:** The
isolated Windows VM changed from 100% to 150% and ran the tracked four-panel
Psychedelic mask journey on the revised Git-built app. Native UI Automation
read `*.*` in all four visible filters with physical bounds of about
143–144 × 37–38 pixels, and the app-only capture visibly painted every mask.
The fixture stayed intact. The owner then closed the earlier elevated process
and launched `publish-main-a34092cf/FileCat.exe`; all four masks showed on
the workstation. The earlier screenshot came from that executable path, but
the process's loaded DLL identity at capture time was unavailable, so its
blank fields have no proven cause beyond the observed pre-relaunch state.
The Windows VM App suite for `a62ac3b8` passed 2,487 cases with 54
platform skips. A two-case held-read test then passed in Hex and Text modes.
At 150%, physical pointer Stop journeys on separate 2 GiB files passed in
both Hex (`search-stop-scale150-20261026-r2.json`) and Text
(`search-stop-text-scale150-20261026-r3.json`): canceled status, hidden Stop,
unchanged clipboard, exact small fixtures and sampled large-file bytes, and
graceful app close. The VM display returned to 100% with a passed receipt.

**26 October 2026, Windows Text Stop at 200% (FCUX-007):** A fresh isolated
Git-built `65725e40` app (EXE SHA-256
`580B26F00C742E190C4AEED2AC18CF53F25194AA8FE99B6BFB2AF554DF6F32B7`)
passed `search-stop-text-native` after Windows Settings selected 200%.
On a test-owned sparse 2 GiB file, the physical Stop target fit inside the
viewer beside Next; pressing it showed `Search canceled.`, hid Stop, left the
clipboard and exact small/sample large source bytes unchanged, and exited
the isolated app gracefully. The guest then restored 100% and closed
Settings. Receipts: `search-stop-text-scale200-20261026-r4.json` and
`search-stop-text-scale-restore100-current.json`. Other providers and spoken
Stop outcomes remain open.

**26 October 2026, Windows Hex Stop at 200% (FCUX-007):** The same isolated
Git-built `65725e40` app passed `search-stop-native` at the selected 200%
Windows scale. A physical Stop click beside Next canceled a search over a
test-owned sparse 2 GiB file, showed `Search canceled.`, hid Stop, preserved
the clipboard and the exact small/sample large source bytes, and closed the
isolated app. The guest restored 100% and closed Settings. Receipts:
`search-stop-scale200-20261026-r4.json` and
`search-stop-hex-scale-restore100-current.json`. Other provider and spoken
Stop/error routes remain open.

**26 October 2026, Windows Text Stop at 100% (FCUX-007):** The same Git-built
`65725e40` app completed the remaining Windows local Text Stop scale route
at the restored 100% display setting. On another isolated sparse 2 GiB file,
the physical Stop target was inside the viewer; cancellation appeared,
Stop hid, the clipboard and small/sample large source bytes stayed exact,
and the owned app closed. Receipt:
`search-stop-text-scale100-20261026-r4.json`. Windows Hex and Text now have
native long-search Stop coverage at 100%, 150% and 200%; remote providers,
other modes and spoken Stop/error outcomes remain open.

**26 October 2026, visible long-search Stop (FCUX-007):** The viewer keeps
Stop search immediately beside Next while a long search runs, so a long match
count cannot displace its cancellation control. A blocked-read App test holds
the content source during an active Find, confirms Stop is laid out and
visible next to Next, clicks it, releases the read, and requires the canceled
status and hidden Stop. It passed in the isolated Windows VM. The same VM
built the revised app and native harness, opened a test-owned 2 GiB local
file in Hex view and physically clicked Next then Stop before the automation
snapshot could finish the search. The `search-stop-scale100-20261026-r2.json`
receipt reports `Search canceled.`, hidden Stop, unchanged clipboard, intact
source samples and four small fixture files, empty destination, and graceful
viewer/app close; the large file was removed. The first attempt took a full
automation snapshot before clicking Stop, let the search finish, and was
corrected in the native runner. Native scaled, provider, other mode and spoken
Find routes remain open. The workstation `vstest` process did not connect to
`testhost` before its 90- and 180-second limits; the VM test ran and passed.

**26 October 2026, four-panel workstation mask follow-up:** A new screenshot
from the running elevated workstation app shows the default masks blank in all
four address rows. This matches the native Windows VM reproduction from before
the `PanelAddressTextBox` template fix. The fixed Git build painted `*.*` in
all four VM panels. The new tracked
`win-ax-panel-mask-theme.ps1` repeated the four-panel journey with physical
pointer input and the screenshot's Psychedelic theme on the `2f1f43c9` VM
build. Its `panel-mask-theme-tracked-20261026-r9.json` receipt records four
visible `*.*` fields and untouched fixture files; the paired native PNG
captures only the app window and visibly paints each mask. The tracked runner
rejects paths outside the guest's `FileCatValidation` root. The headless
regression also covers this theme.
A fresh local Release build from `a34092cf` is available at
`src/FileCat.App/bin/Release/publish-main-a34092cf/FileCat.exe` (app DLL
SHA-256 `a4a9100a760b135f717844f01709c88e6944101f54194545a8d9b37c7f199834`).
The elevated workstation process was left untouched during VM testing; the
owner later completed the fresh-build relaunch as recorded above.

**26 October 2026, Windows checksum source changed during a read (FCUX-008):**
The isolated Win11 VM used the Git-built `2f1f43c9` app to open a test-owned
128 MiB local source and review its Whole file checksum scope. The native
runner waited until the exact FileCat process had spent 281 ms computing,
then changed byte zero and the source revision 401 ms after Compute began.
FileCat refused the changed file with an explicit instruction to reopen
Checksum, displayed no SHA-256 or CRC-32, disabled Copy, preserved the
clipboard, and left the small fixture files and destination untouched.
The viewer and isolated app closed and the 128 MiB file was removed. The
guest receipt is `checksum-midread-scale100-20261026-r6.json`, with an ignored
host copy. The tracked `checksum-midread-native` runner checked all four
small fixture files. Native incomplete sources and mid-read changes
on other supported providers and desktops remain open.

**26 October 2026, Windows checksum layout across display scales (FCUX-008):**
The isolated Win11 VM rebuilt the native acceptance harness and restored the
Microsoft-signed WinAppDriver 1.2.1 to a guest-local validation folder, serving
only loopback. A fresh one-byte scope and explicit Copy journey passed at 100%
on the Git-built `2f1f43c9` app. The first 150% run proved the correct hash,
unchanged clipboard until Copy, visible result and intact fixture, but the
runner incorrectly requested 600 × 420 *physical* pixels below the scaled
window minimum. The runner now validates the configured Git-built executable
when resizing and translates the requested 600 × 420 *logical* pixels through
the viewer's measured per-monitor DPI. Fresh reruns passed at 150% (144 DPI,
900 × 630 physical pixels) and 200% (192 DPI, 1200 × 840 physical pixels):
each kept the complete checksum result and Copy control visible, verified the
exact SHA-256, CRC-32, byte count and revision, required explicit Copy, closed
its own app, and left the source and destination unchanged. Receipts are
`checksum-narrow-scale100-20261026-r2.json`,
`checksum-narrow-scale150-20261026-r2.json` and
`checksum-narrow-scale200-20261026-r1.json` under the guest validation folder,
with ignored host copies. The VM display returned to its original 100%.
Incomplete sources, other provider and desktop mid-read mutation, and scaled
Ubuntu native layout remain open. The full local Windows Release solution suite at 16 workers
passed with zero failures: Core 4,442 passed/151 platform skips, Remote 2,422/164,
App 2,478/63, and Windows platform 442/46. Final-head five-lane CI remains
required.

**26 October 2026, Windows CI SFTP edit baseline save:** CI run
`40261499720` passed producer, Ubuntu, macOS and Windows ARM64, but its
Windows App suite saw a completed SFTP upload followed by a transient
`UnauthorizedAccessException` while replacing the local `session.json`
baseline. The remote bytes were already committed, so the session correctly
remained Modified rather than falsely acknowledging its new baseline.
`EditSessionStore.Save` now writes each attempt to a unique, same-folder
temporary record and gives short-lived Windows access denials a bounded six
attempts before preserving and reporting the prior record. Refused attempts
remove their temporary metadata. New Core tests force both transient
`IOException` and `UnauthorizedAccessException` and a persistent denial;
all three pass. The 34 related Core tests, two Remote edit tests and 42 App
edit/SFTP tests pass with 16 workers. The previously failing App test also
passed eight further local reruns. The pushed-head five-lane CI gate is
pending.

**26 October 2026, Ubuntu long-line comparison (FCUX-009):** The new tracked
`linux-atspi-compare-long-lines.py` uses two isolated source files whose first
and second differences follow 2,000 shared characters. On the native 100%
Wayland VM, it opened Compare with a physical Ctrl+I, copied the entire
2,010-character first left line, used physical F8, and copied the entire
2,008-character second left line. In a requested 640 × 600 window, the
accessible Wrap control became checked and Copy line still returned the exact
second source line. The prior clipboard was restored, the test-owned fixture
was removed, and both source byte streams were unchanged. The passed guest
receipt is `ubuntu-compare-long-20261026-r3.json` (Git-built app SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`).
The guest checkout had advanced to `38bc114c` without rebuilding this app;
the binary hash matches the previously tested `df449513` build. Native Linux
pixel alignment, pointer selection, font fallback and spoken feedback remain
open.

**26 October 2026, Ubuntu scaled Hex pointer follow-up (UXBUG-020):** A
temporary GNOME 200% mode let the tracked combined runner reach its exact
test-owned file after accepting either native accessible row-name form (bare
filename or filename with metadata). Physical uinput movement then left
Xwayland's reported cursor jumping between positions and stuck near the
screen edge; the runner stopped before the scrollbar drag. This reproduces
the VM input-coordinate limitation seen at temporary 150%, not a product
scrollbar result. The diagnostic receipt is
`ubuntu-hex-editor-combined-scale200-20261026-r2.json`. The VM display was
restored to 100% and cleanly restarted. The same revised runner then passed
the full viewer/editor physical thumb, explicit selection, Find, resize and
zoom journey on the Git-built `df449513` app. The viewer thumb reached byte
2,424 and returned to zero; the editor reached byte 2,544 and returned to
zero, without accidental selection. Source bytes and empty target were
verified; receipt `ubuntu-hex-editor-combined-after-reboot-20261026-r1.json`.
Stable scaled-session physical-pointer acceptance remains open.

**26 October 2026, Windows scaled Hex editor and viewer (UXBUG-020):** The
isolated Win11 VM selected 150% (144 DPI) and 200% (192 DPI) through the
native Display Settings control. At each scale, the tracked
`win-ax-hex-scrollbar.ps1` passed its full physical-pointer editor journey
on the Git-built `2f1f43c9` app: the thumb scrolled down and back without
creating a selection, a later drag preserved an explicit selection, Find
selected bytes 3,000–3,005, and the same selection survived window reflow,
zoom and zoom restoration. At 150%, the editor row changed 16→24→20→24
bytes; at 200%, 16→20→16→20. The viewer route separately passed both
physical thumb drags and 8→20-byte row reflow at each scale. All four runs
verified the unchanged source SHA-256, empty target and removal of the
test-owned fixture. Receipts are
`hex-editor-combined-scale150-20261026-r1.json`,
`hex-viewer-combined-scale150-20261026-r1.json`,
`hex-editor-combined-scale200-20261026-r1.json` and
`hex-viewer-combined-scale200-20261026-r1.json` in the guest validation
folder. The display selector reported restoration to its original 100% after
each scale. Stable scaled Ubuntu physical-pointer acceptance remains open.

**26 October 2026, combined native Ubuntu Hex editor (UXBUG-020):** The
tracked `linux-atspi-hex-resize.py` now carries a test-owned `NEEDLE` at
offsets 3,000 and 6,000 through its existing Git-built viewer and editor
journey. On the restored 100% Wayland desktop, the `df449513` app (binary
SHA-256 `f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`)
passed the complete physical uinput run. The editor thumb moved the viewport
to byte 2,568 and back without implicit selection; an explicit four-byte
selection survived a further thumb drag. Find then selected exactly bytes
3,000–3,005 in the same editor. The selected range survived 16-to-24-byte
window reflow and a zoom from 24 to 20 bytes per row, then a return to 24.
The existing viewer Find, zoom, PageDown anchor and thumb checks passed in the
same process. The source hash was unchanged, the target stayed empty, and
the owned fixture was removed. The guest receipt is
`ubuntu-hex-editor-combined-20261026-r1.json`. Scaled Ubuntu physical-pointer
acceptance remains open.

**26 October 2026, combined native Windows Hex editor (UXBUG-020):** The
isolated Win11 VM ran the Git-built `2f1f43c9` app (DLL SHA-256
`af5e45be2ad6669546ca50b7575eef2eb5f043dd59b6323d34dcc81aa24490aa`)
from its logged-in desktop session. The tracked `win-ax-hex-scrollbar.ps1`
now enters Hex mode before opening the editor, then keeps that same editor
open across physical thumb drags, explicit byte selection, Find, resize and
content zoom. Its test-owned 8,192-byte fixture contains `NEEDLE` at offsets
3,000 and 6,000. Find selected exactly bytes 3,000–3,005; narrowing and
widening changed the row from 16 to 24 bytes while preserving that selection;
two zoom steps fitted 20 bytes, and restoring size refitted 24. The thumb
visited byte 2,160 and returned to zero without implicit selection, and a
later thumb drag preserved an explicit four-byte selection. The revised
viewer route also passed, with 16-to-24-byte reflow and a 2,184-byte thumb
viewport. Both reports verify source SHA-256, an empty target and safe fixture
cleanup: `hex-editor-combined-20261026-r3.json` and
`hex-viewer-current-20261026-r1.json` in the VM validation folder. The
temporary interactive task uses the logged-in token and stores no password;
VMware's direct guest-process launch ran in session 0 and could not send
pointer input. The pushed `e48ef5d9` head passed all five CI lanes; the later
heads still need their own final gates.

**26 October 2026, combined native Mac Hex editor (UXBUG-020):** The tracked
`mac-ax-hex-editor-resize.py` now keeps one signed, Git-built editor open for
physical thumb scrolling and explicit byte selection, Find, window reflow,
content zoom and restoration. Its owned 8,192-byte fixture contains two exact
`NEEDLE` ranges away from the first row. Find reached offset 3,000 and
selected exactly bytes 3,000–3,005; the selection survived narrow and wide
frames and zoom. The revised runner tolerates macOS thousands separators in
accessible byte offsets and bounds physical thumb hit-test retries, requiring
real viewport movement with no accidental selection. At Retina, the final
run fitted 16 bytes at 721 × 501, 28 after expansion, then 24 on zoom, and
restored 28; its first thumb attempt missed and the second reached byte 1,760.
At native 1×, the full run fitted 16 bytes at 720 × 500, 28 after expansion,
then 24 on zoom, and restored 28. Both runs preserved exact source SHA-256;
the display helper restored the original Retina mode after the 1× run.
Receipts are `mac-hex-editor-combined-1x-20261026-r6.json` and
`mac-hex-editor-combined-retina-20261026-r3.json` in the isolated Mac
validation folder. The pushed `e1b7d048` head passed all five CI lanes; the
later audit increment still needs its own final-head check.

**26 October 2026, panel-mask screenshot and Ubuntu Hex follow-up:** The
owner's four-panel purple screenshot file was captured at 22:12 on 25 October;
the missing-text fix was committed at 23:28 that day (`2f1f43c9`). The exact
Git-built Windows VM app has since displayed `*.*` in all four panels under
Psychedelic, and the four-panel theme regression remains in the App suite.
The Ubuntu Hex native runner now activates the exact process window, waits for
its asynchronous file row using the full accessible item name, and keeps the
scaled viewer/editor widths and scrollbar inside the guest display. After a
clean Ubuntu VM reboot, its full 100% Wayland journey passed against the
Git-built `df449513` app: the viewer and editor reflowed, physical thumb drags
scrolled down and back without selecting bytes, explicit content selection
survived further thumb drags, Find/zoom remained correct, source bytes were
unchanged and the target stayed empty. The guest receipt is
`ubuntu-hex-resize-scale100-final.json`. A temporary 150% GNOME mode was also
tried: the runner reached the Hex viewer, but Xwayland reported a cursor that
did not follow physical uinput movement; an XTest warp made its reported
position reach the thumb while the physical drag still left the viewport at
byte zero. This is inconclusive for scaled product behavior. Restarting only
Xwayland also broke the 100% pointer route; a clean VM reboot restored it.
Stable scaled-session physical-pointer acceptance remains open.
An attempted persistent 150% GNOME mode was reset to 1280 × 800 at 100% by
VMware's guest display auto-sizing during a clean reboot; the guest remains in
its original display mode.

**26 October 2026, macOS CI follow-up:** Runs `40250366099` and `40250993156`
each passed producer, Windows, Ubuntu and ARM64, but the Mac lane exposed two
different timing-sensitive test assumptions. The Core SMB failure classifier
processed an 84,013-character repeated-prefix input in 583 ms on a busy
runner, exceeding a 500 ms assertion. Its test now allows three seconds for
that bounded input, still rejecting the former quadratic walk; the separate
allocation test keeps its 8 MiB bound. The App headless inline Rename test
injected a model focus change that macOS sometimes turned into a real editor
blur before its synthetic Escape. It now drains the queued editor-focus job,
tests Escape while the editor actually owns keyboard focus, and leaves the
displaced-row scenario to the signed native Mac AX/VoiceOver journey. The
revised App test passed 32 isolated Mac reruns and the revised Core test
passed on Mac and Windows; both focused Windows tests passed with 16 build
workers. A final-head five-lane CI pass is still required.

**26 October 2026, Mac bulk-rename accessibility geometry:** The signed
Git-built `2f1f43c9` app (DLL SHA-256
`1dad0b6faa53748d8ac9867c56e8576b88b7aeeb6e826c090a24d8a2fa953f2e`)
passed the native 760 × 600 bulk-rename editor journey twice, with VoiceOver
running and output muted. The second run asserts that the scrolling body,
Name/Extension masks, preview search and preview list have nonzero native AX
frames intersecting the body. Actual frames include a 662 × 258 body,
260 × 24 Name mask, and 662 × 280 preview list. VoiceOver spoke both complete
old/new path pairs through the explicit Previous/Next mapping controls;
focused Return/Space exercised those controls; the two-file rename preserved
both exact source contents and left the target empty. The new assertion and
receipt are in `mac-ax-rename-editor.py` and
`mac-rename-ax-geometry-2f1f43c9.json` in the isolated Mac validation folder.
The preview list itself still has no native AX row children on this bridge;
the always-visible, spoken mapping navigator provides the complete review
route. Earlier zero-frame observations no longer describe the current build.

**26 October 2026, maximum bulk-rename review:** A new headless App regression
publishes all 50,000 accepted mappings into the review and requires the UI
publication/layout phase to finish within five seconds, while realizing no
more than 100 visual rows. The only collision is in the last mapping;
Problems (1) reaches it in one action and exposes both complete source and
proposed destination paths plus the blocking reason. The four focused
`BulkRenameReviewTests` passed on Windows Release with 16 build workers. This
covers the full advertised row universe and bounded realization; a native
50,000-file end-to-end device run remains separate from the UI test.

**26 October 2026, native Mac last-row collision (FCUX-038):**
`mac-ax-rename-last-problem.py` marked 3,001 exact files in an isolated signed
`2f1f43c9` app and left one pre-existing folder outside the rename scope at
the final proposed filename. The native preview admitted all 3,001 mappings
in 0.312 seconds. The Problems action directly opened the last mapping,
including its complete old/new paths and blocking reason; the dialog's Rename
button was disabled. VoiceOver spoke that entire mapping with Mac output
muted. Cancel preserved every source file's hash, the blocking folder and
the empty target. The AX snapshot held 636 nodes, not thousands of row
controls. Two final native runs passed at Retina 2×; the spoken run's receipt
is `mac-rename-last-problem-r8-2f1f43c9.json` in the Mac validation folder.
The same full journey passed at the supported 1920 × 1200 logical/pixel 1×
mode in 0.311 seconds (`mac-rename-last-problem-1x-2f1f43c9.json`); the
display helper verified restoration to 1440 × 900 logical/2880 × 1800 pixel
Retina mode afterward.

**25 October 2026, panel mask regression:** The panel address field had been
changed from `TextBox` to `PanelAddressTextBox` for Mac accessibility focus
ownership without reusing the base TextBox style key. It consequently had a
UIA value of `*.*` and normal bounds, but no `TextPresenter`; the default
mask was invisible in native Windows, as was typed path text while editing.
`PanelAddressTextBox` now uses the TextBox template and the existing
path/filter styles. New regression tests require the filter's text presenter,
visible opacity and mask value in two-panel resizes and a four-panel workspace,
and require a path text presenter. Nine focused Windows App tests passed.
The isolated Windows VM reproduced the missing mask on the earlier Git-built
app: UIA reported `*.*` at both filter positions while the screenshot showed
no text. A fresh guest Git clone of the pushed `2f1f43c9` head built a Release
app (DLL SHA-256 `af5e45be2ad6669546ca50b7575eef2eb5f043dd59b6323d34dcc81aa24490aa`).
The native screenshot shows the mask in both panels. Physical pointer input
then created the screenshot's four-panel layout; all four `*.*` values have
visible accessibility bounds and are painted beside the path/free-space and
target controls. The guest receipt is `panel-mask-four.json`. The same
four-panel window was resized to 1,500 × 900 physical pixels. All four masks
remained visible, and the focused path editor painted its full
`C:\Users\Admin` path. The guest receipt is `panel-mask-resize.json`; final-head
CI remains open. A signed Mac ARM64 build of the same Git head (DLL SHA-256
`1dad0b6faa53748d8ac9867c56e8576b88b7aeeb6e826c090a24d8a2fa953f2e`)
passed the SFTP first-click and stale AX focus-race probe with the restored
TextBox template; source and target hashes remained unchanged. Its receipt is
`mac-panel-mask-pointer-race-2f1f43c9.json` in the Mac validation folder.
The later four-panel screenshot uses a dark palette. The four-panel regression
now switches between Classic, Cyberpunk and the screenshot's purple
Psychedelic palette, requiring a painted filter text
presenter, visible `*.*` value and in-row bounds in all four panels for each
theme. All six focused `PanelViewportLayoutTests` passed on Windows Release.
The isolated Windows VM also launched the exact `2f1f43c9` Git app with
Psychedelic settings, added the two extra panels by physical pointer input,
and captured all four painted `*.*` masks in the screenshot-specific palette
(`.local-vm-mask-psychedelic-four.png` in the host validation checkout).

**25 October 2026, Ubuntu CI diagnosis:** On `7a5492fe` and `8042cd54`, the
portable Ubuntu App test process aborted with exit code 134 after 625 and 334
passing cases respectively; the Core and Remote suites passed. The final
App TRX records no failed assertions and points to a blame sequence XML that
the workflow had not uploaded. CI now retains that bounded sequence trace.
On `2f1f43c9`, the CI trace identified
`DescendantFocusAutomationTests.Linux_attachment_enumerates_each_ancestor_down_to_the_focused_row`
as in flight. An isolated Ubuntu VM full App run of the prior candidate
independently aborted in
`AccessibilityTests.File_list_announces_the_focused_item_with_its_details`.
The row's Linux speech-mode lookup called `CreatePeerForElement(window)`
while the row peer was still constructing. Creating the root peer can resolve
the focused row and reenter that construction. The lookup now reads a weakly
tracked bridge state, updated only by the root's existing AT-SPI subscription;
it never creates another peer. The new regression checks bridge state on
attach and detach. Four focused Windows and six focused Ubuntu accessibility,
mask and path tests passed; the Ubuntu exact test had previously aborted after
60 seconds when run alone. The final green five-lane gate remains open.

**26 October 2026, Ubuntu full-suite follow-up:** The isolated Ubuntu VM
completed all 2,540 App cases without the former process abort: 2,413 passed,
126 were platform skips, and one place-button assertion failed. That test
assumed the fixture's current mount always appears in the Places bar. On this
VM the fixture lies on a filtered temporary mount, so no listed drive should
be marked current. The assertion now checks the exact current mount when it
is listed and requires no unrelated highlighted drive otherwise; it also
checks the selected drive's actual destination. The adjusted case passed on
both Windows and the same Ubuntu VM. The complete Ubuntu App rerun then passed
2,414 cases with 126 platform skips, zero failures and no process abort
(`ubuntu-app-full-orca-final.log`, 14 minutes 8 seconds). A green five-lane
final-head CI gate is still required.
The same updated guest app (DLL SHA-256
`8c258e6c0078dc3c260d5c8f96df78964a890e9bd3b56ccff68b0909727bf664`)
then passed all 48 steps of `linux-orca-speech.py` with physical keyboard
input and Orca's actual speech log. The receipt is
`orca-final-peer-fix-20261026-r2.json`; row details, marks, dialogs and
source-to-target review speech passed after the passive peer-state change.

**26 October 2026, Mac FTP interruption (FCUX-001):** On the signed
`2f1f43c9` Mac bundle, a plain FTP connection dropped after 262,144 staged
bytes. FileCat retained only an unpublished `.fc-*` stage, and the issue row
reported its path; VoiceOver spoke the partial-stage warning and path. The
source and existing target hashes remained unchanged. The passing receipt is
`mac-ftp-disconnect-speech-2f1f43c9.json` in the Mac validation folder.

**26 October 2026, Mac SFTP destination race (FCUX-001):** The native
test-owned, public-key-only SFTP server accepted a rate-limited staged upload.
After 32,739 bytes, another client changed the destination's content while
preserving its length and modification time. FileCat refused replacement,
removed the unpublished stage, and showed the changed-content issue. The
incoming source and newer destination hashes remained unchanged; the server
contained only `target.txt`. The passing receipt is
`mac-sftp-content-race-2f1f43c9-r2.json` in the Mac validation folder. The
native runner now activates the exact source tab after SFTP host-key review
and verifies the captured remote destination, independent of where macOS
returns keyboard focus. A companion native run changed both metadata and
content after the same 32,739 staged bytes; it also refused replacement,
removed the stage, and preserved source and newer destination. Its passing
receipt is `mac-sftp-metadata-race-2f1f43c9.json`.

**26 October 2026, Mac SFTP interruption (FCUX-001):** The same isolated
signed bundle staged 32,739 bytes on a test-owned loopback SFTP server. The
native runner closed only the two established sshd sessions serving that
fixture port, leaving other SSH sessions untouched. FileCat kept the exact
unpublished `.fc-*` file, offered Skip, and reported the complete remote
stage path in its finished issue. VoiceOver spoke the warning and full path.
The incoming source and existing target hashes remained unchanged. The
passing receipt is `mac-sftp-disconnect-speech-2f1f43c9.json` in the Mac
validation folder.

**26 October 2026, Win11 implicit-TLS FTPS review (FCUX-001):** The restored
Win11 VM built the exact `2f1f43c9` Git app and ran a native, interactive
test against an owned loopback FTPS server. At 96 DPI the connection showed a
certificate decision accepted for this session, listed the remote target,
displayed the full replacement risk and safer Keep both choice, cleared that
warning when Keep both was selected, then showed the per-item conflict and
accepted Skip. Source and existing target hashes remained unchanged. The
passing receipt is `ftps-implicit-warning-2f1f43c9.json` in the guest
validation folder. A restored VM Python 3.9 environment first needed
compatible `pyftpdlib`, `pyOpenSSL` and 32-bit `cryptography` test-server
dependencies; the native run used VMware's interactive guest channel to
ensure foreground-safe input. A signed NVDA 2026.2 portable copy prepared
from NV Access's stable launcher then passed the same owned, 96-DPI review.
Its speech log contains the complete replacement risk, safer Keep both choice
and per-item conflict warning; both speech assertions passed. Source and
target hashes remained unchanged. The passing receipt is
`ftps-implicit-warning-nvda-2f1f43c9.json` in the guest validation folder.
The same native journey passed at 150% (144 DPI) and 200% (192 DPI): both
warnings were visible and spoken, Keep both cleared the replacement risk,
Skip preserved the source and destination, and the VM was restored to 100%.
The guest receipts are `ftps-implicit-warning-nvda-150-2f1f43c9.json` and
`ftps-implicit-warning-nvda-200-2f1f43c9.json`. Other provider and failure
routes remain open.

**26 October 2026, Win11 FTP and explicit-TLS FTPS review speech
(FCUX-001):** The restored VM ran the same native, interactive replacement
journey against separate test-owned loopback FTP and explicit-TLS FTPS servers
with NVDA 2026.2 at 96 DPI. The explicit-TLS route accepted its certificate
for this session. Both routes displayed and spoke the full replacement risk
and safer Keep both choice, cleared the warning for Keep both, then displayed
and spoke the per-item conflict warning before Skip. The existing remote
target and local source hashes remained unchanged. Passing guest receipts are
`ftp-warning-nvda-2f1f43c9.json` and
`ftpes-warning-nvda-2f1f43c9.json`. The same two journeys passed at 150%
(144 DPI) and 200% (192 DPI), including both NVDA speech assertions,
certificate review for explicit TLS, Keep both, per-item Skip and unchanged
source/target hashes. Guest receipts are `ftp-warning-nvda-150-2f1f43c9.json`,
`ftp-warning-nvda-200-2f1f43c9.json`,
`ftpes-warning-nvda-150-2f1f43c9.json` and
`ftpes-warning-nvda-200-2f1f43c9.json`. The VM was restored to 100%.
Other failure and provider routes remain open.

<a id="evidence-ftp-native-recovery-20261026"></a>
**26 October 2026, Win11 interrupted FTP Copy and Move (FCUX-001):** The
restored VM built the Git crash harness and interrupted separate owned FTP
Copy and Move workers after each had written 786,432 bytes to an unpublished
`.fc-*` stage. For both operations, the native recovery window showed the
exact stage identity in its row, conservative cleanup dialog and report.
FileCat did not delete the remote stage automatically. The test inspected and
deleted that exact stage through FTP, then explicitly marked the retained
journal reviewed; the journal closed and the original source hash remained
unchanged. Both server directories finished empty. Passing guest receipts are
`ftp-crash-copy-2f1f43c9.json` and `ftp-crash-move-2f1f43c9.json`.

**26 October 2026, Win11 interrupted FTPS Copy and Move (FCUX-001):** The
test-owned crash fixture now supports explicit and implicit TLS. Its generated
certificate has a loopback IP subject alternative name; the upload worker
accepts it for this connection only when its SHA-256 matches the exact fixture
certificate. The cleanup client verifies that same certificate before
inspecting and deleting the exact unpublished stage. The loopback fixture
rejects the untrusted certificate without its pin; its tests passed on Windows
Python 3.13 and the VM's Python 3.9, and the harness
built with zero warnings on the host and guest. Native Copy and Move runs for
both TLS modes each interrupted after 786,432 staged bytes. The recovery row,
dialog and report named that stage; FileCat left it untouched until explicit
inspection and deletion, then closed the reviewed journal. The source hash
remained unchanged and the test server finished empty. Guest receipts are
`ftpes-crash-copy-2f1f43c9.json`, `ftpes-crash-move-2f1f43c9.json`,
`ftps-crash-copy-2f1f43c9.json` and `ftps-crash-move-2f1f43c9.json`.
The updated shared fixture also passed a native plain FTP Copy rerun.

**26 October 2026, Win11 implicit-TLS FTPS large listing (FCUX-010):** The
Git-built `2f1f43c9` app listed 2,001 files from a test-owned loopback
implicit-TLS server after an explicit once-only certificate decision. Native
UI Automation exposed 25 rows and 242 nodes initially, then 24 rows and 241
nodes after physical End focused `target.txt`; each snapshot took under one
second and the End navigation took 1.134 seconds. Source and server bytes
remained unchanged. The guest receipt is `ftps-implicit-large-2f1f43c9.json`.
The updated native runner also passed plain FTP and explicit-TLS 2,001-file
regression journeys, including plaintext consent and certificate review.

**25 October 2026, Mac FTPS interruption (FCUX-001):** On the signed
`7a5492fe` Mac bundle, implicit-TLS FTPS was dropped after 262,144 staged
bytes. FileCat kept the exact unpublished stage and reported its path in the
finished issue row; VoiceOver spoke the partial-stage warning and path. The
source and existing destination hashes remained unchanged. The passing
receipt is `mac-remote-warning-ftps-disconnect-speech-7a5492fe-r1.json` in
the Mac validation folder. Other provider failure kinds remain open.

**25 October 2026, Linux row speech (FCUX-010):** Avalonia.FreeDesktop.AtSpi
12.1.1 exposes a row's HelpText as both Description and HelpText, so Orca
previously spoke the same size, modified time, mark and position details
twice. With that native bridge attached, the row now exposes its safe full
description in Name and an empty HelpText. Windows, macOS and headless Linux
peers retain their concise filename and separate details. The changed name
raises a semantic property event when cached row state changes. The new App
regression checks complete details, mark changes and the single-source
presentation; the 25 focused accessibility cases passed.

The isolated Ubuntu VM built a clean `df449513` Git baseline and confirmed
that each tested file detail was spoken twice. After compiling the candidate
source in that same checkout, `linux-orca-speech.py` passed all 48 native
steps with Orca 50.2 and physical keyboard input. Each tested file detail
was spoken exactly once; the complete Rename, dialog, marked-selection and
Move/Cancel journey also passed, with exact source and target checks. Its
final receipt is `~/FileCatValidation/orca-current-candidate-r3.json`, app DLL
SHA-256 `2cfc3173910e6977415bc3771bcfbe81cba33cfcb1a48a9e9bde21ef2bcd5713`.
The earlier short runs used older Git revision `38bc114c` and then a cached
assembly; neither qualified as current-main acceptance. The complete Windows
Release App suite passed 2,475 cases, skipped 63 platform cases and failed
none with 16 workers before the final peer read optimization. The exact
optimized source then passed 25 focused Windows and 21 focused Ubuntu
file-list/accessibility cases. The final-head CI gate remains open.

**25 October 2026, Mac SFTP pointer and spoken review (FCUX-001):** A fresh,
clean Git clone of `7a5492fe` produced a signed Mac ARM64 validation app
(DLL SHA-256 `33542522e20e7935a896d07904027dd2ffcc6439e2f1a264e1dc7109cbb74c66`).
With Mac output muted, the current-main SFTP stale-focus probe passed at
Retina and at a supported true 1× 1920 × 1200 display mode. The physical
source-tab click activated panel 1 on the first attempt, and an injected AX
focus request for the previous panel's Filter 35 ms later could not take
ownership. A full 1× 640 × 480 review then opened both Copy paths on the
first source-tab click, displayed and automatically spoke the complete
replacement warning, and preserved exact source/target hashes after Cancel
and Ask/Skip. Receipts are `mac-remote-warning-pointer-race-7a5492fe-r1.json`,
`mac-remote-warning-pointer-race-7a5492fe-1x-r2.json` and
`mac-remote-warning-pointer-narrow-7a5492fe-1x-r1.json` in the Mac
validation folder. The display wrapper verified restoration to 1440 × 900
logical / 2880 × 1800 physical pixels after both 1× journeys. Other remote
provider failure, interruption and publication-race gates remain open.

**25 October 2026, restored Windows Hex acceptance (UXBUG-020):** The owner
confirmed the isolated VMware guest's desktop selection once again follows
the physical pointer after restoring `updated #38`. A powered-off policy
checkpoint and a later suspended-session checkpoint preserve the guest
without another Windows restart. The final pre-Hex snapshot is
`FileCat built Hex baseline 2026-10-25`; it contains a clean Git clone of
`f587a779`, private .NET SDK `10.0.101`, and a Release app whose DLL SHA-256 is
`3f6e7cc99cb44680da350180bfaed208e3755ed91ed0c9b6ae9239d059ff7ee6`.
The VM's Defender real-time, behavior, downloaded-file and on-access scans
stayed off across reboot; `NoAutoUpdate=1` also persisted and `wuauserv` was
stopped at the postboot check. Windows reset that service's startup type to
Manual, so the policy, rather than the service startup mode, is the durable
automatic-update control in this guest.

The Git-built Hex viewer passed the native 100% journey and three consecutive
150% journeys at measured 96/144 DPI. The revised 150% run reflowed from 8 to
20 bytes per row, dragged the thumb to byte 1,460 and back to zero, and kept
the four-byte explicit selection. The editor passed at both 100% and 150%; at
150% its real window widened from 952 to 1,432 physical pixels, rows grew
from 16 to 24 bytes, and the thumb moved to byte 1,440 and back. Every final
receipt records unchanged source SHA-256, an empty target and exact fixture
cleanup. The reports are `hex-scrollbar-policy-100-r1.json`,
`hex-scrollbar-policy-150-r1.json`, `hex-scrollbar-policy-150-r2.json`,
`hex-scrollbar-policy-150-revised-r3.json`, `hex-editor-policy-100-r1.json`
and `hex-editor-policy-150-r2.json` in the guest validation folder. The
earlier editor failure at 150% was a test-width error: Windows enforced its
620-DIP minimum width, so the runner's old 640-to-940-pixel request left the
actual window near the same width. The runner now measures the clamped narrow
window and requests a DPI-aware increase with enough desktop room. The scale
helper also closes the outer Settings frame via its supported WindowPattern;
its final receipt confirms restoration to 100% and a closed Settings window.
The scaled Ubuntu physical-pointer and combined interaction routes remain
open in `UX_UI_REMAINING.md`.

**25 October 2026, CI follow-up:** GitHub runs `40226638787` (`f587a779`) and
`40233350090` (`df449513`) completed every required lane successfully. The
earlier `a6b550a0` Ubuntu Core run `40223594897` observed six rather than five reads in
`MetadataDemandTests.Forgetting_the_files_a_check_read_asks_only_their_rows_again`;
the later `1f478800` macOS App run `40226086392` found `..` instead of
`sample.txt` after canceling inline Rename. Each exact test passed 16
parallel isolated reruns on its respective desktop. Their intermittent
cause remains under review; the final
post-change CI gate is still required.

**25 October 2026, Windows scaled Hex pointer investigation (UXBUG-020):**
The isolated VMware guest ran the Git-built `15a1a9c5` Hex viewer at 150%
(measured window DPI 144), then was restored to its original 100%. The native
runner now measures DPI for content-drag coordinates, uses `SendInput` for
button events, verifies the thumb under the pointer, records the scrollbar's
RangeValue and final cursor coordinates, and leaves the viewer foreground
between consecutive drags. Its 100% control run passed: 16 to 24 bytes per
row, byte 2,184 down and byte 0 back, exact four-byte explicit selection
preserved, source SHA-256 unchanged and target empty. At 150%,
`hex-scrollbar-viewer-150-value-r1.json` passed all those checks (8 to 16
bytes per row, byte 1,456 down and byte 0 back), but an immediate repeat
`hex-scrollbar-viewer-150-value-r2.json` left the scrollbar RangeValue and
viewport at zero after its first physical drag despite a UIA hit on the thumb.
Earlier 150% attempts sometimes moved down but not back. Each attempt
verified exact source bytes, an empty target and removal of its owned fixture.
This is inconsistent native input evidence; the scaled Windows thumb route
remains open pending a repeatable pointer delivery check and, if the app is at
fault, a product fix. The guest's display-scale restoration receipt selected
100% after the final run.

**25 October 2026, Ubuntu spoken inline Rename (FCUX-004):** The native Orca
50.2 journey now checks physical F2, the focused inline field's spoken name and
entry role, and physical Escape back to the same file row. The Git-built Ubuntu
app (DLL SHA-256
`010b58b4f3b1e1a491f636bf6ebb74a4bf92b43b29ee0efc1cbf28106e7468de`)
passed all 48 speech checks: Orca said `Rename selected item`, `entry`, and
`c.txt` on entry, then returned to the `c.txt` row on Escape. Physical F8
announced `Delete dialog` and its Delete button; Escape returned to `c.txt`
without changing any of the three source files or the empty target. Physical
F6 announced `Move dialog`, its Destination entry and the complete isolated
target path; Escape returned to `c.txt` with the same exact fixture bytes. The runner now
discovers the logged-in user's current Xwayland authority, since VMware guest
launches do not inherit it after a VM restart. The receipt is
`ubuntu-orca-move-cancel-r1.json` in the Ubuntu validation folder; the
earlier `ubuntu-orca-inline-rename-r2.json` and
`ubuntu-orca-delete-review-r1.json` have 44- and 46-step results. Other
modal routes remain to be checked for actual Orca speech. The duplicate row
details heard on Escape were subsequently resolved by FCUX-010 above.

**25 October 2026, Ubuntu inline Rename editor accessibility (FCUX-004):**
The strict physical-F2/AT-SPI journey reproduced the missing editor on the
Git-built `38bc114c` app: its TextBox was visible and focused but absent from
the assistive-technology tree. Avalonia 12.1.1's AT-SPI bridge collects only
selection-item descendants of a selection provider; FileCat's file list is
such a provider, so its nested TextBox was dropped. On Linux, the same inline
editor and its validation error now live in a sibling Canvas overlay at the
list row's existing coordinates. The list retains its selection provider and
bounded row peers. A headless test checks the sibling peer, editor focus and
bounds, inline validation, Escape return, and clicking elsewhere to cancel;
all 47 related FileList, geometry and dialog-focus cases passed. On a patched
Git checkout in the Ubuntu VM (app DLL SHA-256
`010b58b4f3b1e1a491f636bf6ebb74a4bf92b43b29ee0efc1cbf28106e7468de`),
the native journey found the named AT-SPI entry, then passed physical Escape
from Rename, Create folder and Delete with `c.txt` focused and exact source
bytes intact. A second journey edited that entry through AT-SPI at its
98 × 307 × 140 × 24 pixel screen bounds, pressed physical Enter, and verified
`renamed.txt`, its exact source bytes, focus return and an empty target. The
receipts are `ubuntu-rename-overlay-candidate-r1.json` and
`ubuntu-rename-overlay-commit-r1.json` in the Ubuntu validation folder.

**25 October 2026, Ubuntu scaled Hex input investigation (UXBUG-020):**
Mutter's DisplayConfig accepted a temporary 1920 × 1200 virtual monitor at
200% and reported that scale in its active configuration. The Git-built
`38bc114c` Hex journey passed its narrow/wide row reflow assertions before
the physical-thumb check, but the disposable uinput mouse did not move the
compositor cursor through the requested drag; the test failed with the Hex
viewport still at byte 0 and no selection. A separately tried Xwayland cursor
warp reached the thumb coordinate, but the physical drag still did not scroll,
so it was removed from the test runner. A 125% DisplayConfig setting on the
original 1280 × 800 monitor also left Xwayland at the prior 192 DPI and
2048 × 1280 root size, so it was not a valid 125% app test. Even a persisted
200% DisplayConfig setting returned to the VM's original 1280 × 800 at 100%
after a soft restart; Xwayland then reported 96 DPI again. All three failed
runner fixtures were removed only after verifying their exact generated source
bytes and empty targets. The VM is back at its original display scale. This
does not establish a Hex regression or close the scaled Ubuntu physical-input
gate; a stable scaled VM session and matching pointer coordinates are still
needed.

**25 October 2026, native Ubuntu Hex scrollbar selection (UXBUG-020):**
The 100% Ubuntu VM ran the Git-built `38bc114c` viewer/editor journey three times
with a short-lived physical uinput mouse. A feedback loop moved the Wayland
cursor onto each actual AT-SPI scrollbar frame before pressing, avoiding
XTest's ignored drag and pointer acceleration. A repeated run moved the
viewer viewport from byte 0 to 2,424 and back; the editor moved from 0 to
2,568 and back. Neither thumb drag created a byte selection. An explicit
content drag in each mode selected exactly bytes 0 through 3; another thumb
drag preserved all four bytes while scrolling to those same respective
offsets. The runner then reset the viewer before the independent Hex Find
journey, which passed its search, zoom, scroll-anchor and editor reflow
checks. The one-time native-input password is removed from the environment
before FileCat launches. The final run separately asserted exact source
bytes, an empty target and owned-fixture cleanup. Receipts are
`ubuntu-hex-thumb-physical-38bc114c-r5.json` and
`ubuntu-hex-thumb-physical-38bc114c-final.json` in the Ubuntu validation
folder. All three desktop platforms now pass the physical Hex
thumb/content distinction; scaled Ubuntu and combined interaction gates
remain open. Thirteen abandoned diagnostic fixture directories were removed
only after matching their generated source hashes, companion files and empty
targets and checking for active owners; the final guest inventory is empty
(`hex-clean-applied.json`, `hex-clean-final.json`).

**25 October 2026, native Mac Hex scrollbar selection (UXBUG-020):** On the
same isolated, Git-built and signed `38bc114c` app, physical `cliclick`
thumb drags and accessible Hex semantics passed in both viewer and editor.
The viewer moved its viewport from byte 0 to 1,824 and back; the editor
moved from 0 to 2,160 and back. Neither route moved the byte cursor or
created a selection. A deliberate drag across the first Hex row then
selected exactly bytes 0 through 3, and another thumb drag preserved those
four bytes in each route. The tests use the actual AX document and scrollbar
frames, confirm exact first-row bytes and resize reflow, and parse macOS's
nonbreaking-space thousands separator in semantic offsets. Source SHA-256
remained unchanged and both owned fixtures were removed. Native receipts are
`mac-viewer-hex-pointer-38bc114c-final.json` and
`mac-hex-editor-pointer-38bc114c-final.json` under the Mac validation folder.
The corresponding Windows native routes passed earlier; scaled and combined
Hex interactions remain open.

**25 October 2026, native Mac Hex reflow (UXBUG-020):** A fresh, isolated
Git checkout at `38bc114c` was published directly on the Mac as an ad-hoc
signed validation app, without a ZIP archive (DLL SHA-256
`7451b60b539c14ada1e1e66d7d642f2ce9657f4fe14c04f0e21dc5b350b7082f`).
The strengthened `mac-ax-viewer-mode-layout.py` asserted the actual first
Hex row against fixture bytes: a 720 × 500 logical-point viewer fitted 16
bytes and a 1,220 × 700 viewer fitted 32. Its Text and Info narrow/wide
journeys also passed. `mac-ax-hex-editor-resize.py` asserted the same source
mapping in a pointer-shrunk 720 × 500 editor (16 bytes) and a widened
1,013 × 602 editor (24 bytes). Both native reports passed, retained exact
source SHA-256, and removed only their own fixture. The Mac's unrelated
FaceTime system prompt was dismissed with its identified **Later** action
before the final captures. Reports are
`mac-viewer-hex-row-reflow-38bc114c-r1.json` and
`mac-hex-editor-row-reflow-38bc114c-r4.json` in the Mac validation folder.
Mac combined editor search, scroll and zoom interactions remain open.

**25 October 2026, native Windows Hex editor (UXBUG-020):** The same
Git-built `15a1a9c5` Windows VM app passed the editor route of
`win-ax-hex-scrollbar.ps1` twice with a native pointer dwell
(`hex-scrollbar-editor-r11.json`, `hex-scrollbar-editor-r12.json`). Narrowing to 640
pixels and widening to 940 grew the first Hex row from 16 to 24 bytes.
Native thumb drags moved the viewport from 0 to byte 2,160 and back without
creating a selection. A deliberate content drag then selected bytes 0
through 3; another thumb drag preserved that exact selection. The editor's
exclusive file lock was released before SHA-256 verification; the 8,192-byte
source and empty target were unchanged, and the owned fixture was removed.
A separate headless `HexEditorSemanticsTests` case sends real Shift+Right key
events through the editor and verifies the same exact four-byte selection.
The complete Windows Release App suite on the preceding code head passed
2,472 tests, skipped 63 platform-only cases and failed none.

**25 October 2026, native Windows Hex resize and scrollbar (UXBUG-020):**
`win-ax-hex-scrollbar.ps1` opened an isolated Git-built `15a1a9c5` viewer in
the Windows 11 VMware guest, narrowed it to 640 physical pixels and widened
it to 940. The first Hex row grew from 16 to 24 bytes. Native mouse drags of
the vertical thumb moved the viewport from byte 0 to byte 2,184 and back to
0 without changing the cursor or starting a selection. An explicit content
drag selected bytes 0 through 3; another thumb drag preserved that exact
selection. The 8,192-byte source SHA-256 remained unchanged, the target
remained empty, and the final runner removed its own fixture
(`hex-scrollbar-viewer-r15.json`). The ten earlier diagnostic fixtures were
removed only after their generated source hashes and empty targets were
verified; `hex-fixture-clean.json` reports zero remaining. This closes the
Windows viewer resize and thumb-drag acceptance for this finding; the editor
route passed separately above.

**25 October 2026, Hex scrollbar selection (UXBUG-020):** A routed pointer
move from a scrollbar thumb reached `HexView`'s byte-drag handler and could
extend a byte selection while the user was only scrolling. The view now owns a
selection pointer only after a left press inside its byte canvas, requires its
own pointer capture for subsequent selection moves, and releases only its own
capture. Pressing or releasing a scrollbar no longer shifts focus to the byte
canvas or releases the scrollbar's capture. Replacing the source also retires
an in-progress byte drag. A real pointer regression drags the vertical thumb
with no selection, preserves an existing exact selection through another
scrollbar drag, and then confirms that a content drag still selects the
intended bytes in both read-only viewer and editor modes. Both focused cases
passed; 77 related viewer, editor and fractional-scroll cases passed with no
failures or skips. Native Windows viewer acceptance passed separately above;
the other desktop and editor combinations remain open.

**25 October 2026, combined native Ubuntu Hex resize (UXBUG-020):** The
Git-built Ubuntu viewer at 100% scale passed an extended
`linux-atspi-hex-resize.py` journey (`ubuntu-hex-resize-combined-9081bd6e-r2.json`).
AT-SPI opened Hex-byte Find and selected source offsets 1 through 3; the exact
selection and cursor remained after narrowing to 620 × 480 and widening to
900 × 480, while the first row grew from 12 to 24 bytes. A native Increase
content size action reduced the fitted row from 24 to 20 bytes without losing
the selection. Physical PageDown input moved the viewport to source byte 384;
narrowing back to 620 × 480 retained that first visible byte. The same run
rechecked editor reflow from 16 to 24 bytes per row and preserved the owned
8,192-byte source byte-exactly. The first attempt's single-PageDown assertion
was corrected to account for the first press landing on the last visible row.
Scaled Ubuntu, Windows and pointer-alignment acceptance remain open.

**25 October 2026, dependency notice CI repair:** The earlier Windows
process-census fix introduced `System.Management` 10.0.0 and its transitive
`System.CodeDom` 10.0.0 dependency. The retained notice snapshot now indexes
both exact NuGet archive hashes and content hashes, their third-party notice
bytes, and the MIT license bytes from the package-declared `dotnet/dotnet`
revision. The release allowlist control passed all 63 cases (7 allowed, 56
refused); a fresh win-x64 self-contained publish passed the production notice
validator and copied all 53 verified snapshot files. The final five-lane CI
gate remains open until the pushed head is green.

**25 October 2026, Hex viewer resize (UXBUG-020):** `HexView` now fits groups
of four bytes to the available content width, from 4 to a bounded 128 bytes
per row. Resize and font-size changes rebuild row geometry while preserving
the first visible byte, byte selection and search cursor. Hex and decoded
text share the same grouped column calculation for drawing, hit testing,
keyboard movement, accessibility text and scroll bounds. A >4 GiB source
geometry test now checks every byte in the computed row; a new two-mode
viewer/editor test covers narrowing, widening, selected search ranges,
vertical scroll preservation and pointer hits. The focused App selection
passed 100 tests with no failures or skips. The complete Windows Release App
suite passed 2,470 tests, skipped 63 platform-specific tests and failed none.
CI exposed two older scroll tests that assumed 16 bytes per Hex row; they now
derive both PageUp distance and next-page wheel distance from the fitted row
width, and their focused 18-test selection passed. CI run `40201034258` on
`45b47f82` then passed the producer, Windows, Windows ARM64, macOS and Ubuntu
lanes. A signed, Git-built Mac arm64
bundle with DLL SHA-256
`8081d370a10ff1011842e0531363e285f7786c317a1e9a5bab4ee0c421cbc0c4`
passed the native Text/Hex/Info narrow/wide capture journey
(`mac-hex-resize-final-r7.json`). The Hex screenshots show consecutive row
offsets 0x00/0x10 at 720 × 500 and 0x00/0x20 at 1220 × 700, with the
decoded text column aligned and visible in both; the test-owned 9,040-byte
source stayed unchanged. The same signed build passed the native Mac Hex
editor's 720 × 500 Find journey with its source unchanged, then a separate
test-owned 8,192-byte editor resize journey
(`mac-hex-editor-resize-final-head.json` was followed by a passed
`mac-hex-editor-resize-retry.json` on the same Git-built binary). Cocoa can
return an intermediate AX frame before finishing a requested expansion; the
runner now waits for the native frame and retries the requested size before
evaluating the wider layout. It requires the editor in front before each
approved capture. The passed captures show rows at 0x00/0x10 in the narrow
720 × 500 frame and 0x00/0x20 in the wider 1154 × 681 frame, with aligned
decoded text, byte-exact source preservation and no unsaved changes. Other
desktop and combined interaction checks remain in `UX_UI_REMAINING.md`. The
logged-in Ubuntu VMware guest built the Git head `f29e9a05` with .NET 10.0.402
and 16 build workers, with zero warnings or errors. Its Git-built app
(SHA-256 `f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`)
passed `linux-atspi-hex-resize.py` at 100% scale
(`ubuntu-hex-resize-f29e9a05-final.json`). The native Hex viewer's exact
first row grew from 12 bytes at 620 × 480 to 24 at 900 × 480; the native Hex
editor grew from 16 bytes at 720 × 480 to 24 at 900 × 480. The test-owned
8,192-byte source remained unchanged, and the passing runner removed its
disposable fixture.

**25 October 2026, repeated Mac FTPS warning speech (FCUX-001):** The
Git-matched native runner passed an implicit-TLS FTPS Replace review and its
subsequent per-item decision with muted VoiceOver speaking the complete
warning at each step (`mac-ftps-warning-recheck-a6.json`). A second run
stopped before the per-item decision because its zero-frame tab fallback
pressed Tab while the source panel was already active. The fallback now checks
the active panel before switching. A normal repeat and a forced keyboard
fallback both passed both automatic speech checks and preserved the exact
source and target bytes (`mac-ftps-warning-active-panel.json` and
`mac-ftps-warning-keyboard-source.json`). The warnings still expose zero Mac
AX bounds; that separate bridge/layout investigation remains open.

**25 October 2026, dropped FTP/FTPS connection and issue speech (FCUX-001):**
The signed Git-built Mac arm64 app ran three fresh, owned loopback journeys
where a 1 MB Replace was rate limited until its exact `.fc-` stage held
262,144 source bytes. The runner stopped only that server during upload,
chose Skip at FileCat's native Review decision, opened Details and required
the retained-stage warning. FTP, explicit-TLS FTPS and implicit-TLS FTPS all
preserved the original source and destination byte-exactly, retained only a
partial stage whose bytes equal the source prefix, and left a two-item server
manifest. Receipts are `mac-ftp-disconnect-during-upload-20261025-r2.json`,
`mac-ftpes-disconnect-during-upload-20261025-r1.json`, and
`mac-ftps-disconnect-during-upload-20261025-r1.json` in the isolated Mac
validation folder. The operation-issue row's macOS accessibility name exposed
raw `JobIssue` record fields even though its visual text was friendly. The
row now binds a concise severity, reason and complete path label; a headless
Avalonia test checks that exact row name and a Core test covers present and
absent paths. The signed Git-built `94f16034` Mac app (DLL SHA-256
`ec8bde1e9d6abb5a33d4a6d0aac81c78be36271d6ba6a9ecd78e2875862822a8`)
ran the same native FTP interruption route. Its macOS accessibility row name
was the concise `Warning: A partial copy is left on the server ... Path:
/.fc-3e0f7c0a-1`, with no raw record fields; the new runner assertion requires
that complete stage path. Its source/target preservation and partial-stage
checks also passed (`mac-ftp-issue-accessibility-94f16034-r2.json`). Native
VoiceOver then spoke the concise warning and exact provider path after the
native AX row was selected: `Warning: A partial copy is left on the server
... Path: /.fc-b6442177-1 table row`. The `--disconnect-and-speak` journey
preserved both files and the exact partial stage
(`mac-ftp-issue-speech-94f16034-r1.json`).

**25 October 2026, CI and reverted Win11 VM:** On `c10bb076`, Ubuntu CI run
`40186163627` built successfully and passed 4,472 Core tests with 110 skips,
but the hostile Markdown `<` case crossed its five-second wall-clock limit by
0.184 seconds on the shared runner; the other five hostile cases passed.
The test keeps the same large pathological input and a finite ten-second
bound, allowing modest scheduler delay while detecting a severe parser
regression. On the owner's reverted isolated Win11 VM, automatic updates were
disabled by `NoAutoUpdate=1` and the stopped `wuauserv` has Disabled startup.
Defender reports real-time, behavior, downloaded-file and script scanning off,
with Tamper Protection off; its protected antivirus service still reports
running. An exact Git clone of `c10bb076` and pinned .NET SDK `10.0.101`
built the Windows app with zero warnings and errors using eight guest cores.
The guest then installed a Python.org signed Python 3.13.17 in an isolated
validation folder and restored `pyftpdlib`/`pyOpenSSL` in a private venv. The
native implicit-TLS FTPS review initially refused keyboard input because an
elevated Salamander window held foreground focus. After clearing that idle
overlap, the same Git-built FileCat executable (SHA-256
`6309E9601B8C744A62CDC70CFC451E0B6C34DF5BB037F8B7D14CDDB9273A959F`)
passed a fresh `win-ax-ftps-replace-c10bb076-r3.json` journey at 96 DPI:
one-connection certificate consent, listing, complete replacement warning,
Keep both clearing the warning, narrow per-item warning, Skip, and unchanged
source/remote target hashes. The same native runner passed fresh
`win-ax-ftps-replace-c10bb076-150-r2.json` and
`win-ax-ftps-replace-c10bb076-200-r1.json` journeys at measured 144 and
192 DPI, with both warnings and actions inside the window and exact source
and target preservation. Windows Display Settings then restored the guest to
100%. NVDA speech on this route remains open.

**25 October 2026, native Mac FTP/FTPS changed-destination cleanup
(FCUX-001):** The signed Git-built arm64 app (DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`)
ran a 1 MB rate-limited Replace from the Mac UI against fresh, owned
loopback FTP, explicit-TLS FTPS and implicit-TLS FTPS servers. Only after an
exact `.fc-` stage held 262,144 incoming bytes did another client change
the destination. All three protocols showed a changed-destination refusal,
removed the stage, preserved the source and newer destination byte-exactly,
and left only `target.txt` on the server
(`mac-remote-change-during-upload-ftp-20261024-r1.json`,
`mac-remote-change-during-upload-ftpes-20261024-r3.json`,
`mac-remote-change-during-upload-ftps-20261024-r1.json`). A separate
implicit-TLS run changed the destination's bytes while restoring its exact
size and modification timestamp. FileCat emitted the more specific
changed-content refusal and made the same cleanup and preservation checks
(`mac-remote-change-content-ftps-20261025-r1.json`). The acceptance runner
now isolates these failure paths from the existing spoken-warning journey
and records the visible warning, exact stage prefix, refusal and final
manifest. One preliminary explicit-TLS run stopped because VoiceOver kept
reading the Destination field despite the visible warning; a second had a
zero-size AX frame for the visible source tab after certificate approval.
The runner gained a native Tab/End fallback for that intermittent AX frame;
the subsequent explicit-TLS and implicit-TLS runs passed. A forced fallback
implicit-TLS run also reached the exact source and destination, refused the
changed target, removed its stage and preserved both files
(`mac-remote-keyboard-fallback-ftps-20261025-r1.json`). The dedicated
warning-speech journeys passed earlier, but announcement reliability after
repeated certificate prompts remains to be rechecked. CI run
`40122291509` passed producer, Windows, Ubuntu, macOS and Windows ARM64 on
the preceding `4cf147cd` main head.

**24 October 2026, Ubuntu decision focus and inline Rename accessibility
(FCUX-004):** On uninstrumented Git-built `6825ecb2` (app DLL SHA-256
`a025ecf6e8863580bf78551ace41e68d2c7b961251d02d51fc079d760e42262d`),
the physical-key/AT-SPI `--dialog-only` journey passed Create folder and
Delete Escape: focus settled on the same `c.txt` row and the source bytes and
empty target stayed exact (`ubuntu-dialog-focus-6825ecb2-r10.json`). The
strict F2 Rename route remains open. Disposable diagnostic builds proved F2
and Shift+F6 reach FileCat with the list focused; `RenameAsync` sees `c.txt`,
and `BeginRename` returns a live, visible and focused TextBox. AT-SPI still
omits that editor from the app's tree. A headless peer-child test and explicit
child invalidation did not change the native result, so that unverified peer
change was discarded. The tracked runner retains the strict Rename route to
check a later bridge or presentation fix. The complete Windows App suite on
the committed focus code passed 2,468/63 skipped/0 failed; final-head CI is
still pending.

**24 October 2026, Mac inline Rename focus return (FCUX-004):** A native
VoiceOver/AX focus journey on the earlier signed build reproduced an
intermittent wrong-row return after Escape from inline Rename: focus moved
from `c.txt` to `b.txt` or the parent row. `FileListControl` now retains the
originating entry store and index and restores that exact visible item when
the editor closes; a changed or hidden listing is not revived. A headless
regression moves model focus during editing, cancels and checks the original
item; the focused FileList/App selection passed 23/23. The signed Git-built
`a3354ef1` Mac app (DLL SHA-256
`86495712533301d8fb3b6cb11cb330df0752d70ae9fede26ae3fa4d992be0c87`)
passed four fresh, isolated Create/Delete/Rename Escape journeys with settled
AX focus on `c.txt` and unchanged source/target bytes
(`mac-focus-decision-returns-a3354ef1-r2.json` through `r5.json`). The native
runner offers a short decision-return route so unrelated later focus journeys
do not obscure this regression. Other modal return paths remain open.

**24 October 2026, Windows CI provenance timeout:** The Windows lane of
CI run `40117503403` timed out while recording the first `dotnet --version`
after `setup-dotnet`; it reached no tests. The build-inventory script now
allows a bounded 120 seconds for .NET CLI commands while preserving its
30-second limit for Git commands. A local unsandboxed provenance invocation
completed with the exact pinned SDK `10.0.101` and a valid receipt. The full
Windows Release App suite also passed 2,468 cases, with 63 platform skips
and no failures; the Platform.Windows suite passed 429 cases, with 46 skips.
The updated script still needs the final-head hosted CI gate.

**24 October 2026, native Ubuntu implicit-FTPS large listing (FCUX-010):**
The Ubuntu VMware guest passed a fresh 2,000-file implicit-TLS FTPS journey
on Git-built `c3a8138f` (app DLL SHA-256
`60905f3ff6c16443f825a7586ff86ad08f1981697a61384873e07802c3fd621c`).
After one-time certificate approval, AT-SPI exposed 21 rows and 477 nodes
on load (1.135 seconds). Physical End focused `native-remote-1999.txt` in
2.264 seconds; the final tree exposed 20 rows and 476 nodes (1.317 seconds).
The two local source files and 2,000 remote files retained their bytes, and
the runner stopped its owned app and loopback server and removed the fixture
(`ubuntu-ftps-implicit-large-list-20261024-r3.json`). Two earlier runs
exposed a VM-restart test-harness issue: an SSH process lacked the logged-in
desktop's Xwayland authorization. The runner now selects exactly one
same-user Xwayland display and a bounded, regular auth file under that
user's runtime directory before launching FileCat; a failed startup also
reports a bounded app-log tail. The updated runner passed on the restarted
guest. The later Windows implicit-FTPS native listing is recorded above.

**24 October 2026, elevated Windows recovery admission (UXBUG-019):** The
owner's disposable `G:` scan was refused despite no second FileCat window.
An elevated Windows 11 VMware process census reproduced the cause without
reading the source: of 75 processes, there were no FileCat or `dotnet` peers,
but `Idle`, `System`, `Registry` and `Memory Compression` had no executable
image. The production guard returned unknown because `Idle.HasExited` raised
access denied, and the other three image queries returned Win32 error 31.
The guard now skips PID 0 before its liveness query and exempts the other
kernel bookkeeping entries only with their expected PID or session, name and
no-image error. Other inaccessible executables still refuse recovery.
Seven focused Windows identity tests and three focused App safety tests pass;
the full Windows Platform suite passed 429/46 skipped/0 failed. The Git-built
production `FileCat.InstanceSmoke` returned `running:false` in the same
elevated VM after the fix, and `running:true` while a separate, isolated
`FileCat.exe`-named process was alive. The test process, guest fixture and
temporary VMware share were removed. The VM has no `G:` drive, so the actual
updated UI scan of the owner's disposable volume remains a native acceptance
check; no recovery-source bytes were written during this diagnosis.

**25 October 2026, host G: recovery census recurrence (UXBUG-019):** The
owner permitted local diagnosis on the disposable `G:` (`FCTEST`). With no
FileCat process running, the Git-built production census initially returned
`running:true`: six unrelated `dotnet.exe` MSBuild workers were treated as
FileCat peers. After distinguishing managed hosts by their Windows process
command line, an elevated diagnostic census still returned unknown. It named
two session-zero, no-image kernel entries (`Secure System` and
`vmmemCmZygote`) and mainstream Slack, Signal, Claude, Dropbox, Codex and
Node executables larger than the former 64 MiB image-inspection bound. The
Windows guard now identifies actual `dotnet FileCat.dll` commands, exempts only
the two additional kernel entries with their characteristic no-image error,
and caches bounded image scans across processes sharing an executable. An
unreadable command, inaccessible image, or executable beyond the new 512 MiB
bound still refuses a scan. A second elevated production census on this host,
with those desktop apps and build workers still present, returned
`running:false` and no unknowns. The exact production `CheckDiskSafety` path
also admitted the real `G:` volume (`Refusal:null`) with its state files on
`E:`; the check did not open or write the recovery source. Whole-census
retries now absorb a process that exits between enumeration and inspection,
while persistent unknowns still refuse recovery. The focused App recovery
tests and Windows identity tests pass. At that point, the updated native UI
scan and off-source recovery on `G:` remained to be checked.

**25 October 2026, elevated Windows VM recovery UI (UXBUG-019):** A fresh
Git-built `15a1a9c5` FileCat ran elevated in the isolated Win11 VM against a
512 MiB NTFS VHD mounted as `G:` (`FCTEST`). The VHD had a separately verified
deleted 81,920-byte fixture; its original SHA-256 was
`28776239DA5D6C1F7CF44C12106F7D6E67C8F5674E94B8966067592EBD4038E9`.
The exact production disk-safety probe admitted the volume with no refusal.
Native UI Automation then verified that `G: FCTEST` was selected, the review
named `Source: drive G: (FCTEST)`, and the visible Scan button opened a recovery
tab without the reported other-process refusal. The scan showed zero deleted
entries on this newly formatted VHD, however, so this fixture did **not**
establish recoverability or an off-source copy. The source path remained absent
and the off-source target on `C:` stayed empty. The task-owned FileCat process
and scheduled tasks were stopped, and the VHD was detached and removed after
checking its exact disk identity. The owner's existing `G:` has still not had
this updated native UI journey; an off-source recovery with retained content
traces remains open.

**26 October 2026, deleted NTFS valid-data and native VHD follow-up
(UXBUG-019):** A fresh 512 MiB NTFS VHD on the isolated Windows VM retained one
deleted 81,920-byte MFT record long enough for a direct scan. Windows had
cleared the record's valid-data length to zero while leaving its runlist and
file size. Previously FileCat classified that physical run as zeros and the
file as fully recoverable. It now reads the former clusters as physical bytes,
marks the result **Uncertain**, and gives the specific valid-data warning to
the recovered content. A synthetic NTFS regression confirms the original
10,000-byte payload remains readable when a deleted record's valid-data length
is cleared; sparse runs and live files retain their existing treatment.
The complete Core Release suite passed 4,443/151 skipped/0 failed and the
complete App Release suite passed 2,493/63 skipped/0 failed with up to 16
workers. The opt-in Windows VHD test verified the selected volume GUID as well
as `G:`, the exact backing disk, and an off-source `C:` copy. Both raw clusters
and the copied file hashed to
`FA569E2360C540E6280E34A4627516770F1A5F34D81D35689334A99CC1013357`
(zeros), rather than the pre-delete reference
`C0CBD0D83B394C5A2E6E81A8080EEE63516735ABC7BBF61AB489AD69E0708D1C`.
The test requires the Uncertain evidence in this case; it does not claim the
original bytes were restored. After a delay, the mounted VHD's deleted record
itself disappeared from both direct and native scans. Recreating that one
disposable file and immediately refreshing the elevated Git-built FileCat
listed `recover-me.bin` in the native recovery panel (one file), with the
source still absent and the target unchanged. The native State column showed
**Uncertain** and the Evidence column began with the new NTFS warning. The
owned FileCat process was closed, and a disk-number/identity-checked cleanup
detached and removed only that VHD. The later flushed fixed-VHD fixture below
established exact original-byte recovery.

<a id="evidence-fixed-vhd-recovery-20261026"></a>
**26 October 2026, retained deleted NTFS bytes (UXBUG-019):** The Windows VM
first showed that both a dynamic and a fixed NTFS VHD returned zeros after the
test script wrote and immediately deleted an 81,920-byte file. A separate
live allocation probe mapped its 20 clusters to LCN `0x68e`; a direct raw
volume read at byte `6,873,088` matched its source SHA-256. Deleting that
probe after its data had reached disk retained the exact raw SHA-256 before
and after a volume flush and after NTFS TRIM returned to its original value.
This isolated the fixture fault: its previous file write was not flushed to
the physical volume before deletion. The opt-in fixture now uses a fixed VHD,
temporarily suppresses NTFS TRIM, explicitly flushes the file and volume,
deletes only its owned file, and verifies restoration of the original TRIM
policy before reporting success. A new 512 MiB fixed `G:`/`FCTEST` VHD with
that fixture exposed the deleted `recover-me.bin` record. Direct and selected
volume-GUID scans read raw clusters and copied the file to the VM's `C:`
validation folder; all three SHA-256 values matched the original
`C0CBD0D83B394C5A2E6E81A8080EEE63516735ABC7BBF61AB489AD69E0708D1C`.
The deleted source remained absent, and the original NTFS delete-notify value
`0` was restored. This flushed record still supplied valid-data evidence, so
FileCat classified it **Recoverable** without the lost-valid-data warning;
the earlier zero-valid-data case separately exercises **Uncertain**. The
focused Windows Platform build passed, and its full suite passed 442/47
skipped/0 failed with 16 workers. At exact Git head `663b6fdd`, the new
`FILECAT_RECOVERY_VM_EXPECT_RETAINED=1` mode passed 1/1 against the mounted
VHD and required the off-source result's SHA-256 to equal the original. The
native elevated FileCat on that same head selected `G: (FCTEST)`, scanned its
deleted items, showed `recover-me.bin` as **Recoverable**, and opened Copy
with the separate `C:` validation folder as destination. The first native
scan found no row after the deleted MFT record aged; recreating only that
disposable file with flushed bytes and pressing Reread immediately exposed
it. Approving the reviewed Copy produced the exact original SHA-256
`C0CBD0D83B394C5A2E6E81A8080EEE63516735ABC7BBF61AB489AD69E0708D1C`
in `native-target`, while the `G:` source stayed absent. The native operation
bar reported Completed. The exact FileCat PID was stopped, then the mounted
VHD was detached and its fixture removed after disk-number and unique-ID
checks; the cleanup receipt passed. At this checkpoint, the owner's
disposable `G:` route on the workstation was still listed for reconfirmation.
The acceptance ledger now classifies that host-specific repeat as optional
after this equivalent VM closure.

**26 October 2026, current-head Mac native dialogs and speech (FCUX-004,
007, 010):** The Mac Git checkout fast-forwarded to `be7b2a30` and published a
directly signed validation app from that revision, without an archive. Its
`FileCat.dll` SHA-256 was
`FE87CD90DA225880974FF6164A2E1CE07270BE47467C2E70C7E502360B96581A`.
The native Copy, Move and Delete review guard passed at both the normal window
and 760 × 600 Retina window: each review exposed one Cancel control and the
correct source/destination summary, blocked six background operation commands,
and canceled without changing the source or target. Approved-helper captures
were inspected for all three narrow reviews and normal Copy; full paths,
buttons and disclosure controls remained visible. A first cold-start attempt
timed out waiting for Copy, but a direct accessibility inspection found the
review and a fresh complete run passed; repeat the cold-start route if the
timing recurs. With Mac audio muted, VoiceOver spoke “Search canceled.” for
both Hex and Text Stop on separate 2 GiB sparse files; each test preserved
source, target and clipboard. A native F5 Copy review was also announced,
exposed the Destination field and Close dialog button, and Escape returned to
the unchanged `b.txt` row, which VoiceOver spoke. The extended native runner
also passed F6 Move and F8 Delete: VoiceOver spoke each title, AX exposed one
enabled decision and the expected Destination-field presence, and Escape
restored spoken `b.txt` with source bytes and target unchanged. Other item
actions and provider types remain open.

The `be7b2a30` macOS 26 CI Remote suite reported 2,595 tests passed with no
assertion failures, then its testhost exited 134. Its uploaded blame sequence
identifies the unfinished case as FTPES Skip after a dropped connection; the
four-minute hang detector terminated the testhost. A full Mac 27 rerun from
the same Git revision with the pinned 10.0.101 SDK passed 2,604/30 skipped/0
failed after adding the SDK to the test subprocess PATH. The first local
attempt's two failures were fixture-launch failures because `dotnet` was
absent from that SSH shell's PATH. A second focused Mac 27 run passed all six
FTP/FTPES/FTPS cancellation and skip cases in 82 seconds, including the
previously unfinished FTPES Skip case. The `9a500635` macOS 26 CI Remote step
also passed; the complete lane and the other platform lanes are still running.

**26 October 2026, Ubuntu 150% viewer acceptance (FCUX-007, 008):** The
isolated Ubuntu guest received a Git bundle containing the exact pushed
`9a500635` commit, made a clean worktree and published `FileCat` directly from
it. App DLL SHA-256 was
`112A09F36114D305F342AFEFBCB136EFFB5C27FE26FDE6EA096D59F6921197B`.
The first 150% attempt used GNOME's persistent monitor change, which reverted
to 100% at its confirmation timeout; those reports were not accepted as 150%
evidence. A temporary monitor change held the measured 1920 × 1200, 1.5×
desktop beyond that timeout. With the VM's user integration process paused,
four native AT-SPI journeys then passed at a measured 1.5×: the 720 × 500
logical Text viewer measured 1080 × 750 physical pixels, retained the exact
Find count, Previous/Next outcomes and F3/Shift+F3 behavior; its narrow
checksum review kept scope, one-byte and whole-file hashes, complete results
and explicit Copy controls within that frame; Hex and Text each displayed
Stop, canceled an in-progress search over an owned 2 GiB sparse file, retired
Stop and preserved the source, target and clipboard. The reports are
`scale150-9a500635-{find,checksum,stop-hex,stop-text}.json` in the guest and
`.local-ubuntu-scale150-final-*-9a.json` in the host checkout. The suite
exited zero and an independent display check confirmed restoration to
1280 × 800 at 100%. Remote-provider cancellation and other spoken or scaled
routes remain open.

**25 October 2026, Mac FTPS replacement speech retry (FCUX-001):** A second
native implicit-TLS certificate and replacement review showed the full amber
warning, but VoiceOver stayed on Destination and did not announce it. The
first implicit-TLS run and two explicit-TLS runs had spoken it. Because this
warning concerns possible loss of a newer remote file, both the transfer
form and per-item conflict review now use assertive live announcements. The
nine focused transfer and conflict warning tests pass; repeated native
VoiceOver acceptance of the new build remains open.

**24 October 2026, native Mac implicit-FTPS large listing (FCUX-010):** The
same signed Git-built arm64 candidate (app DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`)
passed a fresh, owned 2,001-file implicit-TLS FTPS listing at Retina 2× and
at the supported 1920 × 1200 1× display mode. Native AX exposed at most
27–28 rows and visited 499–500 nodes; initial queries took 0.231/0.203
seconds. Physical End reached the tail in 0.295/0.297 seconds, then physical
Down reached `target.txt` in 0.781/0.801 seconds. The original source and
remote bytes remained unchanged; the runner removed its server, test app
and fixture. The 1× wrapper verified restoration of the original 1440 × 900
logical / 2880 × 1800 physical Retina mode
(`mac-ftps-implicit-large-list-20261024-r4.json`,
`mac-ftps-implicit-large-list-1x-20261024-r5.json`). The AX probe now
includes the final visible row within its bounded child inspection.

**24 October 2026, Mac implicit-TLS replacement review (FCUX-001):** The
native Mac runner now covers `ftps://` with its own loopback implicit-TLS
server and fresh, connection-only certificate decision. The signed Git-built
arm64 candidate (app DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`)
passed full Copy and per-item conflict journeys at Retina 2×, and at a
640 × 480 point window in both Retina 2× and 1× modes. VoiceOver spoke the
complete warning about a change after the final check and the Keep both
alternative in both reviews. Cancel and Skip retained exact source and target
SHA-256 hashes and the single target file. The 1× captures show the complete
orange warning, visible close/Cancel/Skip actions and a usable scroll region
(`mac-remote-warning-ftps-implicit-narrow-1x-20261024-r2.json`,
`native-screen-586C124F-D639-4AA0-A816-E369FE4B00EB.png`,
`native-screen-D67943BF-F0FD-4460-9FC9-ABEFE7454609.png`). The test runner
dismisses only the Mac lab's overlapping Apple Account “Later” notice before
capture; an earlier obscured capture was discarded. The 1× wrapper restored
the original Retina display mode. Windows implicit-TLS native review,
failure cleanup and the server-side final-check race remain open.

**24 October 2026, Mac Copy return speech (FCUX-004):** The current signed
Mac arm64 candidate (app DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`)
passed the native `--copy-action` journey at Retina 2× and 1×. VoiceOver spoke
the Copy review title and, after Escape, the restored `b.txt` row with its
size, mark state and position. AX focus returned to the same row; source and
target bytes stayed unchanged (`mac-copy-return-filterfix-r3.json` and
`mac-copy-return-filterfix-1x-r4.json`). The 1× wrapper restored the original
Retina mode. The runner now compiles its dialog probe against the selected
Xcode SDK, matching its focus observer. Other modal focus routes remain open.

**24 October 2026, macOS CI pointer-focus timing:** CI run `40107929968`
passed producer, Ubuntu, Windows and Windows ARM64 lanes but had one macOS App
failure: `PointerFocusOwnershipTests` slept 60 ms before simulating an old AX
focus request, letting its 250 ms ownership guard expire under a loaded runner.
The test now dispatches the pointer press and immediately issues the delayed
AX callback without relying on scheduler wall time. The behavior still checks
that the clicked panel owns focus until a new physical press or key. The
focused test passed 1/1 on Windows and four consecutive 1/1 runs on Mac arm64.
The current main-head CI run must confirm the full lane.

**24 October 2026, Ubuntu native FTP/FTPS replacement speech (FCUX-001):**
The Ubuntu VMware guest used the Git-built `c3a8138f` app (DLL SHA-256
`60905f3ff6c16443f825a7586ff86ad08f1981697a61384873e07802c3fd621c`)
and an owned loopback server for separate plain FTP, explicit-TLS FTPS and
implicit-TLS FTPS runs. The runner now starts Orca on the actual Wayland
session and tolerates a child that disappears during AT-SPI tree traversal.
At measured 200% scale in a 720 × 540 logical window, all three protocols
showed the complete Replace warning and in-bounds Cancel/Skip choices in both
the Copy review and per-item conflict. Orca spoke the complete final-check
risk and Keep both choice in both places. Plain FTP used explicit plaintext
consent; each TLS mode accepted only the test certificate for its connection.
Cancel and Skip preserved both exact source/target hashes and the server's
single `target.txt` (`linux-ftp-warning-20261024-r1.json`,
`linux-ftpes-warning-20261024-r1.json`,
`linux-ftps-implicit-warning-20261024-r3.json` in the guest validation
folder). Windows and Mac native provider routes, other failure cleanup and
the intrinsic final-check race remain open.

**24 October 2026, implicit-TLS native review fixture (FCUX-001):** The
Windows replacement-review runner now accepts `ftps` as well as FTP,
explicit-TLS FTPS and SFTP. Its owned pyftpdlib fixture negotiates TLS before
the greeting, exposes the same unverified-certificate decision, and retains
the exact source/target hash and narrow warning checks. The independent
loopback fixture smoke test passed for all three FTP/FTPS modes; Python and
PowerShell syntax checks passed. The updated runner and helper were copied to
the Win11 guest, but its console was at the lock screen and VMware refused an
interactive run. Native implicit-TLS review remains open until the guest is
unlocked and the test runs against a Git-built app.

**24 October 2026, refused SFTP staging cleanup (FCUX-001):** The controlled
SFTP job tests now cover both Cancel and Skip after a dropped upload when the
server refuses to remove the unpublished partial stage. Both leave the source
and stage intact, publish no target, and report the exact stage path. A new
changed-source Move retry case verifies that only the new source bytes receive
the public name; a refused deletion retains the old partial stage and reports
it as an issue, so the job finishes `CompletedWithIssues` rather than silently
claiming a clean result. All 28 focused SFTP job cases passed on Windows with
up to 16 build workers. The complete Remote Release suite passed 2,422,
skipped 164 lab/platform cases and failed none in 3 minutes 53 seconds.
Native provider failure/cleanup and server-side final-check acceptance remain
open.

**24 October 2026, Mac 1× inline filter and reliable return speech
(FCUX-028):** The 1× display wrapper exercised the signed Git-built Mac arm64
candidate (app DLL SHA-256
`671e2f99319f55f4b4f4403c239889b7ef48e1030c1b66f3ce477445aa4b24dd`)
and restored the original Retina mode after each run. At 760 × 600 native
pixels, the whole orange inline validation error remained visible and the
source/target lists usable. The independent native screenshot and VoiceOver
error receipt is `mac-filter-return-1x-visual-r16.json` with its referenced
`native-screen-C4FD8B81-8606-4B13-A8B0-7033955DD490.png`. A separate
screenshot-free journey spoke the Filter field, exact invalid-mask error and
restored `b.txt` row, with the fixture bytes unchanged
(`mac-filter-return-1x-r15.json`). VoiceOver sometimes left a restored AX row
silent on the earlier build even when keyboard focus was correct; Filter Escape
now repeats the current row through the existing named, polite focus-return
live region after focus settles, provided that the same tab and list still own
focus. The focused filter tests passed 4/4 on Windows and 4/4 on Mac arm64.
The runner retains post-Escape AX focus on failure. VoiceOver's welcome prompt
was dismissed during validation; Mac output remained muted. The filter's
100%/150%/200% desktop layout and spoken routes recorded here are complete;
other FCUX-028 input/provider acceptance remains in the audit backlog.

**24 October 2026, Windows scaled inline filter (FCUX-028):** On the Win11
VMware guest, signed portable NVDA 2026.2 repeated its 37-step native journey
at 150% and 200% display scale against Git-built `b6aa9383` (app DLL SHA-256
`DEC0208B9B136581114EACB919752DA54F8A4A6A2C7AEDD198BC3F747A4A34D2`).
The runner now measures both DPI-virtualized Win32 size and independent DWM
physical bounds, then pauses for a VM screenshot at the invalid inline Filter
error. At 150%, the 720 × 500 logical window measured 1,062 × 741 physical
frame pixels; at 200%, it measured 1,418 × 989. Both screenshots show the
complete orange error below the field and a usable two-panel list. Each run
spoke the Filter field, exact validation error and restored `c.txt` row, and
preserved all fixture bytes (`win-nvda-filter-150-b6aa9383-r3.json/.png`,
`win-nvda-filter-200-b6aa9383-r1.json/.png` in the guest validation folder).
The VM's original 100% scale was restored and confirmed. The subsequent Mac
1× gate passed above.

**24 October 2026, Ubuntu scaled inline filter (FCUX-028):** A fresh Ubuntu
VMware checkout of Git head `c3a8138f` published with 16 workers (app DLL
SHA-256 `60905f3ff6c16443f825a7586ff86ad08f1981697a61384873e07802c3fd621c`).
At the guest's measured 200% display scale, the native runner resized the
FileCat window to exactly 1440 × 1000 physical pixels (720 × 500 app units).
The retained VMware screenshot shows the full orange validation message below
the inline Filter field and a usable file list in the narrow two-panel window.
The 42-step physical-key Orca journey passed again: it spoke the focused
Filter entry, the precise invalid-mask error and the restored `c.txt` row;
fixture inventory and bytes were unchanged
(`ubuntu-filter-scaled-c3a8138f-r2.json` and `.png` in the guest validation
folder). Windows scaled and Mac 1× acceptance subsequently passed above. Orca's
twice-spoken row details remain a separate FCUX-010 issue.

**24 October 2026, Windows spoken inline filter (FCUX-028):** The Win11
VMware guest cloned exact Git head `b6aa9383`, published with 16 workers and
ran the signed portable NVDA 2026.2 against the Git-built app (DLL SHA-256
`DEC0208B9B136581114EACB919752DA54F8A4A6A2C7AEDD198BC3F747A4A34D2`).
The extended 37-step native speech journey passed: Ctrl+Shift+F said “Filter,
edit” with the applied mask, Enter on `/` said “Unclosed regular expression in
mask,” and Escape said “Filter ready” before restoring the focused `c.txt`
row. The runner checked the test-owned files and destination remained exact
(`win-nvda-filter-b6aa9383-r1.json`). Ubuntu Orca and Mac VoiceOver had
already passed the same three consequential routes. Ubuntu's scaled layout
subsequently passed above; Windows scaled and Mac 1× acceptance passed above too.

**24 October 2026, Mac inline filter validation and crash prevention
(FCUX-028):** The native VoiceOver filter journey on the signed `2bd71147`
build reproduced a process crash after entering an invalid mask. The macOS
crash report identified Avalonia.Native's live-region announcement with a nil
name as the failure. The conditional visible error now retains its field-adjacent
text and accessible name, while a persistent, nonempty assertive live region
announces the error and its clearance. Focused Windows filter tests pass 4/4;
the same tests pass 5/5 on Mac arm64. The signed self-contained Mac candidate
(DLL SHA-256 `24fb028023f55b423b10dceb0f876f95429ed6185a609e272a5afd9305787c7b`)
passed the muted native VoiceOver journey: Command+Shift+F spoke the Filter
field, Enter on `/` spoke “Unclosed regular expression in mask,” Escape
restored and spoke the `b.txt` row, fixture bytes remained exact, and the app
did not crash (`mac-voiceover-filter-livefix-20261024-r1.json`). Windows
screen-reader speech and scaled layouts on all three desktops remained open at
that point.
The native runner now has a separate narrow visual mode, so opening the
approved screenshot helper cannot steal VoiceOver's Escape announcement.
The signed Mac build then passed a 760 × 600 Retina 2× main-window visual
check with the entire orange inline error below the Filter field, no clipped
error or panel row, exact fixture bytes, and the specific error spoken
(`mac-voiceover-filter-livefix-20261024-r7.json`, captured with
`FileCatCapture`). A fresh, screenshot-free speech run again passed Filter,
error and restored `b.txt` utterances (`r8.json`). Windows spoken filter
acceptance was subsequently completed above. Ubuntu's 200% layout also passed
above; Windows scaled and Mac 1× acceptance passed above too.

**24 October 2026, direct keyboard access to the panel filter (FCUX-028):**
`Ctrl+Shift+F` now focuses and selects the active panel's inline Filter field;
the command is available in command search and keeps `Ctrl+S`'s Quick filter
dialog. The field hint shows the shortcut. A 640 × 420 headless window test
uses the actual key chord on both panels, checks that only the active field
takes focus and its applied mask is selected, then checks invalid-draft
retention and Escape restoration. The new case and the four existing
filter-draft validation cases passed 5/5 with 16 build workers. Native
Ubuntu Orca 50.2 then passed 42/42 physical-key speech checks on the Git-built
`ebb0c9c6` app (DLL SHA-256
`b9acf6fba7a9cdf409ecf2934588b52d4d797cf2c6a8d7e688f113615a0eb21e`):
the shortcut spoke “Filter, entry,” Enter on an invalid inline mask spoke its
specific error, and Escape restored the focused `c.txt` row. The test kept
fixture bytes exact (`ubuntu-filter-keyboard-ebb0c9c6-r5.json`). The native
runner now asserts this route. Windows screen-reader speech subsequently
passed on `b6aa9383` as recorded above; remaining filter layout gates are
listed there.

**24 October 2026, initial Mac rename mapping speech and AX diagnosis
(FCUX-003/010):** Native VoiceOver intermittently left the first complete
rename mapping silent or let the aggregate count replace it, although
Previous/Next spoke both paths. Live regions now defer name changes prepared
before window attachment, the bulk-rename summary publishes before the
selected mapping, and the first attached mapping is repeated once after the
dialog's initial announcements settle. A new portable test proves deferred
text speaks once on attachment; another proves the count precedes the full
mapping. Focused Windows tests passed 29/29 for dialog/live-region behavior
and 26/26 for rename/review/live-region behavior; the Mac arm64 attachment
and ordering tests passed 15/15 and 1/1. A signed Mac app (DLL SHA-256
`6c891618777050f7c2bde3f0629127c612948df87812ed85aa3263a1071bdd72`)
passed four consecutive 760 × 600 muted VoiceOver rename runs: the first,
Next and Previous phrases contained exact complete source and target paths;
the external editor wrote the expected names and all source bytes stayed
exact (`mac-rename-initial-mapping-20261024-r1` through `r4.json`). The
native runner now waits a bounded five seconds for each exact path phrase and
retains its in-dialog AX snapshot.

The retained Mac AX snapshots confirm a separate unresolved geometry issue:
the visible `Bulk rename content` scroll area has a real frame, but its first
content child and visible Name mask, hidden-mark checkbox and preview list
report `[0, 900, 0, 0]`; the pinned mapping controls have real frames.
Managed automation bounds are valid on Windows and Mac. Refreshing the
native scroll peer and temporarily bypassing the custom width wrapper did
not change the native bounds, so neither experiment was retained. The
virtualized preview still exposes no native Mac AX rows; the complete
Previous/Next mapping route remains available. Final-head CI for this batch
and other Mac form-body accessibility work remain open.

**24 October 2026, Mac spoken row restoration after Copy cancellation
(FCUX-004/010):** Repeating the native AX focus event for the same row did not
make VoiceOver speak its filename. The main window now keeps a quiet,
non-interactive polite live region in the AX tree and announces the current
row's name and details only after the last modal has closed and that same
list still owns keyboard focus. A newer modal or focus move cancels the
announcement. `DialogFocusOwnershipTests` passed 27/27 focused cases on
Windows and 12/12 modal cases on Mac. The strict muted native VoiceOver
Copy-cancel journey on the signed Mac app (DLL SHA-256
`0ec2838a210fed389cf7ab48e541260cfcf7858c6c31659dc6c30d6a066633ce`)
heard “Focus returned to b.txt, file, 5 bytes…” on two consecutive runs
while AX focus remained on the unmarked `b.txt` row; the same build passed
the viewer return-focus journey
(`mac-voiceover-copy-action-focus-speech-20261024-r2/r3.json`,
`mac-voiceover-viewer-action-focus-speech-20261024-r1.json`). The native
runner now fails if Copy cancellation does not speak the restored filename.
Other modal routes and final-head CI remain open.

**24 October 2026, native Windows 2,001-file FTP/FTPS/SFTP listings
(FCUX-010):** `win-ax-remote-large-listing.ps1` passed plain FTP, explicit-TLS
FTPS and SFTP on the Win11 VMware guest using a fresh detached Git worktree at
`2cc6a7e2` (app DLL SHA-256
`27645AD162319C44501944FC04001FEAC8F9D7B79A940545E9A14592CEA34233`).
Each remote server exposed 2,000 generated empty files and one unchanged
target. Native UI Automation visited 239 nodes on load and 238 after physical
End focused `target.txt`; it exposed at most 25 rows. FTP/FTPS/SFTP initial
queries took 1.329/1.364/1.472 seconds, final queries
1.441/1.331/1.375 seconds, and End journeys 1.641/1.539/1.449 seconds.
The plaintext route required explicit consent; FTPS accepted a fresh
certificate once; SFTP matched the prompt to Ubuntu's independently read SSH
host public key before one-time approval. The two source files and initially
empty local target were unchanged; FTP/FTPS server files were unchanged, and
Ubuntu independently verified the SFTP manifest and bytes before removing
its exact fixture. Each runner stopped its owned app and any owned server,
then removed its local fixture
(`win-{ftp,ftpes,sftp}-large-listing-2cc6a7e2-r3/r3/r1.json`, Ubuntu
`WindowsLargeSftpListing/{seed,verify,clean}-9f87b73c5f4a4bb383465ddaf6ddc2a4.json`).
The runner retries bounded transient UI Automation `E_FAIL` responses during
modal replacement. Other supported provider types remain open.

**24 October 2026, Mac remote accessibility and modal popup ownership
(FCUX-010/UXBUG-016):** A new owned native runner,
`mac-ax-remote-large-listing.py`, passed FTP, explicit-TLS FTPS and SFTP on
the unlocked Mac desktop with 2,000 generated zero-byte files plus one
unchanged remote target. The signed candidate's app DLL SHA-256 was
`9cc016b9fe6e4d939214f4c393ae3818830d6f68fb79150c33d8d708c1fc2f80`.
Native AX visited 496 nodes for FTP/FTPS and 508–509 for SFTP, exposing at
most 27 rows; initial queries took 0.207/0.208/0.172 seconds and final
queries 0.212/0.208/0.217 seconds. The Mac End key scrolled to the tail while
retaining a nearby focused row; 25 physical Down keys then focused the final
`target.txt` in 0.846/0.869/0.857 seconds. The SFTP host-key prompt matched
the independently generated server fingerprint before one-time approval.
All three routes retained the exact two-file source, remote target and 2,000
generated files, stopped the owned app/server and removed their fixtures
(`mac-{ftp,ftpes,sftp}-large-listing-popupfix-20261024-r1.json`).

The SFTP native journey initially exposed a real modal regression: selecting
"Private key file" from the sign-in ComboBox closed the connection dialog,
so its file input disappeared. `OverlayDialogService` now treats the content
of open popups attached to the active card as belonging to that modal for
pointer, Escape and focus ownership, while outside clicks still use the safe
cancel result. `OverlayDialogPointerDismissTests` passed 7/7 on Windows with
16 build workers, including a new popup ownership regression and the existing
backdrop and narrow-footer safety routes; the same 7/7 passed on macOS arm64.
The patched Mac build had zero warnings and passed the native SFTP choice as
well as the FTP and FTPS reruns.
Other supported provider types and popup families' native scaled acceptance
remain open.

**24 October 2026, native Ubuntu 2,000-file remote listings (FCUX-010):**
`linux-atspi-ftp-large-listing.py` passed fresh FTP, explicit-TLS FTPS and
SFTP paths on the logged-in GNOME desktop with the Git-built `aec2253a` app
(DLL SHA-256
`c6bceebead49e2049d9bc6e75a3b7d6858780fc3073cee55a9503bc884045583`).
Each remote list exposed 2,000 exact files, 21 visible items and 474 AT-SPI
nodes on load; after physical End focused `native-remote-1999.txt`, it exposed
20 items and 473 nodes. FTP/FTPS/SFTP initial native queries took
1.263/1.244/1.250 seconds, later queries 1.195/1.093/1.019 seconds, and
the End journeys 2.147/2.057/1.974 seconds. The SFTP decision's displayed
host key matched the host's independently read SSH public key. Every route
preserved the local source and 2,000 remote zero-byte files, terminated its
owned app and server and removed its fixture
(`ubuntu-{ftp,ftpes,sftp}-large-listing-aec2253a-r2/r2/r1.json`). Other
supported provider types remain open.

**24 October 2026, native Windows 2,000-file accessibility listing
(FCUX-010):** The Win11 VMware guest passed `win-ax-large-listing.ps1` on its
isolated Git checkout at `4c0d9ddf` (app DLL SHA-256
`4845492B8E7A170FBD40EFDD206D15D16CBD50B3F8CCBA5F49F1981366A74C21`).
For 2,002 source files, native UI Automation visited 238 nodes and exposed
22 generated rows in 2.548 seconds. End focused `native-list-1999.txt` in
1.997 seconds; the subsequent query visited 237 nodes and exposed 24 rows
in 1.823 seconds. The original source bytes and empty target survived, and
the runner stopped its exact app and removed the generated fixture
(`win-large-listing-pointerb-r4.json`). `git diff 4c0d9ddf..eb6a3008`
shows no change to `FileListAutomation`, `FileListControl` or
`DescendantFocusAutomation`, the code exercised by this check. All three
desktop backends have now passed bounded 2,000-file local listings;
provider-backed native latency remains open.

**24 October 2026, native Ubuntu 2,000-file accessibility listing
(FCUX-010):** `linux-atspi-large-listing.py` passed on the logged-in GNOME
desktop with a zero-warning Git build of `aec2253a` (app DLL SHA-256
`c6bceebead49e2049d9bc6e75a3b7d6858780fc3073cee55a9503bc884045583`).
For 2,002 source files, native AT-SPI visited 472 nodes and exposed 21 list
items in 2.453 seconds; after physical End focused
`native-list-1999.txt`, it visited 471 nodes and exposed 20 list items in
1.163 seconds. End and focus confirmation took 2.100 seconds, within the
bounded 10-second navigation gate. The original source bytes and empty target
remained unchanged; the runner removed its generated files and stopped its
exact FileCat process (`ubuntu-large-listing-aec2253a-r1.json`).

**24 October 2026, native Mac 2,000-file accessibility listing (FCUX-010):**
The signed candidate with app DLL SHA-256
`df5104138969f85a02275794408d95e4830ac95dee22e661760e5415ce6148cb`
passed `mac-ax-large-listing.py` on the unlocked Mac desktop. With 2,002
files in the isolated source, native AX visited 508 nodes and exposed only 24
rows in 0.520 seconds; after ten Down keys it visited 495 nodes and exposed
24 rows in 0.152 seconds. The ten-key journey completed in 0.497 seconds and
focused `native-list-0010.txt`. The runner removed its 2,000 generated files,
confirmed both original source bytes and the empty target, and the exact test
app process was closed (`mac-large-listing-macfix-20261024-r1.json`).

**24 October 2026, Mac dialog title CI repair and Ubuntu Orca journey:**
The macOS App lane on `0fdd8d4f` exposed 13 regressions after the new Copy
dialog title announcement: a headless Mac window produced an extra live-region
phrase and retained closed dialog controls and icon borrowers. `DialogCard`
now enables that title's polite live setting only after attachment to a real
native macOS window. The affected Mac App tests passed 14/14 on the Git-built
candidate with 16 workers; the same 14 passed on Windows. A signed native Mac
bundle with app DLL SHA-256
`df5104138969f85a02275794408d95e4830ac95dee22e661760e5415ce6148cb`
passed the actual muted VoiceOver Copy journey: it spoke “Copy” on entry,
focused the Destination field, exposed the close X and Copy action, then
returned AX focus to the unchanged `b.txt` row after Escape
(`mac-voiceover-copy-action-macfix-20261024-r1.json`). The previously observed
failure to speak that restored row was subsequently fixed and verified in
the Mac spoken row restoration run above. Final-head CI remains open.

The Ubuntu Orca runner now starts under the logged-in Wayland backend when
invoked over SSH and fails promptly if its owned Orca exits. Its mark assertion
checks b.txt after the rename review returns focus, when Orca announces that
row's updated state. On the Git-built `278b8655` candidate with app DLL SHA-256
`5b7543d929e7fa33ed0bf53c05ffd44909c251186beae3cdc6edfdbd0127d1b0`,
the actual Orca 50.2 journey passed all 39 speech checks, including the
two-source rename review's complete paths, Previous/Next mappings, Cancel,
and marked b.txt on return. The source and target remained byte-exact
(`ubuntu-orca-list-check-candidate-r4.json`). The same report measured two
spoken copies of each a.txt, b.txt and c.txt row detail, so the duplicate
Orca tutorial/description remains open. Inspection of the locally pinned
Avalonia.FreeDesktop.AtSpi 12.1.1 assembly confirmed both
`AtSpiAccessibleHandler.get_Description` and `get_HelpText` call the same
`AutomationPeer.GetHelpText`; changing FileCat's single HelpText would remove
the row's size, mark and position from both properties. A bridge-level
separation is needed before this can be resolved without losing speech.

**24 October 2026, native Ubuntu checksum scope and clipboard (FCUX-008):**
The logged-in Ubuntu desktop exposed an Avalonia 12.1 AT-SPI gap: its read-only
checksum result entry returned the fixed label “Checksum results” through the
Text interface, leaving the actual digest unavailable there. On Linux the
viewer now mirrors the complete current result into that entry's accessible
description while keeping its concise name. The focused Linux App scope suite
passed 13/13 with 16 workers, including a check that the description follows
the completed result. An isolated Git-built candidate on the Ubuntu VM built
with zero warnings (app DLL SHA-256
`5b7543d929e7fa33ed0bf53c05ffd44909c251186beae3cdc6edfdbd0127d1b0`)
and passed `linux-atspi-viewer-checksum.py`
(`ubuntu-checksum-candidate-r7.json`): native
Shift+Left selected byte zero; the selected-byte and whole-file SHA-256,
CRC-32 and counts were exact; both complete results were exposed through
AT-SPI; the XWayland clipboard retained its sentinel until each explicit
Copy, then held the exact visible result; and the source and target stayed
byte-exact. The runner uses `xclip` because a long-lived GTK test clipboard
object cached its own sentinel after FileCat replaced the selection. Native
`--stale-scope` separately changed the source after opening a one-byte scope:
the viewer showed the changed-file refusal through AT-SPI, disabled Copy,
kept the clipboard and target unchanged, and the fixture was restored
(`ubuntu-checksum-stale-candidate-r2.json`).

The same candidate passed `--narrow` at GNOME 200% in a measured 1440 × 1000
physical viewer frame (720 × 500 app units). AT-SPI bounds kept the scope,
Compute, Copy results, Close checksums and selectable result entry within the
frame before computation and after each selected-byte and whole-file result;
hashes, exact clipboard Copy and fixture preservation still passed
(`ubuntu-checksum-narrow-candidate-r2.json`). Other display scales and native
incomplete-source/mutation-during-read paths remain open.

The complete Windows Release App suite started on `0fdd8d4f` and finished
with 2,462 passed, 63 platform/environment skips, zero failures and a
20-minute-40-second test duration using 16 workers. The checksum change on
`0746cf93` retains its separate focused Linux and native Ubuntu checks;
final-head CI remains the cross-platform gate.

**24 October 2026, Mac spoken in-window Copy entry (FCUX-004/010):** A new
`mac-voiceover-list.py --copy-action` journey exposed a real speech gap:
the native AX tree focused the Destination field of an actionable Copy review,
but VoiceOver's last phrase remained the prior listing update. The shared
`DialogCard` now repeats its visible title through the existing Mac live-region
bridge after attachment; Windows and Linux focus behavior is unchanged. A
separate signed Mac bundle (binary SHA-256
`4a8e1e28d756b0a55392f7fb777ff6f05cb82f1a521c9260d400618d905f2d97`)
made VoiceOver speak “Copy” at Retina 2× and 1×. Both runs confirmed the
Destination AX field, enabled Copy action, accessible close X, unchanged
source/target, and AX focus restored to the unmarked `b.txt` row on Escape
(`mac-voiceover-copy-action-livedialog-20261024-r3.json`,
`mac-voiceover-copy-action-livedialog-1x-20261024-r1.json`). The 1× wrapper
restored the original Retina mode. VoiceOver did **not** automatically speak
the returned row on either run with that earlier bundle; the current bundle
passes the retest recorded above. A delayed repeat of the
window focus event did not change this and was discarded. The same signed
bundle passed minimum-window native X/backdrop Copy cancellation and the
existing marked-review VoiceOver journey. Thirty-six related App tests passed
with 16 workers; the native speech test provides the Mac-specific regression.

**24 October 2026, final-head modal ownership on Windows and Ubuntu
(FCUX-004):** The Windows VM's Git-built `fd141cc3` app passed
`win-ax-modal-guard.ps1`: native UI Automation invoked background toolbar
and key-bar Copy/Delete controls while exactly one Copy review was open.
All four actions left that review and its Destination field intact; Cancel
closed it with unchanged source SHA-256 and no target
(`win-modal-guard-fd141cc3.json`). The Ubuntu VM at GNOME 200% passed the
same four AT-SPI actions against Git-built `278b8655`, with one review before,
none after Cancel, and no target (`ubuntu-modal-guard-200-278b8655-r3.json`).
The first Ubuntu run lacked `DOTNET_ROOT` for its framework-dependent Git
build. `linux-atspi-modal-guard.py` now detects the test VM's isolated pinned
SDK when present; the final run passed without an external runtime override.
Neither VM left a FileCat process running.

**24 October 2026, Windows scaled shared-dialog pointer acceptance
(UXBUG-016):** A separate Windows VM Git worktree built `fd141cc3` with .NET
10.0.402 and 16 workers (app SHA-256
`9647CA9F01912FEF1569FF9F0A9DAAE0C2F7B7A85D13BA06444719E254F85E2C`).
The native Display Settings selector was set to 150% and visually confirmed;
the Registry, Create folder, Copy, Move and Delete guarded pointer runners
each reported 144 DPI, a 45 × 45 physical-pixel close target, a centered X
with (0, 0) centroid offset, accessible cancel help, and successful close-X
and backdrop cancellation. The Copy, Move and Delete source hashes and empty
targets remained unchanged. Receipts are
`win-{registry,folder,copy,move,delete}-pointer-150-fd141cc3*.json` in the
guest's isolated validation directory. Display Settings initially retained
foreground and the Registry runner correctly refused to send F7 to it; after
closing only Settings, all five routes passed. The native Scale selector was
restored to its original 100% value, confirmed in
`win-scale-restored-20261024.json`, and Settings closed.

**24 October 2026, Ubuntu scaled popup and Find acceptance (UXBUG-016):**
The Git-built `278b8655` app passed physical Shift+F10 context-menu and
Alt+F7 Find journeys at GNOME 200% scale. A bounded native runner pause let
the VM capture both open states; the menu and Find controls fit within the
2560 × 1600 physical display with readable labels and visible actions
(`ubuntu-popup-200-278b8655.png`, `ubuntu-find-200-278b8655.png` in the
isolated VM validation directory). The runner's
`ubuntu-keybar-scaled-visual-278b8655.json` report confirms the menu and
Find windows opened, Esc restored the twelve-key bar after each, the source
hash and empty target remained unchanged, and the display scale was 2.0.
GNOME's Alt+F7 window-move binding was restored exactly; the runner stopped
its owned FileCat process and removed all capture markers. The two captures
were verified byte identical after transfer into the VM validation directory.

**24 October 2026, Mac minimum-window dialog pointer acceptance
(UXBUG-016):** Copy, Move and Delete review dialogs passed native close-X and
backdrop mouse cancellation in a 640 × 480 point FileCat window at Retina 2×
and 1×. Native AX confirmed each enabled approval button was
inside the narrowed window and measured the accessible close button at 30 ×
30 points. Exact fixture bytes and the empty destination survived every
cancellation. The first narrow attempt was a test-runner error: a transient
tooltip appeared as System Events' `window 1`, so its supposed backdrop click
went to a different app. The runner now identifies the main FileCat window
by title and size and records the actual click position. The corrected runner
passed against both the earlier signed build and the new root pointer guard
build (`mac-dialog-copy-pointer-narrow-oldcorrected-20261024-r1.json`,
`mac-dialog-{copy,move,delete}-pointer-narrow-overlayroot-20261024-r4/r1/r1.json`,
`mac-dialog-copy-pointer-narrow-1x-overlayroot-20261024-r1.json`). The root
pointer guard separately prevents a minimum-window press on a footer beyond
an overlay hit surface from invoking an underlying action; a focused headless
test failed before the guard and passed afterward. Six pointer cases and 25
related dialog/focus cases passed with 16 workers. The Mac signed build uses
an isolated locally installed .NET 10 SDK; its focused App suite passed 6/6.
The privileged Windows `$LogFile` retry correction passed all five lanes of
CI run `40075217938`; the final-head five-lane gate remains open.

The further 1× minimum-window Move and Delete runs passed with the same
signed build (`mac-dialog-move-pointer-narrow-1x-overlayroot-20261024-r1.json`,
`mac-dialog-delete-pointer-narrow-1x-overlayroot-20261024-r1.json`). Each
recorded an in-window approval button and close target, delivered native X and
backdrop clicks, preserved the byte-exact source, and restored the original
Retina display mode.

**24 October 2026, Mac dialog pointer scaling (UXBUG-016):** The signed
`20df90d0` build repeated Create folder, Copy, Move and Delete native close-X
and backdrop cancellation in the supported 1920 × 1200 logical/physical
**1×** display mode. All four exact-process AX reports found a 30 × 30 point
close target; the review reports confirmed their enabled approval controls,
and source/target fixtures stayed unchanged
(`mac-dialog-{create,copy,move,delete}-pointer-1x-20261024-r1.json`). Each run
used `mac-display-run.swift` to verify restoration to the original 1440 × 900
logical / 2880 × 1800 physical Retina mode.

**24 October 2026, Mac transfer and deletion review pointer acceptance
(UXBUG-016):** The signed `20df90d0` Retina build passed native CGEvent close-X
and backdrop cancellation for Copy (`mac-dialog-copy-pointer-20261024-r4.json`),
Move (`mac-dialog-move-pointer-20261024-r1.json`) and Delete
(`mac-dialog-delete-pointer-20261024-r1.json`). An exact-PID AX probe confirmed
each review's enabled approval control, the Destination field for Copy/Move,
and one accessible 30 × 30 point Close dialog button. Each run reopened its
review for the second pointer route, then verified the owned `b.txt` source
remained byte exact, the target empty and the test app stopped. The same AX
helper change passed Create folder pointer cancellation and open-dialog
SIGTERM again (`mac-dialog-create-pointer-20261024-r5.json`,
`mac-dialog-create-sigterm-20261024-r3.json`).

**24 October 2026, Mac open-dialog termination (UXBUG-016):** On the signed
`20df90d0` Retina build, the native AX runner entered and read back the
`unsubmitted` Create folder draft, verified the dialog was still open, sent
SIGTERM to the exact owned FileCat PID and observed exit within ten seconds.
The source and target remained empty; the runner stopped its owned process
and removed its fixture (`mac-dialog-sigterm-20261024-r2.json`). Its pointer
mode also passed again after the runner change
(`mac-dialog-pointer-20261024-r4.json`). This supersedes the earlier
exploratory observation below that an open dialog prevented SIGTERM exit.

**24 October 2026, Mac dialog pointer acceptance (UXBUG-016):** The signed
`20df90d0` Retina build passed a fresh Create folder close-X and backdrop
mouse journey (`mac-dialog-pointer-20261024-r3.json`). Native AX measured one
enabled, cancel-labeled `Close dialog` button at 30 × 30 points; both exits
closed the overlay and left the owned source and target empty. An initial
backdrop run was obscured by an unrelated FaceTime account alert. After its
authorized dismissal, a second run showed VoiceOver's floating caption over
the test's lower-left click point. The runner now clicks the visible left
pane above that caption, outside the centered dialog. The corrected result
passed with point (160, 280) in the 1280 × 799 point FileCat window.

**24 October 2026, Windows CI live NTFS log test:** The `bba12fd8` Windows
lane failed one privileged `WindowsFileRecordsTests` case. Its bounded retry
stopped when the report had two timestamp signs, although those signs could
come from the MFT and journal before the raw `$LogFile` held the fixture's
backdated-time image. The retry now waits for that specific `$LogFile` finding.
If the live circular log retains the creation and name but never exposes the
time image within the deadline, the test records a skip; if the detailed time
row exists without its warning, it still fails with the report. A regression
case proves unrelated aggregate signs do not satisfy the gate. The focused
case passed 1/1 and the complete non-elevated Windows platform suite passed
424, skipped 46 and failed none with the normal parallel runner; privileged
CI validation of this correction remains pending.

**24 October 2026, Ubuntu Orca Hex search speech (FCUX-007):** The Git-built
`44f246a2` Ubuntu app passed the 200% native Hex editor journey while an owned
Orca instance read its match-count live region. Orca's `SPEECH OUTPUT` log
contained one, two, then one distinct match after the query changed (records
42, 48 and 66). The native route independently checked two matches over 4 MiB
apart, Previous and physical F3/Shift+F3, measured Find controls, unchanged
7 MiB source bytes and editor closure (`orca-hex-scaled-r5.json`; the private
speech and native reports remain in its VM fixture). The new
`linux-orca-hex-search.py` requires both results and verifies that its owned
FileCat and Orca processes stop. Its first SSH launches
lacked the logged-in GNOME display authorization; the runner now verifies
that user's Xwayland auth file and Wayland socket before launching Orca.

**24 October 2026, Windows NVDA Hex search speech (FCUX-007):** A signed,
portable NVDA 2026.2 instance on the isolated Win11 VM spoke the Hex
editor's visited-match counts in order: one, two, then one after the query
changed. Its own `Speaking [...]` log gave records 44, 47 and 62 at both
100% (`nvda-hex-r2.json`), 150% (`nvda-hex-r5.json`) and 200% (`nvda-hex-r4.json`). Each run's native
Hex journey passed first physical row click, distant matches,
Previous/no-earlier feedback, exact source-byte preservation and fixture
cleanup. `win-nvda-hex-search.ps1` requires both the native and spoken
results, keeps the private speech and native reports under its test-owned VM
fixture, and writes only the three relevant plain-text phrases to its summary.
The Windows PowerShell 5.1 parse and ordered speech/JSON checks passed. The
150% and 200% VM runs were restored to the original 100%, confirmed by their
scale receipts.

**24 October 2026, Windows narrow 200% Hex acceptance (FCUX-007):** The
Git-built VM app shrank from 1500 × 1000 to its actual 200% minimum of
1306 × 911 physical pixels. In that measured layout, the first physical row
click focused the source; Hex search then found two matches over 7 MiB apart,
returned through Previous, reported the no-earlier result, reset the visited
count on a changed query, preserved all 13 MiB of source bytes and removed
the owned fixture (`pointer-hex-narrow-200-r12.json`). The native runner
requires both the measured shrink and first-click focus to pass. The VM was
restored to 100%, with the restore confirmed in
`pointer-scale100-r12.json`.

**24 October 2026, Windows 200% Hex pointer check (FCUX-007):** The Git-built
Windows VM Hex editor found and traversed two matches over 7 MiB apart, reset
its visited count for a changed query, preserved exact bytes and closed its
owned fixture at 200% scale. The native runner now records the file-row bounds,
actual pointer coordinates, hit HWND and first-click focus outcome, waits for
accessibility updates and gives a synthetic mouse press a human-scale interval.
With FileCat already active and the display Settings window closed, one click
selected the row (`pointer-hex-200-r4.json`). The earlier two-click result was
caused by the native runner's emergency activation click: at 200%, a guessed
title-bar point landed in FileCat's menu, and its popup intercepted the next
row press. The existing bounded menu trace showed the first press entering a
menu icon presenter and the second entering `FileListControl`. The runner now
repositions an offscreen window and verifies a visible non-client caption
target before any activation click. The original startup route then passed
first-click selection and the full Hex search (`pointer-hex-200-r9.json`),
with its first press reaching `FileListControl`. No product row-click change
was needed. The VM was restored to its original 100% scale after each run.

**24 October 2026, Windows ARM64 FTP fixture readiness:** The `3a4bce53`
ARM64 CI lane exposed a transient Windows sharing denial while the owned FTP
server replaced its readiness file. The bounded fixture poll now treats that
denial like an unpublished file and retries until its existing deadline. A
focused test injects the denial and confirms the next read succeeds; the full
owned FTP and explicit TLS fixture smoke test passed locally. The `e73e57d8`
CI run passed all five lanes, including ARM64. The final-head gate remains open.

**24 October 2026, macOS CI checksum generation test:** The `ac342092` macOS
App lane failed one checksum-dialog generation case because its test's busy
five-second spin did not observe the actual worker continuation. The test now
polls asynchronously for up to 20 seconds, yielding the UI scheduler while
still requiring the real worker to post. All six focused Windows cases passed
with 16 available test workers. The complete Windows App Release suite then
passed 2,461 tests, skipped 63 platform cases and failed none in 20 minutes
8 seconds using the resource-bounded settings and up to 16 build workers.
The `55fd1dc9` CI run passed all five producer and platform lanes, including
macOS; the final-head gate remains open. A separate clean Mac test checkout could resolve the
pinned .NET 10.0.101 SDK, but its first restore exited after five minutes
without compiler errors; it is not counted as a passing Mac test.

**24 October 2026, FTP link traversal safety (FCUX-001):** A restored Ubuntu
FTP/FTPS lab exposed that its MLSD response reports a directory symbolic link
as a directory. FileCat's delete journey then followed that link and removed
a file in its target. The FTP channel now cross-checks machine listings against
LIST before jobs recurse, retains machine-listing names and metadata, and
treats entries absent from the corroborating listing as opaque links. The live
link test checks link identity before any mutation, preserves a hidden entry,
and verifies the target file survives rename, move, replacement and recursive
delete. A local pyftpdlib integration test checks the same classification and
target preservation in CI. The temporary lab's SFTP and FTP link cases, trust
and refused-upload cases, and the local integration test passed. The complete
restored SFTP/FTP/explicit TLS/implicit TLS Remote suite then passed 2,445
cases, skipped 139 lab/platform cases and failed none in 6 minutes 21 seconds
with up to 16 build workers. The same guarded link journey then passed against
Ubuntu's independent `vsftpd` 3.0.5 server over explicit TLS, whose FEAT
response omits MLST, checking the LIST-only path as well. The temporary
`vsftpd` listener was stopped and its separate configuration removed. The
final-head CI gate remains open.

**24 October 2026, live SFTP changed-source decisions (FCUX-001):** The
isolated OpenSSH lab now covers a local Move source changed while a nonempty,
incomplete SFTP upload stage is on the server. Cancel and Skip retained the
original local SHA-256 and left no public target or stage. Retry changed the
local bytes while the decision was pending, discarded the old stage, published
only the new SHA-256 and removed the local source after completion. All three
focused cases passed against the Ubuntu VMware guest. The complete portable
Remote Release suite then passed 2,419 cases, skipped 164 lab/platform cases
and failed none using the repository's resource-bounded xUnit setting and up
to 16 build workers. A separate full run with the live lab enabled passed
2,434 cases, skipped 139 and failed 10: the rebooted Ubuntu guest has SSH on
port 22 but no FTP/FTPS listeners on ports 21 or 990, so those FTP cases
could not connect or reach their certificate prompts. The focused SFTP cases
passed in that live run. The FTP/FTPS lab was subsequently restored as
described above; no product regression was inferred from absent services.

**24 October 2026, Windows scaled Hex Find acceptance (FCUX-007):** The clean
Win11 VMware Git checkout at `9cfb6b3c` built successfully with eight guest
workers using its installed .NET 10.0.402 SDK from the validation directory;
the pinned repository `global.json` was not changed. Isolated 13 MiB Hex
editor runs passed at Windows 100% and 150% display scale, with exact matches
at 0x100014 and 0x800014, Previous/Next, F3/Shift+F3, no-earlier feedback,
changed-query reset, unchanged source bytes and fixture cleanup. At 200%,
UI Automation exposed the file row at its physical screen bounds, but the
native test's raw `mouse_event` click failed to give it keyboard focus. The
runner now verifies focus before invoking Search commands and falls back to
an element-targeted native click while recording `nativePointerFocused`. The
200% journey then passed the full Hex Find sequence with
`nativePointerFocused = false`; this isolates the remaining raw pointer
question rather than treating it as a Hex Find failure. The VM display was
restored from 200% to its original 100%. This earlier run preceded the narrow
200% acceptance and the later spoken Windows Hex result are recorded above.

Follow-up 200% runs isolated the pointer behavior further. Replacing the
legacy click with `SendInput` did not fix the *first* press; a second press
focused the row with either API. Even after a 500 ms pointer hover, the first
press missed focus. A final run confirmed that FileCat was foreground before
and after the first press (`foregroundBeforePointer = true`,
`foregroundAfterFirstPointer = true`), while `firstPointerFocused = false` and
`nativePointerFocused = true` after the second identical press. Hex Find,
unchanged bytes and cleanup still passed. The runner now records both presses,
keeps an element-targeted fallback for the Hex route, and leaves native
one-click selection at 200% open for focused follow-up at that point. The
menu-activation cause and successful first-click rerun are recorded above.
The VM was restored again to 100%.

**23 October 2026, live SFTP upload cancellation (FCUX-001):**
`RemoteLabTests.Canceling_a_live_SFTP_upload_removes_its_stage_without_consuming_the_source`
now covers Copy and Move against the running Ubuntu VM's OpenSSH service.
Each 8 MiB source was rate-limited until an independent connection observed
a nonempty, incomplete `.fc-` stage and no public target. Cancel ended the
job, removed the stage, left the public folder empty and preserved the exact
source SHA-256. A separate Delete job removed the test-owned remote folder.
Both live cases passed with 16 available workers. The complete Windows Remote
Release suite passed 2,397 cases, skipped 159 platform/lab cases and failed
none in 33 seconds with 16 workers. The final-head CI gate remains open.

The same live OpenSSH lab now covers remote-to-local cancellation for both
Copy and Move. Each 8 MiB server source was read through FileCat at 64 KiB/s
until a nonempty, incomplete local `.fc-` stage appeared. Cancel removed that
stage without publishing the destination, left the remote source as the sole
server item with its original SHA-256, and completed exact test-folder
cleanup. Both focused cases passed. The existing real FTP cancellation case
now runs under explicit TLS too, using a fresh test certificate and a
one-connection trust decision. Both FTP and FTPS observed a nonempty hidden
upload stage, canceled before publication, removed that stage and preserved
the exact local source. The final complete Windows Remote Release suite for
this batch passed 2,398 cases, skipped 161 platform/lab cases and failed none
in 38 seconds with 16 workers.

The disposable pyftpdlib fixture now also starts TLS before the initial FTP
greeting for `ftps://` (implicit TLS). The cancellation regression reached a
nonempty private stage over that encrypted connection, canceled it, found no
public target or remaining stage, and preserved the source SHA-256. Plain FTP,
explicit TLS and implicit TLS all passed the focused run (3/3); the complete
Windows Remote Release suite passed 2,399 cases, skipped 161 platform/lab
cases and failed none in 42 seconds with 16 workers.

The real FTP changed-source Move regression now exercises Cancel, Skip and
user-approved Retry across plain FTP, explicit TLS and implicit TLS. After
the first partial upload, a changed source timestamp led to the failure
decision. Retry replaced the local source bytes while that decision was
pending, discarded the old stage, published exactly the new byte hash and
removed the local source only after the new upload completed. Cancel and
Skip kept the original source hash and left no published file or stage. All
nine focused cases passed. The replacement guard now also runs under implicit
TLS: a changed target, including changed bytes with its size and timestamp
restored, stayed intact while the local Move source and unpublished stage were
handled safely. All six focused replacement cases passed. The complete
Windows Remote Release suite passed 2,419 cases, skipped 161 platform/lab
cases and failed none with 16 workers. The final-head CI gate remains open.

**23 October 2026, native Mac SFTP panel pointer ownership (FCUX-001):**
LaunchServices runs with VoiceOver exposed the underlying intermittent focus
race. The physical source-tab press reached FileCat and activated panel 1, but
roughly 50 ms later macOS AX called `TextBoxAutomationPeer.SetFocusCore()` on
the previous right panel's Filter field and reactivated panel 2. Native AX hit
testing had already confirmed that the physical click landed on the source
tab. A first asynchronous focus-reclaim prototype caused repeated panel
switches during the short guard period; it was replaced before commit. The
panel path and filter fields now use a TextBox automation peer that rejects a
stale AX focus request for the previous panel during the 250 ms following a
physical press in another panel. A new pointer press or keyboard input ends
that priority immediately. The headless regression injects the late AX peer
request for both fields, verifies that the active panel never changes, then
checks that a new physical press and a key permit ordinary focus changes.
It passed with the requested 16-worker setting. An isolated signed Mac
candidate (`FileCat.dll` SHA-256
`faf9b566291d548f16e8bc88a7d7609743e48c619803f163eedb57e0d869d59e`)
passed the physical source-tab probe and the full 1× SFTP warning route.
The latter activated the source on both first clicks, displayed and spoke
the complete replacement warning in a 640 × 480 review, and kept source and
target hashes unchanged after Cancel and Ask/Skip
(`mac-remote-warning-pointer-fix-v2-probe-1x-r1.json`,
`mac-remote-warning-pointer-fix-v2-narrow-1x-r1.json`). The new native race
helper then clicked that source tab and sent `AXFocused=true` to the previous
right Filter field 35 ms later. macOS accepted the request (`AX result=0`),
but the source panel stayed active and the exact fixture hashes stayed
unchanged (`mac-remote-warning-pointer-focus-race-v2-1x-r3.json`). The first
two previous-build attempts could not click the tab because overlapping macOS
Apple Account and Archive Utility alerts hid it and made its native bounds
zero. After dismissing those authorized system dialogs, the same helper
reached the click on the previous build, macOS accepted the stale Filter
focus, and the source panel lost the race as expected
(`mac-remote-warning-pointer-focus-race-old-1x-r3.json`); both fixture hashes
still matched. The race runner now waits another 350 ms for delayed AX and
VoiceOver notifications before reading the active panel; the fixed build
passed that stronger check with the same native `AX result=0`, a first-click
source activation and unchanged hashes
(`mac-remote-warning-pointer-focus-race-v2-1x-r4.json`). The earlier normal
LaunchServices trace also shows source
activation followed about 50 ms later by AX focus returning to that Filter
editor. The complete Windows App Release suite passed 2,461 cases, skipped
63 platform-specific cases and failed none with 16 workers. The final-head
five-lane CI gate remains open.

**23 October 2026, CI native bootstrap and Mac SFTP pointer diagnosis:** The
`3175ff3e` Windows CI lane compiled the managed projects but the Visual Studio
native bootstrap compiler exceeded its two-minute deadline under runner load.
The bounded deadline is now five minutes, and timeout terminates the exact
owned `cmd.exe` compiler tree before temporary-file cleanup. The Windows
bootstrap cleanup regression now launches and stops an owned process in
addition to its locked-file and guarded-path checks; it passes under Windows
PowerShell 5.1. `dotnet build FileCat.slnx -c Release --no-restore -m:1
-p:UseSharedCompilation=false` passed with no warnings or errors locally.
The final-head CI result remains open.

The signed Mac 1× SFTP replacement test reproduced the intermittent physical
source-tab activation gap twice (`mac-remote-warning-sftp-pointer-narrow-1x-
3175ff3e-r3.json`, `-r4.json`): FileCat was frontmost, the pointer ended at
the exact source-tab AX center (358,280), and four clicks left the remote panel
active. Both fixture files and the remote manifest stayed unchanged. A repeated
run passed on the first source-tab click for both replacement decisions; the
640 × 480 review exposed and automatically spoke the complete replacement
warning, Cancel preserved both files, and the per-item Ask warning was spoken
before Skip preserved the remote target (`-r5.json`). The runner now records
frontmost process, pointer coordinates and tab bounds on each attempt, and
probes the source row if the tab fails again. The intermittent tab route remains
open for diagnosis; successful SFTP transfer review does not erase that
reproduction.

The `r6` full review and `r9` short probe also passed on the first click.
`r7` and `r10` reproduced the failure again with FileCat frontmost and the
pointer at the source-tab AX center. A subsequent physical source-row click
also failed to activate the local panel, narrowing the fault to pointer
delivery across that panel rather than tab button handling. Source and target
hashes stayed exact. A first Swift accessibility hit probe compiled under
`swiftc -typecheck` but failed in the Swift interpreter at runtime; the helper
now builds as an owned native executable with ApplicationServices and runs only
when the pointer failure recurs. Its first smoke test returned a native
desktop hit. `r11` then reproduced the pointer failure and the compiled
native hit test identified the **source tab's static text** at (358,280) and
the **source list** at the clicked row; neither pointer action activated that
panel. This rules out a misplaced click or an overlay obscuring those points.
The `330dfd94` Windows CI lane passed the native cleanup
regression and Release build, then entered its complete test projects. A
local App suite attempt with 16 workers and resource-bounded settings aborted
before test execution when VSTest could not connect to its testhost within
90 seconds; no product assertion was reached. The complete CI gate remains
open.

**23 October 2026, Mac marked-review modal focus (FCUX-004/010):** A native
VoiceOver run exposed a dialog focus regression: invoking Review marked items
from command search displayed the review while keyboard focus stayed on the
search field behind it. `OverlayDialogService` now moves initial focus into
the current modal even when the invoking field retained focus, and redirects
later outside focus back into that modal. `DialogFocusOwnershipTests` covers
both timings. The isolated ad-hoc-signed Mac candidate (`FileCat.dll` SHA-256
`633871648fff92922bc7894615bf2441297604403ff68a113aedf0b1f1ccc8c2`)
passed `mac-voiceover-list.py --review` with muted VoiceOver
(`mac-voiceover-marked-review-modal-focus-r8.json`): the accessible focused
item was `b.txt, file`, Down did not escape to command search, Escape returned
to a source-list row, and `b.txt` remained marked with all fixture bytes
unchanged. A screenshot also shows the complete review and source path. The
last VoiceOver phrase during the review was the window name rather than the
marked row. A follow-up receipt (`mac-voiceover-marked-review-modal-focus-r10.json`)
captured a second AX focus notification for the review row while VoiceOver's
navigation cursor still reported the invoking Search commands field. The
search field also generated repeated focus notifications after the row was
focused. Spoken marked-review content and other focus routes remained open
at that stage. The same candidate passed the existing spoken F3 viewer
open/Escape return journey (`mac-voiceover-list-actions-modal-focus-r1.json`).
The first complete Windows App Release run found 12 dialog-lifetime failures:
the initial root focus callback captured a closed dialog's session. The root
handler now uses an instance method so closed sessions can be collected;
all 32 affected focus and lifetime cases pass. A complete rerun and the
final-head CI gate remain open.

The subsequent signed Mac candidate (`FileCat.dll` SHA-256
`161532070b9d66d02d699a42dec641c0b10db54d96af31b7d6b832a43a840a76`)
closed the spoken entry gap. Command dispatch clears and leaves the search
field before opening a modal, and the main window removes that field from the
visual/accessibility tree while an overlay is open. The marked-items review
repeats its bounded, polite scope summary once after it attaches, and exposes
the focused item through the list row while its full path remains in the
details field. On the muted Mac, the native
`mac-voiceover-list.py --review` runner twice heard VoiceOver speak
`b.txt, file`; the VoiceOver cursor was inside `Marked items on this page`,
and AX emitted the one-item scope announcement without later search-field
focus events. Escape returned to the marked source row with unchanged bytes
(`mac-voiceover-marked-review-announcement-r1.json` and `-r2.json`). Focused
Windows search, list automation, dialog and lifetime tests pass 78/78 with
16 workers; the existing Mac spoken F3 viewer journey passes again on this
candidate (`mac-voiceover-list-actions-modal-announce-r1.json`). Full App and
final-head CI remain open; multi-item paging,
hidden marks and other native item actions still need spoken acceptance.

The complete App suite exposed a live-region naming gate: assigning a fixed
accessible name to the changing review summary would prevent later text from
being announced. The summary now keeps its text-derived name. All 79 focused
review, search, live-region, focus and lifetime tests pass with 16 workers.
The corrected signed Mac candidate (`FileCat.dll` SHA-256
`2c8795102bfc21cdd519085f874efe427b97a17e0fe92adacb0128631c494e85`)
again passed the spoken one-item review
(`mac-voiceover-marked-review-live-name-r1.json`). Its new `--review-pages`
journey marked 73 exact source files, heard VoiceOver speak the first focused
item, reached Next page by Tab, and heard the remaining `65–73 of 73` scope;
both pages exposed their counts through native AX with the empty target and
all source hashes unchanged (`mac-voiceover-marked-review-pages-live-name-r1.json`).
Filtered hidden marks, other item actions and the complete final-head suite
were still open at that stage.

The same signed candidate then passed the native filtered selection route
(`mac-voiceover-marked-review-hidden-r3.json`). After reviewing all 73 marks
across two pages, the Mac Quick filter accepted `item-00*`, leaving ten visible
files and 63 hidden marks. The reopened review exposed `63 marks are hidden`
through AX, kept a hidden `item-010.txt` row, and exposed its complete original
source location in the read-only details field. VoiceOver spoke the row's
`hidden in file list` state; all 73 source hashes and the empty target stayed
exact. Other Mac item actions, other desktop assistive-technology routes and
the full final-head gate remain open.

**23 October 2026, native Mac long-line comparison (FCUX-009):** A signed
`20df90d0` Mac build opened two files whose changed suffixes followed 2,000
shared characters. `mac-ax-compare-long-lines.py` used native Ctrl+I and F8,
copied both complete 2,010/2,008-character source lines through the visible
Copy line action, retained the clipboard through navigation, and preserved
both source hashes. Captured Mac desktop views show each differing suffix in
the primary aligned surface and in the paired full-line detail. The same
journey passed at the default 1200 × 788 size and a measured 640 × 600
comparison window (`mac-compare-long-20df90d0-r2.json`,
`mac-compare-long-narrow-20df90d0-r3.json`; DLL SHA-256
`b238976cc0f24a35f6965fa014289c925761c74b6fa55a1e6b3ddf3fdf3dc80f`).
The 640 × 600 wrapped route also passed: F8 revealed both changed suffixes on
the same aligned visual row, and Copy line returned the complete second source
line (`mac-compare-long-narrow-wrap-20df90d0-r1.json`). The same wrapped
640 × 600 route passed in supported 1× mode
(`mac-compare-long-wrap-1x-20df90d0-r1.json`), and the display returned to
Retina afterward. Font fallback,
pointer selection, spoken inspection and Linux native comparison remain open.

**23 October 2026, timed administrator notice (FCUX-029):** The startup
administrator advisory now uses the existing informational toast lifetime:
it expires after six seconds, pauses while the notice is hovered or focused,
and remains in Recent notifications for later review. Actual failures still
require deliberate dismissal. `NotificationCenterTests` verifies the real
startup notice classification, timer, expiry and retained history; all seven
notification tests pass with 16 test workers. Native Windows elevation and
scaled-layout acceptance remain open. The complete Windows App Release suite
passed 2,458 tests with 63 platform skips and zero failures in 19 minutes
41 seconds, using 16 workers.

Work starts with the 13 S1 findings in the supplied [76-finding audit](FileCat_UX_UI_Audit.md), then continues into the shared S2 dialog and viewer foundations and the S3 refinements. The implementation is organized by finding below. The second batch also resolves the original test failures and verifies ordinary-user permission behavior. Native desktop acceptance remains separate from automated headless checks.

**23 October 2026, CI FTP fixture readiness:** The five-lane `8c6a3950`
run passed producer, Ubuntu and macOS, but both Windows lanes hit the same
fixture race: `ready.txt` existed briefly before its port number was written.
The fixture now publishes that file with an atomic same-directory replace, and
the smoke test waits for a complete port while explicitly testing an absent,
empty and populated ready file. The local loopback FTP and explicit-TLS FTPS
smoke tests passed after the fix. The final-head CI gate remains required.

**23 October 2026, actual Mac VoiceOver file-list focus (FCUX-010):**
The signed `8c833c35` Mac build exposed a VoiceOver-only regression: ordinary
Down navigation asked the AX selection provider to select each row, turning
focus movement into a FileCat mark and speaking the list's marked-items help
instead of `b.txt`. An earlier VoiceOver-off AX journey left the same rows
unmarked. The Mac bridge now omits the selection-item provider for virtual
rows; their ItemStatus and HelpText still report mark state, and explicit
Space marking remains available. A separate signed build from Git head
`25e007df` plus this source patch (`FileCat.dll` SHA-256
`19bac6c44899e3b517d7155f6e7fbb6f633b1ca95a183117823cdb3b61997415`)
passed `mac-voiceover-list.py` on the muted Mac: VoiceOver spoke `a.txt`,
`b.txt`, `c.txt`, then `b.txt` again; one-shot AX probes found each focused
row unmarked; Space marked and then unmarked `b.txt`; all source hashes and
the empty target stayed exact. The guest receipt is
`~/FileCatValidation/mac-voiceover-list-explicit-mark.json`. The focused
FileListAutomationTests pass 19/19 on both Windows and Mac with the platform
guard; other Mac item/action speech, Orca's repeated tutorial help and
large-listing latency remain open.

The signed `20df90d0` build then passed `mac-voiceover-list.py --actions`
with Mac output muted (`mac-voiceover-list-actions-20df90d0-r3.json`, DLL
SHA-256 `b238976cc0f24a35f6965fa014289c925761c74b6fa55a1e6b3ddf3fdf3dc80f`).
After the same four spoken row moves and explicit mark/unmark, native F3
opened `b.txt`. VoiceOver spoke the named viewer and its text; Escape closed
it and VoiceOver spoke the restored `b.txt` row. A one-shot AX probe confirmed
that row remained unmarked. Every source hash and the empty destination stayed
exact. Other item actions, marked-item review and large-listing latency remain
open.

**23 October 2026, spoken retained-report navigation (FCUX-018):**
`ReportWindow` now offers Previous/Next report line controls and a visible
polite live readout with the line position. A single readout segment is bounded
to 4,096 UTF-16 units, preserves a surrogate pair at the boundary, and can be
navigated in either direction; the original report, Find, Copy all and Save as
are unchanged. The readout escapes hidden control and bidi characters. Four
focused report tests pass, and the related ownership, search, copy and Undo UI
selection passes 62 tests with one platform skip. On exact Git commit
`44f246a2`, the Ubuntu 200% Orca runner spoke both full paths from line 3 of
3 after Next was pressed twice; the Win11 NVDA runner spoke both full paths
from a failed inverse on line 3 of 44 and retained all 42 outcomes. The
signed Mac bundle
(`FileCat.dll` SHA-256
`d9ce231aeba35c991107abec3e882887826e73aaac5523ec6c209aca112268c4`)
spoke both complete paths in the narrow review, the report itself and the
third navigated line, while Copy all and the exact Undo bytes remained valid.
The final guest receipts are `ubuntu-undo-orca-44f246a2.json`,
`win-undo-nvda-44f246a2.json` and
`~/FileCatValidation/mac-undo-report-44f246a2.json`. Final-head CI remains
required.

**23 October 2026, Windows CI native-bootstrap cleanup:** The
`b74e8b00` five-lane run passed producer, Ubuntu, macOS and Windows ARM64,
but Windows solution build failed after compilation when another process
briefly held `NativeBootstrap.obj` and `Remove-Item` in a `finally` block
turned temporary cleanup into a build error. The exact-directory guard now
retries cleanup and reports a still-locked directory as a warning while
preserving any original compilation failure. A Windows regression holds the
object open, verifies that cleanup does not delete it, releases the lock,
verifies removal, and rejects an unsafe directory. It passed under both
Windows PowerShell 5.1 and PowerShell 7. The local PrivilegedHost Release
build then succeeded with the native compiler inventory (229 headers,
11 libraries, 7 tool components). The final-head five-lane CI gate remains
required.

**23 October 2026, real FTP crash recovery in the native UI (FCUX-001):**
`win-ax-ftp-crash-stage-review.ps1` and `win-ftp-owned-stage.py` ran against
the isolated Windows Git build of `b1042cb6` (`FileCat.exe` SHA-256
`E54A3D1EDC4BC8375EFE26DC818028D6B572BD8E3BA211F416E6BAA443D87B97`).
For both Copy and Move, a loopback FTP server accepted a rate-limited 8 MiB
upload until a hidden `.fc-<journal-id>-` stage existed. The runner killed
only its own worker, then found 786,432 retained stage bytes, no public target,
the unchanged source SHA-256 and one open recovery journal. The native FileCat
row, conservative cleanup confirmation and retained report all named the
actual journal-derived stage prefix; FileCat did not delete the remote stage
or close tracking. A separate FTP connection inspected the exact staged bytes
as a prefix of the owned source, deleted only that verified stage, and found
an empty remote manifest. The explicit Mark reviewed decision then closed the
journal without changing source bytes. Copy and Move both passed. Guest receipts
are `win-ftp-copy-crash-review-final.json` and
`win-ftp-move-crash-review-b1042cb6.json` under
`AppData\Local\FileCatValidation`. Other provider failure/cancel paths and
the non-atomic server-side final-check race remain open.

**23 October 2026, native FTP replacement risk within the viewport (FCUX-001):**
`win-ax-ftp-replace-warning.ps1` opened the isolated Git-built `9397baf4`
Windows app (SHA-256
`A70C24EF6C62873845F676CD09AB8821E62D0732A1769746E79C6B429D5482CE`)
against an owned loopback FTP server. The runner explicitly accepted the
unencrypted-connection warning, signed in, listed the existing target and
opened Copy from an exact local source. The prior build exposed Replace's
residual-risk warning in accessibility but placed it below a 720 × 540
window (warning top 645 versus frame bottom 612). The new build puts that
warning before the conflict options: at 96 DPI the complete warning occupied
`148,372,584,38` inside the `88,111,704,501` frame. It warned that another
client can change the target after the final check and suggested Keep both.
Changing to Keep both removed the warning; Cancel kept source and server
target hashes unchanged. The passing guest receipt is
`win-ftp-replace-warning-final2.json` under
`AppData\Local\FileCatValidation`. The same fixture then submitted a new Copy
with Ask each time, followed the compact strip's Review decision action, and
checked the separate per-item conflict dialog. Its complete warning occupied
`131,327,618,38` and Skip stayed inside the same narrow frame. Choosing Skip
kept both exact hashes and left no server staging item; the passing receipt is
`win-ftp-conflict-warning-probe5.json`. Higher
scales, spoken output and the intrinsic server-side final-check race remain
open.

**23 October 2026, native explicit-TLS replacement risk (FCUX-001):**
The same Windows VM app and owned loopback target passed the extended
`win-ax-ftp-replace-warning.ps1 -Protocol ftpes` journey. The fixture generated
a fresh self-signed certificate. FileCat displayed an unverified-certificate
decision, and the runner chose Connect once without changing system trust. At
96 DPI and a 720 × 540 logical window, the full final-check warning and Cancel
were inside the transfer review; changing to Keep both hid the warning. A
second Copy reached the per-item conflict, where the same warning and Skip
were also in bounds. Cancel and Skip left the exact source and target hashes
unchanged, with no extra remote object. The passing guest receipt is
`win-ftpes-warning.json` under `AppData\Local\FileCatValidation`; a fresh
`-Protocol ftp` run passed the same checks plus explicit plaintext consent in
`win-ftp-warning-regression.json`. The loopback FTP and FTPS fixture smoke test
passed locally and runs in CI on Windows, Windows ARM64, Ubuntu and macOS.
Higher scales, spoken review and the server-side final-check race remain
open.

**23 October 2026, native SFTP replacement risk (FCUX-001):**
`linux-sftp-replace-fixture.py` seeded one exact owned target in the Ubuntu VM
and recorded its SHA-256 and the SSH server's public-key fingerprints. The
Windows VM ran `win-ax-ftp-replace-warning.ps1 -Protocol sftp` on the isolated
Git-built `9397baf4` app. It saw FileCat's New server key decision, matched a
visible fingerprint against the independent Ubuntu receipt, and chose Connect
once. At 96 DPI in the narrow Copy review, the residual-risk warning was
visible for Replace, cleared for Keep both, and was visible again in the
per-item conflict review. Cancel and Skip kept the local source hash unchanged.
The separate Ubuntu verifier found the original target SHA-256 and exactly
`target.txt`, with no staged or published extra file. The passing guest
receipts are `win-sftp-warning-final.json` and
`SftpReplaceReview/verify-7ebd6aac8b6b423a80a16c0064500def.json` under
the respective VMs' `FileCatValidation` folders. Higher scales, spoken review,
other provider cancellation paths and the server-side final-check race remain
open.

**23 October 2026, exact-head remote replacement warning retest (FCUX-001):**
The isolated Windows VM fetched and built Git `8ad02057` after a locked restore
with 16 workers; the test executable's SHA-256 was
`34196070208071D2967B1917CCEA429FDF2CA27875733935EFC45600C544ACFB`.
The native warning runner now activates and verifies the owned FileCat window
before sending Enter to its Location field, preventing the foreground lock
from silently directing that key to another window. On this build, its owned
loopback FTP and explicit-TLS FTPS runs both passed the 720 × 540 logical
review at 96 DPI: Replace displayed the complete final-check risk, Keep both
removed it, and the per-item conflict displayed it again with in-bounds Skip.
Consent/certificate decisions were explicit and Cancel/Skip preserved both
exact source and target hashes. The receipts are
`win-ftp-warning-8ad02057-focus.json` and
`win-ftpes-warning-8ad02057-focus.json` under the guest validation folder.
The same runner passed SFTP against a new Ubuntu-owned fixture after matching
the shown host key to independently read server fingerprints. Its source
remained exact; the separate Ubuntu verifier found the original target SHA-256
`71a6255cb042a7e0a187b15bb133d2dfcc48d94e5179f24a1c0df05997b1fce1`
and only `target.txt`. The receipts are
`win-sftp-warning-8ad02057-focus.json` and
`SftpReplaceReview/verify-3cf9e6245b60473193491cf5838fcae4.json` in the
respective VMs. At that stage, higher-scale and actual screen-reader acceptance
remained open.
The same Git-built FTP app then passed the native 720 × 540 logical review at
150% (144 DPI) and 200% (192 DPI); both Copy and per-item conflict warnings
and their safe actions stayed within the frames. Keep both cleared the Copy
warning, and Skip preserved exact source and target bytes. The guest receipts
are `win-ftp-warning-150-8ad02057.json` and
`win-ftp-warning-200-8ad02057.json`; the VM was restored to its original 100%
scale. The same exact Git build then passed FTPS at 150% and 200% with fresh
self-signed test certificates and explicit Connect once, and SFTP at 150% and
200% with fresh Ubuntu-owned targets and independently matched host keys.
The complete Copy/per-item warning and safe actions stayed within each narrow
frame; Cancel/Skip kept all local sources byte-exact. Both Ubuntu SFTP
post-test verifiers found only `target.txt` with its original SHA-256
`71a6255cb042a7e0a187b15bb133d2dfcc48d94e5179f24a1c0df05997b1fce1`.
The guest receipts are `win-ftpes-warning-150-8ad02057.json`,
`win-ftpes-warning-200-8ad02057.json`,
`win-sftp-warning-150-8ad02057.json`,
`win-sftp-warning-200-8ad02057.json`, and the Ubuntu
`SftpReplaceReview/verify-5a7bc0251b9c4ab38cf2ee2f87df55c1.json` and
`SftpReplaceReview/verify-ddaa40b826ee4e2ab38615ca1d8d8a02.json`.
The Windows VM was again restored to 100%. Mac/Linux scale and spoken warning
checks remained open at that point.
The five-lane CI run for `9de8a57d` exposed one stale App test assertion that
looked for the warning as a `TextBlock` after it became a selectable `TextBox`.
`SyncReviewStateTests` now checks the read-only field and complete target
identity; all six focused Sync review/endpoint cases passed locally with 16
workers. The subsequent `314cd798` CI run passed the producer policy, Windows,
Ubuntu, macOS and Windows ARM64 lanes. The final-head CI gate remains open.

**23 October 2026, actual Windows speech for remote replacement (FCUX-001):**
An owned NVDA 2026.2 run exposed that the static remote replacement warning
was visible but silent when Replace was chosen: changing visibility alone did
not change its live-region name. The Copy review now explicitly raises its
complete warning on each newly risky choice; the per-item conflict raises the
same warning when its review opens. A headless regression checks the live name
events for SFTP, FTP and both FTPS profile types, plus the local control;
the nine focused cases passed with the direct xUnit runner. The native runner
now accepts a signed portable NVDA path, records only its owned speech log,
uses extended navigation keys, and requires the whole final-check risk and
Keep both choice in both reviews.
The isolated Win11 VM built exact Git head `0dc92d25` from a clean checkout
(FileCat.exe SHA-256
`643624A3A9263FFF1916D84C5912EC3AB794068273E276AD270FB2C6557C2508`).
Its native FTP, explicit-TLS FTPS and SFTP reviews all passed at 96 DPI: NVDA
spoke the complete warning on selecting Replace and again when the per-item
conflict opened. Keep both hid the warning, and Cancel/Skip preserved exact
local sources. FTP/FTPS kept their single targets byte-exact; SFTP matched an
independently recorded Ubuntu host key, and Ubuntu independently confirmed
only `target.txt` remained with SHA-256
`71a6255cb042a7e0a187b15bb133d2dfcc48d94e5179f24a1c0df05997b1fce1`.
The retained receipts are `win-ftp-warning-nvda-0dc92d25.json`,
`win-ftpes-warning-nvda-0dc92d25.json`, and
`win-sftp-warning-nvda-0dc92d25.json` in the Windows VM validation folder,
plus Ubuntu `SftpReplaceReview/verify-cb3e40929f004e94b678385ccaf90aa3.json`.
The `0dc92d25` CI run passed producer policy, Windows, Ubuntu, macOS and
Windows ARM64. The final-head CI gate and Mac VoiceOver warning check remain
open.

**23 October 2026, native Ubuntu speech and scaled remote review (FCUX-001):**
`linux-atspi-ftp-warning-speech.py` drives an isolated Git-built FileCat
(`62796e0c`, DLL SHA-256
`9dea1175626420d87868994ccc544376c9e55c2b83b0eb32f256d6e1042d6454`)
and an owned Orca log. For FTP it requires explicit plaintext consent; FTPS
requires Connect once for a newly generated self-signed certificate; SFTP
matches the new host key against independent SSH server public keys before
Connect once. For all three protocols, Orca spoke the complete final-check
risk and Keep both choice on selecting Replace and again in the per-item
conflict. Keep both hid the warning; Cancel and Skip kept the original source
and remote target hashes and a single `target.txt` manifest. The runner then
repeated each protocol at measured GNOME 100%, 150% and 200%, resizing FileCat
to 720 × 540 logical pixels and requiring the warning and safe action to
remain inside the frame in both reviews. All nine scale/protocol runs passed;
the VM was restored to its original 200%. Receipts are
`ubuntu-{ftp,ftpes,sftp}-warning-orca-scale{100,150,200}.json` under
`~/FileCatValidation` (the first FTP 200% receipt is
`ubuntu-ftp-warning-orca-scale200-r2.json`; the SFTP 200% receipt is
`ubuntu-sftp-warning-orca-scale200-r2.json`). The later Mac checks are recorded
below.

**23 October 2026, native Mac remote replacement speech (FCUX-001):**
The signed isolated macOS ARM64 build from exact Git head `cc31c682` (FileCat
DLL SHA-256 `5cb7d8eee2a82408ce25354eb94591837596844665b99c8216b07253e020d166`)
passed fresh local FTP and explicit-TLS FTPS Copy reviews with VoiceOver running
and Mac audio muted. The native runner required plaintext consent or a fresh
certificate's Connect once, focused the exact local source row, and checked
that the review destination was the owned remote endpoint. On Replace,
VoiceOver's last spoken phrase contained the complete final-check warning and
Keep both choice automatically. Cancel retained both source and remote target
SHA-256 hashes and a one-file remote manifest. The receipts are
`mac-remote-warning-ftp-cc31c682.json` and
`mac-remote-warning-ftpes-cc31c682-r2.json` under `~/FileCatValidation`.
`native-screen-3171B761-D328-4817-A0CE-F11472A7132B.png` shows the full
orange warning wrapped above the replacement selector and in-bounds Cancel
and Copy actions at the tested native Mac scale. Avalonia reports zero AX
geometry for some visible controls inside this scrolling dialog, so further
Mac pointer/AX geometry acceptance remains open. The pushed `03093eef` CI run
passed producer, Windows, Ubuntu, macOS and Windows ARM64 lanes; the final-head
gate is still open.

The same exact signed build then passed a separate Ask-each-time copy on both
FTP and FTPS. The native runner selected the complete remote destination,
submitted the new transfer, opened its background Review decision, and
required VoiceOver to speak the full final-check warning automatically when
the per-item conflict appeared. Skip preserved both exact source and remote
target hashes and the single `target.txt` manifest. Receipts are
`mac-remote-warning-ftp-conflict-cc31c682.json` and
`mac-remote-warning-ftpes-conflict-cc31c682.json` in the Mac validation folder.
The same exact signed build passed the SFTP Copy and per-item Ask reviews with
VoiceOver running and Mac audio muted. A disposable public-key-only loopback
SFTP server used fresh host and client keys; the native runner matched the
new-host-key prompt to an independent SHA-256 fingerprint before Connect once.
The Copy review targeted that exact server folder. VoiceOver spoke the full
final-check warning automatically after Replace and again for the per-item
conflict after Ask. Cancel and Skip preserved both source and target SHA-256
hashes and the server's single-file manifest. The final helper rerun passed
again, including cleanup of its disposable server. Its receipt is
`~/FileCatValidation/mac-remote-warning-sftp-final.json`; the captured
warning is `native-screen-93CE2B4D-72F9-4671-83B9-E183E67FCA89.png`.
The same signed build passed a Retina 2× Mac Copy review narrowed to 640 ×
480 points against a fresh FTP fixture. The complete orange warning wrapped
above the Replace selector, the review stayed scrollable, and Cancel and Copy
remained inside the native window. VoiceOver spoke the full warning; Cancel
and a later Skip retained both exact hashes and the one-target manifest. The
guest receipt is `mac-remote-warning-ftp-narrow.json` and the visually checked
capture is `native-screen-63E65E99-AD39-4166-857E-EFFF8E1E55D4.png`.
The same 640 × 480 journey passed with fresh public-key SFTP after waiting
for the native Copy action to become enabled. The complete remote folder
mapping wrapped below its editable Destination field; the warning and both
buttons stayed in view, VoiceOver spoke the warning, and Cancel/Skip kept
source and target bytes unchanged. Its receipt is
`mac-remote-warning-sftp-narrow-final.json`; visual capture
`native-screen-2D185C88-4472-4269-8E3D-1E128F35911B.png` was checked.
The final-head CI gate remained open at this stage. The `cc31c682` macOS
CI App lane exposed tests that expected the new deliberately delayed VoiceOver
announcement synchronously, plus a Compare status read before completion.
Commit `5ce0f8bf` now waits for the visible outcome; all ten affected cases
passed on both Windows and macOS with 16 test threads. Its
[five-lane CI run](https://github.com/benny-cz/FileCat/actions/runs/39949321528)
passed producer, Windows, Ubuntu, macOS and Windows ARM64; the final-head gate
will run again after this validation batch.

The Retina 2× Mac per-item conflict also passed at 640 × 480 points on fresh
FTP and SFTP endpoints. The orange warning and complete long SFTP destination
wrapped inside the scrollable body; Skip and Cancel operation stayed inside
the window in a two-row action layout. VoiceOver spoke the full warning before
Skip; both source and target hashes and the single target manifest remained
unchanged. The guest receipts are `mac-remote-warning-ftp-both-narrow.json`
and `mac-remote-warning-sftp-both-narrow-keyboard.json`, with visually checked
captures `native-screen-A98A1BFC-27C7-477A-9F26-DE8356E2B799.png` and
`native-screen-73BD3736-9C12-453C-98ED-5B150E0DDCD3.png`. The narrow SFTP
run used Esc then Tab to return from the right Location field to the exact
local source panel. Earlier pointer runs overlapped with another user's Mac
mouse input, so they were not evidence of an app focus defect. Two fresh
SFTP pointer-only probes without competing input opened the exact Copy review
on the first attempt, with source and target hashes and the one-target
manifest unchanged. Receipts are `mac-remote-warning-sftp-pointer-probe.json`
and `mac-remote-warning-sftp-pointer-probe-2.json`.

The same signed build then passed fresh FTP, explicit-TLS FTPS and public-key
SFTP Copy/per-item Ask reviews in the Mac's supported 1920 × 1200 logical and
physical (1×) display mode. Each review was narrowed to 640 × 480 points;
the full orange final-check warning and safe decision buttons remained visible,
VoiceOver spoke both warnings with output muted, and Cancel/Skip retained both
exact source and target hashes and the one-target manifest. The test wrapper
restored the original 1440 × 900 logical/2880 × 1800 pixel Retina mode after
every run. Receipts are `mac-remote-warning-{ftp,ftpes,sftp}-ax-narrow-1x.json`
under `~/FileCatValidation`; the SFTP Copy and conflict captures are
`native-screen-5AD34C7D-8042-4B80-9580-91B8DD13D947.png` and
`native-screen-578C1A29-276A-4CA4-A3E5-52AB817C0C33.png`. The 1× SFTP
source tab activated and opened the exact Copy review through its native AX
action. A separate pointer probe passed once but repeated fast pointer runs
after Connect once left the right panel active despite clicks on the left tab;
the source and target bytes remained unchanged. The native pointer timing
after this transition remains open; this is not counted as a completed pointer
acceptance route at 1×.

**23 October 2026, narrow native Undo review and report (FCUX-018):**
`linux-atspi-undo-narrow.py` passed a real Move and approved Undo in the
logged-in Ubuntu VM on the isolated Git-built `8a8769fd` app (DLL SHA-256
`9705a8e68966a3f8c84cffd31f07c7feaf8709ed572ede7cd420ada74b836125`).
At GNOME 200% it resized FileCat to 1440 × 1080 physical pixels (720 × 540
app units). The complete source and target paths occupied a wrapped 229-pixel
action label; both Cancel and approval remained within the frame. Undo
restored the exact original bytes and removed the moved target. The retained
report accepted 1200 × 920 physical pixels (600 × 460 app units), kept Copy
all in bounds, and copied 664 UTF-8 bytes containing its Undo heading and the
complete restored source path. The guest receipt is
`~/FileCatValidation/ubuntu-undo-narrow-copy-final.json`.
The native Win11 `win-ax-undo-mixed-report.ps1` then passed at 150% (144 DPI)
and 200% (192 DPI) with 42 complete outcomes at each scale. At 150%, the
review action wrapped to 140 physical pixels within a 1058 × 754 frame;
at 200%, it wrapped to 187 physical pixels within a 1414 × 1009 frame.
Both decisions remained in bounds at each scale. The 600 × 460 app-unit
retained report kept Copy all visible and exported the full result at both
scales, including the failed and safely kept paths. Guest receipts are
`win-undo-mixed-scale-150.json` and `win-undo-mixed-scale-200.json` under
`AppData\Local\FileCatValidation`. The VM display was restored to 100%,
verified as 96 DPI from a fresh process.
The signed Mac Git build of `8c833c35` (app DLL SHA-256
`1bfec730b1544d948a8fe2345ffc9c321b07af0176de9b9ca9427e4cd78a06ed`)
also passed `mac-ax-undo-narrow.py` on an unlocked Retina desktop. A real
Move opened Undo at 720 × 540 points. The AX row named both complete full
paths and wrapped to 75.5 points; Cancel and approval were inside the frame.
Native captures confirmed the full highlighted path pair and both buttons
visible. Cancel preserved the moved file and exact bytes; reopening and
approving Undo restored the original bytes. The retained report accepted
600 × 460 points, kept Copy all in bounds and copied 675 bytes including the
complete restored path. The guest receipt is
`~/FileCatValidation/mac-undo-narrow-cancel-8c833c35.json`.
On Ubuntu, the same 200% `linux-atspi-undo-narrow.py` runner passed again
with `--orca-review-speech` on Git build `8a8769fd`. After a controlled
focus transition, Orca's own speech log said the complete Move back row,
including both full source and target paths. The retained report's AT-SPI
text interface and Copy all both contained the complete restored path.
The passing guest receipt is
`~/FileCatValidation/ubuntu-undo-orca-review-final.json`. On focus, Orca
said the retained report's name and read-only status, but did not speak its
restored-path line. The line navigator above resolves that gap.
The signed portable NVDA 2026.2 build in the Win11 VM also passed the
expanded `win-ax-undo-mixed-report.ps1 -NvdaPath` on Git-built `9397baf4`.
After moving focus from Cancel to the inverse row, NVDA's own speech log
said the complete source and target paths of `item-41.txt`. The same run
retained and copied all 42 outcomes, with one failed and one safely kept
path. The guest receipt is `win-undo-nvda-review.json` under
`AppData\Local\FileCatValidation`.
The expanded `win-ax-undo-mixed-report.ps1` also passed on the isolated
Git-built `b1042cb6` executable. At 96 DPI it requested a 720 × 540 FileCat
window and found the complete inverse path wrapped to a 94-pixel text block;
both Cancel and Run these inverse actions stayed within the window. The
retained Undo report then accepted a 600 × 460 window, kept Copy all visible,
and exported all 42 mixed full-path outcomes with 40 restored, one failed and
one kept. The guest receipt is `win-undo-mixed-narrow.json` under
`AppData\Local\FileCatValidation`. With the owner-approved VoiceOver
AppleScript setting and SSH helper control, the Mac output was muted and the
same signed build passed an asserted actual-speech rerun. VoiceOver's last
phrase contained both complete source and target paths in the narrow Undo
review and again after focusing the retained report; Cancel and approved Undo
retained their exact byte checks and Copy all remained complete. The passing
receipt is `~/FileCatValidation/mac-undo-voiceover-final.json`. Spoken retained-report line navigation on Ubuntu, Windows and Mac is
verified above on exact committed source; final-head CI remains open.

**23 October 2026, native active Undo close and restart (FCUX-018):**
`win-ax-undo-active-close.ps1` passed both interactive Win11 VM routes on the
isolated Git-built `b1042cb6` FileCat executable (SHA-256
`E54A3D1EDC4BC8375EFE26DC818028D6B572BD8E3BA211F416E6BAA443D87B97`).
In the final Keep/Cancel run, closing during a 5,000-file inverse showed the
active-work choice; Keep FileCat open preserved its job owner, and Cancel
produced 2,040 restored plus 2,960 not-attempted outcomes. Copy all from the
retained UI report contained all 5,000 full-path lines, including paths beyond
row 30. Every file existed in exactly one fixture folder with its original
bytes. In the separate Stop-and-exit run, the close choice stopped after
2,134 restorations and left 2,866 unattempted. FileCat exited only after the
job ended; no open Undo journal remained. Reopening that exact profile showed
the same byte-exact split of all 5,000 files. The receipts are
`win-undo-keep-cancel-final-5000.json` and
`win-undo-stop-exit-restart-5000.json` under the guest's
`AppData\\Local\\FileCatValidation` directory. Scaled and spoken review and
result acceptance remain open.

**23 October 2026, native mixed Undo report (FCUX-018):**
`win-ax-undo-mixed-report.ps1` ran against the isolated Win11 Git build of
`b1042cb6` (`FileCat.exe` SHA-256
`E54A3D1EDC4BC8375EFE26DC818028D6B572BD8E3BA211F416E6BAA443D87B97`).
FileCat moved 42 files with verified original bytes. The runner then held
`item-35.txt` exclusively and changed `item-36.txt`, approved the complete
inverse review, and verified 40 exact restorations, one locked-file failure
and one guarded keep with unchanged modified bytes. The retained Undo report's
native Copy all action returned all 42 full-path outcome lines, placed the
failure and keep ahead of successes and included twelve complete path lines
after row 30. The passing receipt remains in the guest's
`AppData\\Local\\FileCatValidation\\win-undo-mixed-b1042cb6-final.json`;
the fixture remains for inspection. The subsequent active-close and
cancellation routes are recorded above.

**23 October 2026, final Windows validation gate:** the complete local
Release App suite passed 2,452, skipped 63 platform-specific cases and failed
none; the complete local Release Core suite passed 4,437, skipped 151 and failed
none. An earlier three-core hosted Windows runner timed out four Core
filesystem/job tests while its fixed assembly setting admitted sixteen
parallel collections. The Windows CI Core invocation now caps xUnit collection
threads at the runner's actual processor count, up to sixteen. A transfer
progress test also waits for its canceled job to relinquish the journal before
removing its fixture, and retains the original timeout diagnostic if the job
does not stop. On `93483e33`, the hosted Windows CI lane measured four
processors, ran four Core collection threads and passed 4,435 Core cases with
153 platform skips and zero failures in 5 minutes 56 seconds. Its other four
test inventories also passed, and the run's producer, Ubuntu, macOS and
Windows ARM64 lanes were green. The final-head gate is still required.

**23 October 2026, complete bulk-rename mapping navigation (FCUX-003 and
FCUX-038):** macOS exposed no children for the virtualized rename preview,
leaving only the selected full-path detail available to Accessibility. The
review now places Previous and Next actions above the long preview, keeps the
pair together when it wraps, shows the current position among filtered
results, gives each action the next full-path mapping as accessible help, and
announces the changing position and full path through a polite live region.
The read-only detail keeps a stable label. The list stays
virtualized for large selections. Headless review tests cover both ends,
filtered and empty results, full-path status/help and 3,001 rows; the minimum
window viewport tests cover 14-, 21- and 28-point text without horizontal
overflow. The affected Windows App tests passed 56, skipped one Unix-only
case, failed none; the final focused viewport/review run passed 35/35. An
ad-hoc signed Mac Git build at `e7fdc1ae` (`FileCat.dll` SHA-256
`b78d65733f4d87e495a913696c91ba595d2cf27633df472fc1cc3bec55511ed5`)
passed a 760 × 600 native AX journey: Previous/Next exposed both exact old
and new full paths and returned to the first, then editor approval produced
the exact expected files and bytes. The approved capture shows the two
buttons adjacent and visible above both preview rows. macOS still reports
zero AX bounds for controls inside the visibly rendered modal body and no
children for the virtualized list; VoiceOver's own spoken journey remains
open, while the independent AX button-and-detail route now permits complete
mapping inspection.
The later signed Mac candidate with `FileCat.dll` SHA-256
`fce670447e48fd5b14d73bb671fb15d483358fa26eca2bdb855f890cf91b38d1`
kept Previous/Next, the complete selected paths and the position announcement
in a fixed review section below the scrolling form. This makes the reviewed
mapping visible in a 760 × 600 window even when the form and preview scroll.
`mac-ax-rename-editor.py` then passed the native hidden-mark, external-editor
and exact-byte journey with Mac audio muted: VoiceOver actually spoke both
complete source-to-target paths at the first mapping, Next and Previous.
The passing receipt is `mac-rename-pinned-candidate-2.json` in the isolated
Mac `FileCatValidation` folder. The Windows headless viewport and review suite
passed 37/37 cases, including 14-, 21- and 28-point text at the minimum
window size. The form body still exposes zero Mac AX bounds, and its
virtualized preview still has no AX children; those broader accessibility
routes remain open.
The signed revised Mac build at `efb78acd` (`FileCat.dll` SHA-256
`d485cd80e20595a19552836f43ab153240ecde718bc8a766ff0e60dad98fc08d`)
passed a 760 × 600 native rename journey in both supported display modes:
1920 × 1200 logical/pixel 1× and 1440 × 900 logical/2880 × 1800 pixel Retina
2×. AX Previous/Next inspected both complete mappings and returned to the
first. The external editor produced the exact two renamed files and bytes,
removed its private list, and left the target empty. Captures show the editor
action, paired mapping buttons, both rows and decisions visible at each
scale. The display helper verified restoration to the original Retina mode.
The same signed build passed focused Return on Next and Space on Previous at
both 1× and Retina 2×. Each key changed the complete source-to-destination
detail in the expected direction, and both editor runs still produced exact
files and bytes. The 1× run again restored Retina mode.
A fresh Win11 VM checkout of pushed `2212b4de` (`FileCat.exe` SHA-256
`C965A653E57B4F1C50B97530388FB7F6E2AE87ED4427774D70FA8057B45BD3C1`)
passed the `rename-keyboard-native` journey. WinAppDriver focused Next and
sent Return, then focused Previous and sent Space. Each key exposed the exact
complete source and destination path in the read-only detail; the final
Rename produced the two expected files with their original bytes and left
the fixture target empty. This closes physical keyboard mapping navigation
on Windows alongside the Mac and Ubuntu runs; spoken review remains open.
The complete Windows App Release suite passed 2,452 tests, skipped 63
platform-specific cases and failed none on this implementation slice.
An actual NVDA 2026.2 speech run on the Win11 VM then found that its live
region spoke only the position, although the automation name contained the
complete mapping. The region now puts the full, safely formatted source and
destination paths in its live text while truncating only its visible 160-unit
status line; the selectable full-path detail remains below it. The 37 focused
review, viewport and live-region tests pass. A fresh Git VM build of
`094f04b7` (`FileCat.dll` SHA-256
`31BD0E7E642D5F0F05B4B310E98CF0C576B7A6B3CC392FE126F078C55C3D74C7`)
passed 34/34 NVDA speech steps. The focused Next and Previous buttons exposed
both exact full-path pairs in NVDA's own speech log; Cancel preserved all
three fixture source files byte-exactly and left the target empty. NVDA
intercepts Insert as its own modifier, so the test marked the two exact native
selection items before testing the spoken review; keyboard selection routes
remain part of FCUX-010 acceptance.
The same live-text fix passed actual Orca speech on Ubuntu. A fresh Git build
of `aca48067` (`FileCat.dll` SHA-256
`2e2d15854f8785d62f8ec41936766369a5ed347791112e8a5bee9bce7c068385`)
ran in the logged-in GNOME session at its original 200% display scale. The
extended runner used physical Shift+Down to mark exactly `a.txt` and `b.txt`,
and Orca announced the new marked row. Native accessibility actions then
focused and invoked Next and Previous; Orca's own speech log contained each
position and the exact full source and destination path pair. All 39 speech
steps passed; Cancel left the three source hashes unchanged and the target
empty. VMware's interactive guest launch was required for Orca to receive
the desktop's accessibility events.

**23 October 2026, complete synchronization endpoints at scale (FCUX-005):**
The Ubuntu VM's 200% native capture exposed clipped Source and Target fields
despite their complete AT-SPI values. The read-only, selectable endpoint
fields now wrap long paths, with bounded vertical scrolling for extreme
lengths; direction, mode and the show-all choice reflow on narrow windows.
The 36 focused sync/comparison tests passed. A Git build of pushed `015eac1c`
(`FileCat.dll` SHA-256
`0154fbb658d7dbd79618ab20be82cdc27495532a599a41c8362af7c9b9c73ef7`)
passed the native Ubuntu review: two different long paths ending in `Project`
remained complete in selectable Source/Target fields, physical Down reversed
their roles, and Mirror named the new full target path. At 200% display scale,
the review reflowed at 1280 × 1000 physical pixels: both path fields were 127
pixels tall and fully inside the 1280-pixel-wide dialog, the mode moved below
direction, and Cancel remained visible. Captures of wide and narrow layouts
show the complete final folder segment on both endpoints. Cancel preserved
both exact trees and file hashes. The native runner and JSON receipt stay in
the tracked test harness and isolated VM/ignored host output respectively;
Windows and Mac native checks follow below.
A fresh Win11 VM Git build of `1840791f` (`FileCat.dll` SHA-256
`D396CF45326F18AB15E64BD086663543ECEF012463307FBE2BA8D6725D003138`)
also passed the native endpoint journey. UI Automation read exact complete
Source and Target values and accessible names for two long, same-basename
folders. Physical Down reversed the roles, the Mirror warning named the new
full target, and the 96-DPI dialog resized to 640 physical pixels with both
path fields 62 pixels tall, direction and mode on separate rows, and Cancel
inside its bounds. The isolated VM capture shows both `Project` suffixes,
distinct parent paths and visible decisions. The native JSON receipt confirms
Cancel preserved both exact trees and file hashes.
A signed, isolated Mac build of pushed `7072b2e2` (`FileCat.dll` SHA-256
`b9df6d632979e835cb300e3d1f4a10364a431cb48f1fb5c5bf6a10752bfd4ba2`)
passed the native AX endpoint runner on a Retina 2× display. Both long paths
ending in `Project` were exposed as complete selectable values and accessible
names. Pointer focus followed by physical Down/Return reversed their roles;
Mirror named the complete new target. At 640 × 600 logical points, the two
wrapped fields, warning, choices and Cancel were visible. The capture was
visually inspected at 2880 × 1800 pixels. Cancel preserved both exact trees
and file hashes.
The same Git-built Windows binary then passed the revised DPI-aware native
runner at 150% (144 DPI, 960 × 900 physical) and 200% (192 DPI, 1280 × 1200
physical), each representing a 640 × 600 logical review. Its UI Automation
receipt checked both complete selectable endpoint values and names, physical
direction reversal, the complete reversed Mirror target, wrapping field
heights, in-bounds choices, and unchanged fixture trees and hashes on Cancel.
Isolated VM captures at both scales show the two distinct `Project` paths,
the fully wrapped warning and visible Cancel/Synchronize actions. The runner
passed again at 100% after Display Settings was restored to 96 DPI.
The first actual NVDA run spoke both complete Source and Target paths before
and after direction reversal, but selecting Mirror spoke only the mode. The
warning now uses a polite accessibility live region. A fresh Git VM build of
`559a67a2` (`FileCat.dll` SHA-256
`F5AD01D373B17F65CA2640BF50C9FE17FB195BBE5A0DEE3865EB7981014B6DC0`)
passed all five NVDA speech-log checks: both complete paths in each direction
and the complete reversed target in the Mirror warning. The retained log
contains the actual warning utterance; Cancel left both fixture trees and
file hashes exact. The Ubuntu runner then used Orca's own speech log at the
VM's 200% display scale on a Git build of the same `559a67a2` commit
(`FileCat.dll` SHA-256
`b4d91fac810d43c6b14a58f7c86fb78b5ae819ecbe176c0ea1337bacfe30c8d8`).
It passed all five corresponding spoken endpoint and Mirror-target steps;
Cancel preserved both exact trees and hashes. VoiceOver and final-head safety
gates remain open.
The later signed Mac candidate (`FileCat.dll` SHA-256
`fdd16ec8d2ea3cb52c6bd6b7f34cc5eb7f04820c4e3225bdbb22600ca2fbfd47`)
passed `mac-ax-sync-endpoints.py` with VoiceOver running and Mac output muted.
Its own speech phrases contained both complete endpoint paths before and after
direction reversal, then the complete reversed Mirror target when Tab reached
the highlighted, selectable warning field. The native 640 × 600 review and
Cancel preserved both exact trees and hashes; the receipt is
`mac-sync-voiceover-tab-warning.json` in the Mac `FileCatValidation` folder.
VoiceOver did not reliably announce the polite warning automatically while
its cursor stayed on the mode popup, so that automatic announcement remains
an open refinement. The complete Windows Git solution run for the preceding
bulk-rename commit `e53784c8` passed 4,437 Core, 2,397 Remote and 2,456 App
cases, with platform-specific skips and no failures; the Sync warning change
passed its three focused App cases afterward.
The subsequent signed Mac candidate (`FileCat.dll` SHA-256
`e75b6350608cbccaf2bbf7f345de4fd33a7c61ef0feb7df144d2251a2ff039d7`)
also passed the full journey with the Mac output muted. A bounded repeat of the
live warning after the native selector closes made VoiceOver announce the full
reversed Mirror target automatically while focus remained on the selector;
the runner recorded `warningSpeechRoute: automatic` and all five spoken path
checks passed. Its native review reached 640 × 600 points and Cancel preserved
both exact trees and file hashes. The receipt is
`mac-sync-voiceover-auto-warning-strict.json` under `~/FileCatValidation`.
The repeat timer retires when the warning changes, the review loses focus or
closes, and it does not add a second notice on Windows or Linux. Three focused
App `SyncEndpointTests` passed after that change. The preceding paragraph
records the first candidate's behavior; this later run closes its automatic
announcement gap.
The exact `559a67a2` Windows VM checkout then restored the locked test graph
and passed eight focused Core `SyncTests` plus six App
`DirectoryDiffTests`/`SyncEndpointTests`, all with zero failures or skips.
Those cases cover nested-folder overlap refusal, a target changed after
comparison, letter-case collisions, Recycle Bin handling and the full-path
review. The audit-wide final-head CI gate remains open.

**23 October 2026, Ubuntu external-editor acceptance (FCUX-003):** A fresh
shallow clone of pushed `ac19c68f` was published with the pinned .NET SDK in
the logged-in Ubuntu VM (`FileCat.dll` SHA-256
`1c7fc359789e518a41046000c23fbf077f269e8c3b5835fa973b0df747d2a500`).
The tracked AT-SPI runner created a private two-file fixture, used physical
Insert input to mark both exact source rows and Ctrl+M to open Bulk rename,
changed the name mask, and invoked the configured, fixture-bound external
editor. Its saved names appeared in the review, the private name list was
removed, and the modal-owned Rename approval produced exactly
`editor_a.txt` and `editor_b.txt`, each with its original bytes and an empty
target. The normal-size JSON receipt is retained in the isolated guest and
ignored host test output. A separate narrow run was **not** counted as
narrow-layout evidence: although its rename succeeded, its measured window
remained 1280 × 840 after both `wmctrl` and `xdotool` requests for 720 × 540.
The Ubuntu desktop was at 200% scale, so that request had reached the
640 × 420 minimum in app units. The runner records the native display scale,
measures the window's rendering scale and requires settled geometry in app
units.
A fresh Git build at `447d8a61` (`FileCat.dll` SHA-256
`3633a2366debd5ad72d94f05d889d7fc11fecf3c2cd89163ece8c9342f66ed38`)
passed the whole editor journey at a measured 1440 × 1080 physical window,
720 × 540 device-independent units at 200%: both exact source files were
renamed with original bytes, the target stayed empty, and the private edited
name list was removed. A second native run on the same Git build invoked the
new Previous and Next buttons through AT-SPI at that narrow size, verified
both complete source-to-destination path mappings and returned to the first,
then passed the editor and exact-filesystem checks. The read-only mapping
detail retains a stable accessible label for focused review; the changing
position is the sole live announcement and includes the full mapping. Other
FCUX-003 gates remain in
[UX_UI_REMAINING.md](UX_UI_REMAINING.md).
The same narrow AT-SPI and editor journey also passed at GNOME 100% and 150%,
with exact path mappings, original file bytes, an empty target and removed
private editor list. At 150%, GNOME reports 1.5× while Xwayland gives this
window a measured 2× rendering scale (1280 × 840 physical minimum for the
app's 640 × 420 unit limit); the runner now derives app units from the native
window manager minimum-size hints and confirmed 720 × 540 app units. The VM
display was restored to its original 200% afterward.
The exact revised Ubuntu build also passed a physical-keyboard rerun at 200%
and 720 × 540 app units: Enter on focused Next exposed the second complete
mapping, and Space on focused Previous restored the first. The editor and
exact filesystem checks still passed.
VMware captures at 200% and a measured 720 × 540 app-unit window showed the
editor action, paired mapping controls, both preview rows and Cancel/Rename.
Focusing the mapping detail scrolled it into view, but its fixed 75-unit
height clipped the destination line and added an inner scrollbar. The detail
now grows from 75 up to 160 units for wrapping, with a cap for exceptional
paths; focused headless review and minimum viewport cases pass 12/12. A fresh
Ubuntu Git build at `efb78acd` (`FileCat.dll` SHA-256
`e26aa40bfa4aa5d963398b04ad697d457906bc56df25153b1a9099b661609e5e`)
passed at 200% and 720 × 540 app units. Its capture shows both complete paths
without the inner scrollbar for this fixture, and the subsequent editor,
temporary-file removal, exact renamed bytes and empty target all passed.
An additional fresh fixture loaded editor names, then changed the name mask
in the same review. The editor-derived status disappeared and approval used
the new rule mapping, yielding exactly `rule_1.txt` and `rule_2.txt` with
their original bytes. This verifies the ordinary post-editor change; a
concurrent change while the editor is pending remains a separate native gate.

**22 October 2026, scaled-display sweep on Ubuntu at 200% and the key bar's
modifier layers (UXBUG-018):** A scripted sweep drove Git-built FileCat on the
Ubuntu VM at 200% with physical uinput keys (the helper now also sends F8,
Ctrl+Q, Alt+F7, Shift+F10, F1, F9, F10 and typed lowercase words) and captured
each window. The main window, Create folder, Copy with a long name, the Delete
confirmation, command search, the panel resize strip, the viewer, Quick View,
the Operations drawer, About and Settings lay out without clipping or cropped
icons; the viewer's toolbar wraps to a second row at its 980-unit width. Popup
menus (F10, Shift+F10) and the Find window were not captured by this route.
The key bar was wrong, though: X11 sends a key with the modifiers held before
it, so on Ubuntu holding Ctrl showed the normal keys and releasing it left the
Ctrl layer (Name, Ext, Time…) up, also after Ctrl+Shift+P opened command
search. A modifier key's press now adds its own flag and its release removes
it (`e7836d62`); a new `KeyBarTests` case replays Ctrl and Shift with both
kinds of reported state and failed before the change. With a physical Ctrl
hold, Git-built `e7836d62` showed the Ctrl layer only while Ctrl was held on
Ubuntu (`FileCat.dll`
`2e757bb54ab50efbd0a095b1bb3be7d63271a87813640c27ca6427754abdf320`), and the
Win11 VM behaved the same before and after the change (`80598aeb`,
`3EBE38A7ADA10E0D873345DA7D9E8A6BEF5843950931C9C0896FE3F34D559217`), as
Windows reports the state after each key. Still open: after Shift+F10 opened
the context menu, and after Alt+F7, the Shift or Alt layer stayed up once the
popup closed, because the modifier's release reached the popup or the new
window instead of the main window; it lasts until the next modifier key.

The follow-up key-bar correction reconciles the held layer from every ordinary
key's modifier flags and resets it when a panel or toolbar menu closes or the
main window regains focus. It avoids rebuilding all twelve key buttons when
the modifier state has not changed. A headless regression presses Ctrl without
delivering its release, verifies the next ordinary key clears the Ctrl layer,
then opens and closes a sort popup while a Shift release is missing and checks
that the normal layer returns. All four focused `KeyBarTests` pass on Windows.
An isolated Ubuntu VM build of `bdb618d9` had zero warnings/errors and
`FileCat.dll` SHA-256
`4d5d52b47ac3c79a332bf2b354d2ddb3a3727051207c8655b013380a6ad1bf37`.
The tracked AT-SPI runner then used physical uinput at 200% display scale:
Shift+F10 opened the real item context menu, Escape returned all twelve
key-bar labels to their original values, and the source SHA-256 and empty
target stayed exact. GNOME normally owns Alt+F7 for Move window; that route
also returned the normal key bar after Escape. A second run temporarily
released only GNOME's `begin-move` binding, required FileCat's actual Find
files window to open from Alt+F7, closed it with Escape, and observed all
twelve original labels again. Its shell trap restored the original
`['<Alt>F7']` binding. Both machine-readable receipts are retained in the
guest validation folder and ignored host test output. UXBUG-018's native
keyboard behavior now passes; the remaining Ubuntu popup/Find work is visual
capture at the scaled layout.
The complete Windows Release App suite on the key-bar correction passed
2,448 tests, skipped 63 platform-specific cases and failed none with
`DOTNET_PROCESSOR_COUNT=16`.

**22 October 2026, native icons cropped at display scaling above 100%
(UXBUG-017; FCUX-058 Places and the scaled-display gates):** The owner saw
only part of each panel header icon on the Ubuntu VM at 200%. Native icons are
made at the display's pixels, 32 for a 16-unit slot at 200%, and were given a
resolution that made them 16 units. Avalonia 12.1 measures a bitmap in units
but draws the source rectangle it is handed in pixels, so the place bar, file
rows, menus and choice lists drew only each icon's top-left 16 pixels,
enlarged: a quarter of the icon at 200% and two-thirds at 150%, from the
Freedesktop, macOS and Windows icon sources alike, and the same for the Shell's
Quick View thumbnails. The pixels now stay at 96 DPI inside a `ScaledBitmap`
that reports the size in units and draws the matching pixels (`80598aeb`).
The new `FileCat.Rendering.Tests` project draws with Skia, as FileCat does on
every desktop, and reads the pixels back: an icon of four colored quadrants
framed on two edges must land pixel for pixel in the place bar's 16-unit
image, a file row's drawn 16-unit rectangle and Quick View's never-enlarged
picture at 100%, 125%, 150%, 175%, 200%, 250% and 300% (21 cases). The old
construction failed all six scalings above 100% (768 of 1,024 pixels wrong at
200%). CI runs the project on Windows x64 and ARM64, Ubuntu and macOS; the 121
related App cases (icon caches, sources, place bar, Quick View, Shell
pictures) pass. Git-built `80598aeb` and `eebb8832` were captured in the same
fixture on each desktop: Ubuntu GNOME at 200% (`FileCat.dll`
`1aeb4216a1b5c137f78ca9c8dce779f17dda4a6d76a067c6cd60b2d0d4bfa15d`), the
Mac's built-in Retina display at 2× (`7409bf7706e9dbe5ed477a0a7b86781fdc485ad256180d5cfd7c20e9e154aa20`),
and the Win11 VM at 150% and 200% (`9AADFFB2272535473374DF1D30D559348A0E4AB3345D3296FBE10E7788A80929`),
set through the display configuration calls Settings uses, captured with
PrintWindow and restored to 100% afterwards. Before, the drive, Home,
Desktop, Documents, server, computer, network and file-type icons showed
their enlarged top-left part (Windows' D:, Y: and Z: drives were grey
slivers); after, every icon is complete on all three desktops. Quick View
of a 512-pixel PNG of four colored quadrants in a magenta frame, which Windows
draws as a Shell thumbnail, showed only the red top-left quadrant and the top
and left frame at 200% before, and the whole picture after. A theme's tinted
icon keeps its scaling, but no built-in theme tints icons, so that path has no
native check.

**22 October 2026, keyboard panel resizing heard as a slider (FCUX-031):**
The speech runners now open command search, type "splitter", press Enter,
then Right and Escape. While the boundary was a thumb whose name carried the
share, NVDA and Orca read the whole resize strip after every arrow press and
never the boundary itself. Each boundary is now a read-only slider whose value
is the first side's share in percent and whose name gives its orientation and
panels; the strip is no longer a live region and its tooltip keeps the
percentage (`daec7d64`). Avalonia's AT-SPI bridge raises no value changes, so
FileCat sends the focused slider's `accessible-value` change to Orca itself
(`8c15f80d`). Git-built `eebb8832` passed every runner step (NVDA 30/30, Orca
31/31, macOS 30/30). NVDA 2026.2 heard only "51" for Right and "50" before
the restored row for Escape (`FileCat.dll`
`EF50E8C4DA18B1C59D075072530E5EA2D2BDAE98166A627687488B9D268476F0`). Orca
heard "Vertical panel boundary, first side's share of panels 1 and 2",
"slider", "50 percent." and the boundary's help, only "51" for Right, and
"Panel boundary restored." after Escape
(`2d912c08975f9d07de153f7a982e792fab0f475ca613ba85e74dcaa3bb6db58c`); it reads
that help twice because Avalonia gives AT-SPI the same text as description and
help. The macOS AX journey observed focus on the `AXSlider` of that name, one
`AXValueChanged` with value 51, and focus back on the row
(`80f0b4050b86bafff67db2d5824e521e76dc66cb97551542e312f6af74516d00`). The
complete Windows Release App suite on `eebb8832` passed 2,509 tests with 71
platform skips and no failures. The WinAppDriver `panel-resize-native` runner
now finds the slider and reads its value but was not rerun.

**22 October 2026, live status text that could never be heard (FCUX-007,
FCUX-040, FCUX-041, UXBUG-015):** A screen reader speaks a live region's
accessible name, and twelve live texts had a fixed one that never changed
with their text: the viewer's match count ("Find match count") and zoom
percentage, the comparison match status, the transfer form's destination and
options errors and its remote warning, bulk rename's three counter errors
and summary, and the About and text file exchange statuses. None of them
could be heard, whatever the bridge. Each keeps its identifier as an
AutomationId for tests and speaks its text (`eed454ba`), and the conflict
review's remote warning now reads as its text too. `LiveRegionNamingTests`
scans the App sources and fails on any control that can be live and has a
fixed accessible name; it flags the previous viewer code. Once heard, the
viewer's count spoke every step of a search: Orca heard "Press Next or
Previous to count" after the first typed letter, then "Searching…", "At
least 1 match · counting…" and "1 match", and the macOS AX journey saw the
same three announcements. Only outcomes are live now (a count, an
unavailable or stopped count, or no match), and a new viewer test hears
exactly "1 match". The complete Windows Release App suite on `35763947` passed 2,508 tests with 71 platform skips and no failures using 16 workers. The speech runners now also open the viewer on
the focused file with F3 and its search with F7, type a query, require "1
match" alone, and leave with Escape twice. Git-built `35763947` passed all
of it on the three desktops: Orca 50.2 heard only the typed letters and "1
match" (Ubuntu, `FileCat.dll` `5e8740ebf834bb455a67d1e621ed9f3c65b6b0020ed109a6a8da2b0579039929`), as did NVDA 2026.2
(`bd3aae95255cef05a083e8a8de2929a31944bbcb3adb0e0221e34f7e790aeba2`), and the
macOS AX journey observed one `AXAnnouncementRequested`, "1 match", at
priority 50 (`e4f7e6d282e52bd83dff69be23ceab53d420853426f3852dde4969384e669e07`).
The other renamed regions were not run natively. Orca also read the viewer's
text document as "heading" and never said its name: Avalonia 12.1's AT-SPI
role table is one off from the specification between roles 81 and 114, so its
Document (83) is the specification's HEADING, and a link or tree item would be
misread the same way (FileCat uses neither). On Linux the text, hex and binary
comparison views are read-only entries now (`68fdea8e`), the nearest role the
table gets right, and Git-built Orca heard "Text viewer, read only entry"
(`FileCat.dll`
`6a13a93b67444a5853b7e0982dd393943214edee05304499aba9edd7effea9e8`); Windows
and macOS keep the document role.
The runners then opened Copy with F5, emptied its destination and heard
"Destination: Enter a destination folder." exactly once on all three desktops
before Escape cancelled it (Git-built `68fdea8e`: Windows
`4098596b21b577cbe9c58235bc9927babbaf7cab55ba1eb6460363e873d9f004`, macOS
`2137a18a1c7c2985677e09401b6b34c483b71bd3e0d2628b42899c2e22e0113c`).

**22 October 2026, spoken prompt validation and macOS typing after a command
(FCUX-054's shared prompt pattern, FCUX-011 field-associated errors):** The
speech runners now also open Quick filter with Ctrl+S or Command+S, type the
unclosed mask `/a` and press Enter. Every desktop was silent. The shared
prompt's validation message had no live setting and was not tied to its
field, and Enter on a rejected value was ignored without a word. The message
is now an assertive live region and the field's help text while shown, and
Enter on a rejected value keeps the dialog and speaks the reason again, also
before anything was typed (`6afb92a0`). Such a message gets its text while
still hidden and is shown in the same step, so a live control's text that
changed while hidden is spoken once the shown control is laid out. NVDA still
dropped it: a temporary Windows log showed the message's peer with real
bounds (381, 195, 522 × 19), on screen, but no parent, because Avalonia keeps
the empty link it looked up while the control was hidden, and a UI Automation
element that cannot be placed in its window has its live event discarded.
The children from the window down are listed again to restore the link
before any bridge raises its event. Main window notices still raise exactly
one live change each, which a test now counts, after an intermediate build
announced the first copy notice twice on macOS.

The same runs found that on macOS the first character typed into a field that
F7, Command+L or Command+S had just focused was lost: in the System Events
journey, and in every CGEvent HID probe of an isolated pre-fix instance (six
of six after F7 and Command+S, and after Command+L). An instrumented build
showed why. A key that runs a command arms a flag so its own character is not
typed into the field the command opens, and only the next key-down cleared
it; macOS types into a focused text field with text input and no key-down,
so the flag swallowed the user's first character. Only text equal to the
command key's own symbol is swallowed now, and Command and Control chords arm
nothing (`8d828249`); Num+ still does not type its `+` into Select by mask.
The Quick filter hint's folder example now uses the platform separator,
`src/` on Linux and macOS (`cda6a365`), and Orca read the Create folder hint
as "use / for nested folders" on Ubuntu.

`DescendantFocusAutomationTests` (14, adding the hidden-then-shown message
with its restored parent link and the prompt's spoken refusals),
`CommandKeyTextTests` (2; the text-without-key-down case fails on the
previous window code) and a 131-case keyboard, dialog and accessibility
selection pass. The complete Windows Release App suite on the `ad3bf946` code passed 2,506 tests with 71 platform skips and no failures using 16 workers. Git-built `ad3bf946` then passed the extended
journeys on all three desktops, exact fixture bytes afterwards: Orca 50.2
heard "Unclosed regular expression in mask." once as `/` was typed and once
more on Enter (Ubuntu, `FileCat.dll` `5de175b894f5e0d6103f046c0b339b681a26010b7a276b5823c1d6bbbd9cce7e`); NVDA 2026.2 heard the same (Win11 VM, `baa9265a75bd5845626d9508dfae9dba145e2411c50618d304c84308bc282bdc`); and the macOS AX
journey, typing `/` with a raw key code as the first character after
Command+S, observed exactly one `AXAnnouncementRequested` at priority 90 for
each (`fc9b44edf19354aee038b87078e8c5ac0e12f669741dbd44dfe00a6c86872531`).
A FileCat whose dialog is open does not exit on SIGTERM; the Mac runner now
kills its own process after an unanswered TERM.

**22 October 2026, spoken notifications on every desktop and macOS modifier
input (FCUX-029, FCUX-010):** The speech runners now also copy the focused
file twice. The first notification after startup was never announced on any
desktop: its text changed while the hidden notification bar had not yet
templated it, where no live-region event reaches a screen reader, and a
repeated notice kept its text and raised nothing (`25711ffc`). Both texts now
stay silent while a notification changes; once laid out, the shown one
becomes polite or assertive and raises one name change. Avalonia's AT-SPI
bridge has no live regions, so on Linux a live control's name change is sent
as an AT-SPI announcement from its window. Even then Windows and macOS stayed
silent until a reader had visited the text: Avalonia creates an element's UI
Automation node, and its macOS native element, only on first visit, and only
those raise the event. A live control attached to a window is now tracked
(`41a5ab48`): Windows gets the element's node, and macOS gets native elements
from the window down to it by refreshing each layer's children. A peer's
parent link can also end below the window, because Avalonia keeps a link it
looked up while a control was hidden; the macOS refresh and the Linux focus
attachment continue such a path through visual parents. Each bridge path
disables itself and logs once if the Avalonia member it needs is missing.
Overlay dialog cards also joined UI Automation's control view, so NVDA now
says "Create folder dialog, grouping" on entry.

The Mac run then heard "Copied 3 items" for one focused file. Avalonia reports
a macOS modifier change as a key press of the key in the system's change
event; a modifier set by synthesized input such as AppleScript's `using
command down` carries no key, which Avalonia reads as the A key. Every such
Command or Control shortcut therefore ran Select all first. The pre-fix
`9479b280` and `02ca4b75` bundles behave the same. CGEvent posts of the
modifier keys themselves arrive correctly as LWin and LeftCtrl, so only input
that sets modifier flags without a modifier key is affected. On macOS an A key
event whose
modifiers differ from those last seen, one per changed modifier, is now
marked handled where its route starts (`7da4fc0b`). On that build a native
probe found lone Command, Control, Shift and Option presses and Command+C
marking nothing, while `keystroke "a" using command down` and Control+A still
selected all three files. The same runs spoke the Create folder hint
"use `\` for nested folders" on Linux and macOS, where `\` is an ordinary name
character: following it created one folder named `left\right`. The hint now
names the platform separator and `\` nests only on Windows (`aef91939`).

`DescendantFocusAutomationTests` (12, including a hidden-then-shown container
for both bridge walks), `SyntheticModifierKeysTests` (5, including a Main
window check that a synthesized Control change does not select every file),
`CreateFolderPromptTests` (1; the Ubuntu and macOS CI lanes run its Unix
branch), `NotificationCenterTests`, `FileListAutomationTests` and
`PrivateAvaloniaApiTests` pass. The complete Windows Release App suite on the `7da4fc0b` code passed 2,501 tests with 71 platform skips and no failures using 16 workers. The Ubuntu CI lane on `fd816915` had failed one older list test that still expected no selection provider on the Linux parent row, which is a never-selected selection item there since `9479b280`; `2d2f7c90` updates that expectation. Git-built `7da4fc0b` then passed all
three native journeys (14 steps each, isolated three-file fixture, exact
bytes afterwards): Orca 50.2 on Ubuntu heard "Copied 1 item to the
clipboard." after both Ctrl+C presses (`FileCat.dll`
`df20632c53d3edbbadceb520ccbe834803cb9b9e9e1cc94b1b4d9874894b6e63`); NVDA
2026.2 on the Win11 VM heard the same twice and "Create folder dialog,
grouping" (`9814d711db447843464e19bafc6dc439162eab30a3c667ed25afd7784eafeed1`);
and the macOS AX journey observed one `AXAnnouncementRequested` from the
FileCat window reading "Copied 1 item to the clipboard." at priority 50 for
each Command+C (MacBook Pro M1,
`cacc09fef1e4f54322881151d0ec53c3423edd5ed7448b6d0c1e4eb2570aa4f3`).
Remaining: VoiceOver's other item and action speech, Orca's repeated
tutorial description, other live regions such as validation errors and Find
results, and large-listing latency.

**22 October 2026, screen-reader focus on all desktops (FCUX-010):** The
first actual screen-reader runs found that FileCat was silent while a user
moved between files. Avalonia reports the focused *control* as a window's
focused element, so the virtualized list announced itself once and its
focused row never reached a screen reader. On the pre-fix Ubuntu build, Orca
50.2 said only the window title; on the pre-fix Windows build, NVDA 2026.2 said
nothing for Down, Up, Home or End; on the pre-fix `c6806430` Mac bundle, an
`AXObserver` saw no `AXFocusedUIElementChanged` while the focused element
stayed the whole list. Avalonia's AT-SPI bridge additionally emits focus only
for nodes a client has already enumerated, announces nothing after window
activation and names unnamed layout containers after their class.
Every FileCat window now has a root automation peer that reports the list's
focused row as the window's focused element and raises the native focus change
when that row changes, through UI Automation, macOS accessibility and AT-SPI.
On Linux, the AT-SPI nodes from the window to the focused row are attached
through the bridge's own child enumeration before the event (a guarded shim
that disables itself if Avalonia's internal members change), and focus is
announced again after window activation on Linux and Windows. Unnamed layout
containers no longer fall back to class names on Linux, overlay dialogs are
named groups, a row's description no longer repeats its name, the parent row
is never described or accepted as markable and stays reachable in the AT-SPI
child set, and focus moves inside the visible rows no longer rebuild the
list's children. `IRootProvider` is an Avalonia private API; the App now
compiles against Avalonia's implementation assemblies and
`PrivateAvaloniaApiTests` fails on any other referenced private type or
member. `DescendantFocusAutomationTests` (6), the updated
`FileListAutomationTests`, `AccessibilityTests`, dialog viewport, lifetime and
focus-ownership classes passed 73/73, and the complete Windows Release App
suite on `9479b280` passed 2,490 tests with 71 platform skips and no failures
using 16 workers.
Git-built `9479b280` then passed three new native journeys with an isolated
three-file fixture (launch or genuine activation, Down ×3, Up, Home, End, Tab
to the target and back, F7 into Create folder, Escape, and a switch to a
test-owned window and back), each with exact fixture bytes afterwards:
`linux-orca-speech.py` heard every step through Orca's own speech log, for
example "a.txt. not selected. file, 6 bytes, …, not marked, 2 of 4" and
"Create folder dialog panel, Folder name (use \ for nested folders): entry"
(Ubuntu 26.04.1 GNOME, `FileCat.dll`
`53bed638d4a21901f8ed966cca8992711d43d0a1823eb19408622aa534363cc1`);
`win-nvda-speech.ps1` heard every step through a signed portable NVDA 2026.2
log, with the focused row named once after returning to FileCat (Win11 VM,
`89e23a64396e089d70c64a99f616027d8af2b35cc948a354c04c4ca70124e4f6`); and
`mac-ax-focus-speech.py` observed exactly one `AXFocusedUIElementChanged` per
move for an `AXRow` titled with the file name, an `AXTextField` for the dialog
field, and the focused row after returning from Finder (MacBook Pro M1,
`50a763083b88178d2587aa47223802d79e90c9947af7deaeb2207db6d80abee8`).
VoiceOver's AppleScript control is now enabled with owner approval and the Mac
output muted; the Undo review and report pass actual speech assertions above.
Actual VoiceOver file-row speech now passes the separate Mac journey above. Remaining
speech refinements: Orca also repeats a row description as its tutorial
message because Avalonia maps one help text to both AT-SPI description and
help text, and NVDA does not yet voice the dialog's group name on entry.
Large-listing latency is still unmeasured.

**22 October 2026, accessible panel sorting:** A bounded virtual header peer
was visible in the in-process automation tree but omitted by Ubuntu AT-SPI as
soon as a file list contained rows. Each panel now has a visible Sort button
beside its tabs, outside that list bridge. Its accessible name reports the
current field and direction; its menu lists the currently visible sortable
columns, identifies the selected column, and reverses it on a second choice.
The action belongs to the tab that opened the menu, so a stale choice cannot
sort a replacement tab. `PanelSortControlTests` passed 3/3 across 1,200 and
720-pixel windows. A Git-built Ubuntu VM passed physical pointer and AT-SPI
tests in both populated source and target panels at normal and measured
720 × 540 widths: the Name choice reversed four actual file rows, after
which Shift-click still marked the correct two files with unchanged source
bytes. Native screen-reader speech and the other file-list actions remain open.
The signed `a30a5ccf` Mac build then passed the test-owned
`mac-ax-panel-sort.py` runner at normal and 760 × 600 widths. Direct
AXUIElement traversal found bounded Sort buttons in both panels and the five
visible sortable choices. One physical pointer sequence reversed the source
row order; keyboard selection of the currently active Name choice restored
it. The target order and four source SHA-256 hashes stayed unchanged, and the
target remained empty. The direct AX helper uses the matching Xcode SDK
because the guest's default Command Line Tools SDK is newer than its Swift
compiler. The runner additionally touched `b.txt`, reversed Name order and
physically Shift-clicked `c.txt`; direct AX reported exactly those two rows
selected, `c.txt` focused, and `Marked 2 of 4 files · 13 bytes`. Reversing
Name again preserved the same selected identities. Both normal and 760 × 600
native JSON reports passed with exact source hashes. A separate Git-built Mac
native runner then passed filter-anchor retirement at normal and 760 × 600
widths: it touched `b.txt`, entered `*|b*` through the actual Filter field,
observed only `a.txt`, `c.txt` and `d.txt`, restored `*`, and physically
Shift-clicked `d.txt`. Direct AX reported exactly `c.txt` and `d.txt`
selected, `d.txt` focused and `Marked 2 of 4 files · 14 bytes`; all four
source hashes and the empty target were unchanged. A fresh two-folder Mac fixture then
passed a native second-tab route at normal and 760 × 600 widths: `d.txt`
was focused in the first tab, Ctrl+T exposed three distinct accessible tab
identities, and the second tab entered its own `second` folder. Physical
pointer and Shift-click input selected exactly `e.txt` and `f.txt` there,
with `f.txt` focused and `Marked 2 of 2 files · 13 bytes`. Both source
folders kept their original SHA-256 hashes and the target remained empty.
A third native Mac runner passed held-Shift window deactivation at normal and
760 × 600 widths. It physically marked `b.txt`, moved focus back without
losing the mark, held Shift while a second Down unmarked `b.txt`, switched to
Finder with Shift still down, released Shift there, and returned to FileCat.
A fresh Shift+Down then marked only `c.txt` (8 bytes) and focused `d.txt`.
The same runner then moved focus back to `c.txt`, held Shift while unmarking
it, clicked the command line with Shift still down, released Shift there and
used Escape to restore list focus. A fresh Shift+Up marked only `d.txt`
(6 bytes) and focused `c.txt`. Both routes passed at normal and 760 × 600
widths with exact source hashes and an empty target. A Git-built Ubuntu VM
also passed the command-line route at normal and measured 720 × 540 widths:
physical uinput touch and keyboard input marked `b.txt`, held Shift across
the focus move into the accessible command-line entry, released it there,
and used Escape to return to the list. The next Shift+Down marked only
`c.txt` (8 bytes) and focused `d.txt`; all four source hashes and the empty
target remained exact. A stricter native rerun detected that the original
fixed-duration uinput helper could release Shift before the focus change.
The runner now sends a fresh, owned release signal after observing focus and
asserts the helper remains active before and after the change. Both Ubuntu
command-line and window-deactivation routes passed again at normal and
720 × 540 widths with this controlled hold. Scaled-display and other focus
routes remain open. The signed Mac validation build also passed a deleted-anchor
refresh route at normal and 760 × 600 widths. A physical click anchored
`b.txt`, then a test-owned external mutation removed that file and added
`0.txt`. Native Ctrl+R produced the new `0,a,c,d` order; after keyboard
focus moved to `d.txt`, a physical Shift-click on `0.txt` marked exactly all
four current files for 25 bytes, with `0.txt` focused. Each resulting source
hash and the empty target were verified. The bounded direct AX helper now
includes the new `0.txt` fixture row. Ubuntu repeated the same deleted-anchor
refresh at normal and 720 × 540 widths with a native uinput touch anchor,
Ctrl+R, four Down keys and a calibrated physical Shift-click on `0.txt`.
AT-SPI reported the same exact `0,a,c,d` order, all four current files
marked for 25 bytes, `0.txt` focused, expected source hashes and an empty
target. The old `b.txt` row was absent. A separate Mac native pointer run
then exposed Command-click treating an additive mark as an ordinary focus
click. The file list now treats Command as the additive modifier on macOS,
while preserving Ctrl-click elsewhere and retaining the existing anchor.
The signed Git build `c6806430` passed at normal and 760 × 600 widths:
Command-click marked only `d.txt`, Shift-click from the original `b.txt`
anchor marked `b,c,d` (19 bytes), and a second Command-click unmarked `d.txt`
to leave `b,c` (13 bytes). Direct AX focus and status, all source SHA-256
hashes and the empty target matched. The focused App selection suite passed
11/11 on Windows and macOS with up to 16 build workers. A Git-built Ubuntu
VM then passed physical Ctrl-click at normal and 720 × 540 widths using a
calibrated uinput pointer: `d.txt` alone was marked for 6 bytes, then a
Shift-click on `c.txt` added exactly `b.txt` and `c.txt` from the original
anchor for 19 bytes. AT-SPI focus, source hashes and the empty target were
exact in both runs. GNOME's temporary logical-monitor mode then enabled real
150% and 200% display-scale checks, at 1920 × 1200 and 2560 × 1600 physical
pixels respectively. Xwayland's mouse coordinate space disagreed with
physical uinput after scaling, so the scaled runner used an explicitly held
Ctrl/Shift key plus physical touch. At 100%, 150% and 200%, AT-SPI exposed
exactly `d.txt` after Ctrl-touch (6 bytes) and `b,c,d` after Shift-touch
(19 bytes), with `c.txt` focused; all source hashes and the target were
exact. The VM's original 1280 × 800, 100% display mode was restored and
verified. A subsequent 100% held-Shift window-deactivation regression also
passed with the generalized uinput helper.
The complete Windows Release App suite on `c6806430` passed 2,420 tests,
skipped 63 platform cases and failed none with up to 16 workers. The earlier
Core suite passed 4,437 with 151 skips and no failures on the same core code.

GitHub Actions run `39749992629` on `7b5b0bea` completed green on the
producer policy, Windows, Ubuntu, macOS and Windows ARM64 lanes. The Mac
checkout's complete Core Release suite passed 4,450 tests, skipped 133
platform cases and failed none with 16 processors and the SDK on `PATH`.
GitHub Actions run `39758524147` on the Sort-control product commit
`a30a5ccf` also completed green on all five required lanes. The later
native-acceptance commits still need the final-head CI gate.

**22 October 2026, CI recovery-test synchronization:** The Windows CI run on
`cf791da9` exposed a file-sharing race in the isolated dependent-Undo crash
harness: it created `inverse-held.json` before closing the writer, while the
parent read the first visible file. The harness now writes a same-directory
temporary marker and atomically renames it after closing, so a visible marker
is complete and readable before the parent interrupts the exact child. The
focused Windows Core cases passed 2/2. Windows and ARM64 runs on earlier heads
also caught the interrupted-rename UI test sampling an overlay before its
review text had rendered. That test now waits for the exact bounded review
content, then checks the remaining warning and full temporary path; its focused
Windows App case passed. The crash marker cases passed 20 repeated runs (40
cases), and the interrupted-rename review passed 20 repeated runs. The
product's recovery guards and content were not changed by these test
synchronization corrections. Final-head CI remains open.

GitHub Actions run `39738616565` on `3edb6bb6` completed green on the
producer policy, Windows, Ubuntu, macOS and Windows ARM64 lanes. Later
acceptance-runner and test commits require their own final-head run.

**22 October 2026, desktop icon-theme helper deadline:** Mac and Ubuntu CI
occasionally lost the exact `Adwaita` setting from a successful `gsettings`
helper under load. The helper now starts draining its bounded stdout as soon
as it starts, then allows one bounded second after exit for a pending read to
finish. A new regression holds stdout open in an owned descendant after the
parent exits and verifies the query still returns within its deadline.
Focused tests passed on macOS ARM64 (7 passed, 1 platform skip) and Ubuntu
(8 passed, 0 skipped); the prior exact-setting Mac case also passed 20
repeated runs. Final-head CI remains open.

The full Windows Core Release suite on `3edb6bb6` passed 4,437 tests, skipped
151 platform-specific cases and failed none with 16 processors. The Git-built
Win11 VM app from that same head (SHA-256
`CDAA633E53B23A0F4611916F8DFB2E3B401517821823BCFA5889AACDDDA0984E`)
also repeated the native FCUX-018 dependent-Undo crash and recovery journey.
The exact child was killed at its held inverse rename; Cancel preserved the
journal and bytes, approval restored the recorded temporary item without
closing the unverified journal, and a separately reviewed decision retired it.
The VM receipt is `AppData\Local\FileCatValidation\win-undo-last.json`.
The separate native close/restart and large mixed-report gates remain open.

The same Windows checkout's complete App Release suite passed 2,410 tests,
skipped 63 platform cases and failed none with 16 processors. Its build
preceded the additional FTP/FTPS warning test cases below; those passed
separately against the same product code.

**22 October 2026, native Move review pointer exit:** The shared Win11 dialog
runner now opens a disposable Move review and tests both native pointer exits.
On the Git-built `3edb6bb6` app at 96 DPI, its 30 × 30 close target exposed
the cancel HelpText, its 92 high-contrast X pixels were centered within
(-0.22, 0) physical pixels, and both the X and backdrop click dismissed the
review. The source SHA-256 stayed
`6C9A89482AB2E0A75957FFBAADD3F37737898C7A75A5FD8DBAA210B5B00AC398`;
the target stayed empty. The guest report is
`AppData\Local\FileCatValidation\win-dialog-move-20261022.json`. Scaled,
spoken and other dialog-family checks remain open.

**22 October 2026, native Ubuntu Move review pointer exit:** The Wayland
VM built Git head `b5f4fbbe` with zero warnings and errors. The shared AT-SPI
runner selected the owned source row with its temporary uinput touch device,
opened the Move review twice, and used physical uinput mouse clicks on its
30 × 30 Close dialog target and on the backdrop. Both exits preserved source
SHA-256 `02d96fb583ff0571cde49db8f2ecffd43cedb0abacf6ed296114231d39328647`
and left the target empty. The accessible cancel description was present.
The guest receipt is `~/FileCatValidation/ubuntu-dialog-move-20261022.json`.
Scaled, spoken and other dialog-family checks remain open.

**22 October 2026, native Ubuntu range anchor:** A new isolated AT-SPI runner
uses physical touch to focus `a.txt` and a transient uinput keyboard for
Shift+Down, Up and Shift+Down. On the Git-built `b5f4fbbe` app, both normal and
measured 720 × 540 windows reported the exact sequence: zero marks with
`a.txt` focused; one 6-byte mark with `b.txt` focused; the mark retained with
`a.txt` focused; zero marks with `b.txt` focused. The source hashes stayed
`b6a98d9ce9a2d9149288fa3df42d377c3e42737afdcdaf714e33c0a100b51060`
and `f2c82decdd7181cf98945929a62598db7e6b477e11f6e0eb0ae97020eff151ad`;
the target stayed empty. Guest receipts are
`~/FileCatValidation/ubuntu-selection-keyboard-20261022.json` and its
`-narrow-` counterpart. A separate four-file Ubuntu run touched `b.txt`,
reversed the Name sort to the observed `d.txt`, `c.txt`, `b.txt`, `a.txt`
order, then physically Shift-clicked `c.txt`. The exact mark was `b.txt` and
`c.txt` (13 bytes), with `c.txt` focused, unchanged SHA-256 values for all
four source files and an empty target. The Wayland pointer was measured and
calibrated into `c.txt` before the uinput button event; the report records
each requested and actual coordinate. Guest receipt:
`~/FileCatValidation/ubuntu-selection-sort-20261022.json`. Scaled display,
tab transitions and spoken feedback remain open. A companion Ubuntu filter
journey used physical keyboard input to apply `*|b*`, observed `b.txt`
disappear, restored `*`, then physically Shift-clicked `d.txt`. The obsolete
`b.txt` anchor did not reappear: only `c.txt` and `d.txt` were marked (14 bytes),
`d.txt` was focused, all four source hashes remained exact and the target
stayed empty. Guest receipt:
`~/FileCatValidation/ubuntu-selection-filter-20261022.json`.
The same fixture now also passed native second-tab selection: after touching
`d.txt` in the first tab, Ctrl+T opened a new tab, Enter navigated into the
owned `second` folder, Home/Down focused `e.txt`, and physical Shift+click on
`f.txt` marked exactly `e.txt` and `f.txt` (13 bytes), with `f.txt` focused.
The four first-tab and two second-tab SHA-256 values remained exact, and the
target stayed empty. Sort, filter and tab variants each passed again at a
measured 720 × 540 window, with their respective 13-, 14- and 13-byte exact
marks. Guest receipts are `~/FileCatValidation/ubuntu-selection-tab-20261022.json`
and the `ubuntu-selection-{sort,filter,tab}-narrow-20261022.json` files.
Scaled display, deeper focus transitions and spoken feedback remain open.
The Ubuntu held-modifier deactivation journey then passed at normal and
measured 720 × 540 widths. Physical Insert marked `b.txt`; Up returned focus
to it; a physically held Shift+Down unmarked `b.txt` and focused `c.txt`.
While Shift remained down, the test activated a separate owned GTK window,
released Shift there, reactivated FileCat and sent a fresh Shift+Down. Only
`c.txt` became marked (8 bytes) with `d.txt` focused. All four source hashes
and the empty target remained exact. Guest receipts are
`~/FileCatValidation/ubuntu-selection-deactivate-20261022.json` and its
`-narrow-` counterpart. Other non-Windows focus routes and scaled/spoken
acceptance remain open.

**22 October 2026, Mac CI runner variance:** The `8d8af3f8` macOS CI lane
timed out the existing 20,000-entry archive rewrite performance assertion
after 45 seconds while all preceding and adjacent product code was unchanged;
the preceding `b5f4fbbe` run passed. The test retains a bounded performance
check with a 90-second allowance for shared-runner scheduler and filesystem
load. Its targeted run on the isolated Mac passed (1/1); final-head CI must
confirm all required platform lanes.

**FCUX-001 remote publication capability review:** The implemented SFTP
`posix-rename@openssh.com` path can replace an existing file in one server
operation, but its interface has no expected-version argument. The ordinary
SFTP rename path is for a name that does not exist. The FTP channel exposes no
atomic replacement and implements it as delete followed by rename. Therefore
none of these supported channel operations can make the final content check
and publication one conditional server action. The reviewed Replace warning
must remain visible; source preservation, an unpublished stage, and fresh
metadata/content checks reduce exposure but cannot eliminate a concurrent
server writer after the last check. Server-specific conditional publication
remains a separate capability to investigate only if a supported provider
offers it.

The transfer form and per-item conflict review now have explicit regression
cases for actual saved SFTP, FTP, explicit FTPS and implicit FTPS profiles,
plus a local control. The nine focused App cases passed and verify that the
warning follows the reviewed destination and Replace choice. An additional
controlled SFTP Move test drops an upload, refuses cleanup of the unpublished
stage after cancellation, and requires an unchanged 2 MiB source, no public
target, and an issue naming the exact retained stage; it passed. Native
server-failure variants and end-user stage cleanup remain open. The complete
Windows Remote Release suite then passed 2,397 tests, skipped 157 platform/lab
cases and failed none with 16 processors.

**22 October 2026, stale bulk-rename editor scope:** A headless UI regression
opens the real Bulk rename review with one visible and one hidden mark,
launches an editor round trip through an injected private file writer, changes
the scope while Load names is pending, and then loads the old one-line result.
The generation guard rejects that stale editor result. The refreshed two-row
preview shows `reviewed_1.txt`/`reviewed_2.txt`; the approved real Rename job
changes exactly those two source files and does not create the stale editor
name. The affected Windows Release bulk-rename selection passed 22 tests,
one Unix-only skip, zero failures. The complete Windows Release App suite
then passed 2,405 tests with 63 platform skips and zero failures using up to
16 processors. Native external-editor and rule-change journeys remain to be
exercised.

**22 October 2026, Windows scaled bulk-rename review:** The native runner now
binds `rename-hidden-native` to an exact Git-built executable under the guest
validation root and finds the enabled Rename button within the modal that
contains the hidden-mark choice and Name mask. A prior direct-child XPath
failed to find the action, while an unscoped button search hit an obscured
main-window Rename action; neither was counted as a product pass. Fresh
Win11 fixtures then passed at 100%, 150% and 200% display scale on the same
app DLL (SHA-256
`D7568EFB4548F6DC6545B835A27E2AA2381F62B49A6D336F9EA280A7923683D6`).
At each scale, the review first showed only visible `b.txt`, then explicitly
included the hidden marked `a.txt`, displayed the exact two-row counter/name
mapping, and created `reviewed_1.txt`/`reviewed_2.txt` with the original bytes.
Every destination stayed empty, and the path-verified app closed. Display
Settings restored the VM to 100% (96 DPI). Mac/Ubuntu scaled and narrow
review and keyboard/spoken checks remain open; the Windows native editor round trip passed later, as recorded below.

**22 October 2026, Windows native bulk-rename editor:** The original dialog
placed “Edit names in editor…” below the tall preview and path details, out of
the initial viewport. Both a coordinate click and a UI Automation click could
miss it. The action now precedes the preview; the 640 × 420 modal viewport
test checks its order and the reachability of the final path detail. The
focused dialog/scope suite passed 39/39 on Windows with 16 workers. Running
the same fixture inside the process filesystem sandbox caused five unrelated
Temp rename denials; their job issues identified `Access is denied`, and the
unrestricted rerun passed all 39. An isolated Win11 VM then passed the real
external-editor path at 100% display scale on app DLL SHA-256
`844B535C9CA1CB05A224E4D372639C47E3DBCBCEF8A5D6D62F570DF23354430E`.
Its structured editor setting launched a fixture-bound native helper, saved
`editor_b.txt`/`editor_a.txt` for the visible and explicitly included hidden
sources, used the nested Load names decision, and showed both edited mappings.
The private list was removed; only the two source files were renamed with
matching bytes, the destination stayed empty, and the exact app closed.
Fresh VM fixtures repeated that entire editor round trip at 150% and 200%
display scale on the same app DLL. The dialog visibly kept the editor action,
both mappings and Cancel/Rename controls in view at each scale; the native
reports passed the edited names, file contents, empty target and process stop.
The scale sweep restored Windows to 100% (96 DPI).
At that point, Mac/Ubuntu editor journeys and native rule-change races remained open.

**22 October 2026, Mac native bulk-rename editor:** A fresh shallow checkout
of `24eb70f1` published and ad-hoc signed an isolated arm64 validation bundle
(app DLL SHA-256
`672ABE794C01B4A0D87A8EEA55812DFC9FEB8503AB58BAC8706B73484E63F437`).
The Mac normal and 760 × 600 narrow runs both passed on fresh two-file
fixtures. Native click/Shift selection marked both sources, a filter hid one,
the unchecked scope choice was explicitly enabled, and the counter mask
produced the reviewed first full-path mapping. A controlled editor saved
`editor_b.txt`/`editor_a.txt`; Load names published its explicit editor-derived
status and first full-path mapping. The private list was removed. A physical
click on the modal-owned Rename action produced exactly those two source
filenames with their original hashes and an empty destination. Approved native
captures show the editor action, both preview rows and decision buttons in
view at both widths. macOS AX exposes the scope, mask, summary and selected
full-path detail, but reports zero bounds for the visible modal body and no
children for its virtualized preview list. AX actions still activate those
controls; complete spoken inspection of every mapping remains open.

**22 October 2026, Git-built Windows selection at narrow width:** The native
runner now accepts `FILECAT_NATIVE_TEST_APP` for selection journeys and checks
the exact executable path under the guest's validation root before focusing,
resizing or stopping it. This avoids attaching a current fixture to the older
`ReviewHighlights` build. On Win11 VMware, the guest checked out
`369b5ced`, rebuilt the runner with zero warnings/errors, and ran two fresh
fixtures against the Git-built app (SHA-256
`0FFB1E3A06798D7D4C30C574F0FADF6FBE86DA704631EC354770717C6D271A68`).
At 720 × 540 physical pixels, sorting from `a,b,c,d` to `d,c,b,a` and
Shift-clicking marked exactly `b.txt` and `c.txt`; source bytes and the empty
destination remained exact. A second narrow fixture held Shift across an
Explorer window switch and focus loss: the old gesture marked nothing, then
fresh gestures marked only `c.txt` and `d.txt` in their respective phases.
Both reports passed, both app instances closed gracefully, and only other
narrow/scaled and non-Windows focus journeys remain for FCUX-002. A follow-up
Git build at `1c4f93ff` (same app SHA-256) enabled the previously unreachable
tab-selection runner mode and added five measured narrow variants. Native
Win11 runs on separate 720 × 540 fixtures passed filter hide/restore with
only `c.txt`–`d.txt` marked, a new tab with only `e.txt`–`f.txt` marked,
refresh after replacing `d.txt` with `0.txt` with only `b.txt`–`c.txt`
marked, Ctrl-click toggling followed by the original `b.txt`–`c.txt` range,
and keyboard Shift+Down mark/unmark with exact focus. Every run checked
unchanged expected source bytes (or the exact owned refresh mutation), an
empty destination, and a graceful path-verified process close. Their JSON
receipts are retained under the guest's
`AppData\Local\FileCatValidation\SelectionGit-20261022-selection-*-narrow-native`
folders. The normal and narrow Windows transition matrix now passes; scaled
input and non-Windows focus routes remain open.

**22 October 2026, Windows selection at 150% and 200%:** The native runner
was initially DPI unaware, so WinAppDriver's physical row rectangles sent
the pointer to the wrong place at 150%; the first run sorted correctly but
marked nothing. The runner now requires per-monitor DPI awareness before any
desktop mode and records its awareness and tested row bounds. Its source
snapshot also retries a transient WinAppDriver HTTP 500 during list rebuilding
up to four times at 200 ms; a persistent error still fails the run. The
runner rebuilt with zero warnings/errors. A clean Git clone at `37bc7288`
built FileCat and the native runner with 16 workers; the app DLL SHA-256 was
`D7568EFB4548F6DC6545B835A27E2AA2381F62B49A6D336F9EA280A7923683D6`.
Fresh native Win11 fixtures passed the sort/Shift-click range at 100%, 150%
and 200%: the order reversed from `a,b,c,d` to `d,c,b,a`, exactly `b.txt`
and `c.txt` were marked, and source bytes and the destination were unchanged.
Fresh 150% and 200% filter fixtures hid `b.txt`, restored it, and used the
retired-anchor fallback to mark exactly `c.txt` and `d.txt`, also without
changing bytes. Reports retain the exact paths, sort/filter state, marks and
graceful process stop. Windows Display Settings restored the VM to 100%
(96 DPI). Other scaled selection routes, Mac scaling, streaming/creation
transitions and remaining focus routes stay open.
The same Git-built app and DPI-aware runner then passed five more fresh
native fixtures at 200%: a new tab marked only `e.txt` and `f.txt`; refresh
after replacing the anchored `d.txt` with `0.txt` marked only `b.txt` and
`c.txt`; Ctrl-click toggles preserved the original `b.txt` anchor for the
`b.txt`–`c.txt` range; keyboard Shift+Down marked and then unmarked the exact
range; and a held Shift across Explorer deactivation and command-line focus
returned to new, correct gestures. Each report records the fixture identity,
exact expected file state, empty destination and graceful process stop. The
VM was restored again to 100% (96 DPI).
The five additional routes also passed at 150% on fresh isolated fixtures,
with the same exact tab, refresh, Ctrl, keyboard and deactivation outcomes;
the reports retain each path, mark set and fixture check. Native Display
Settings restored the VM to 100% (96 DPI) again. The verified Windows
selection transition matrix now covers normal and narrow windows at 100%
and the seven tested transition families at 150% and 200%. Streaming,
Mac scaled input and other focus routes remain open.

**22 October 2026, Windows creation before a retained selection anchor:**
The native runner added `selection-create-native` on a fresh, path-verified
fixture. It clicks `c.txt`, creates `0.txt` ahead of it, rereads the listing,
and Shift-clicks `d.txt`. Runs at 100%, 150% and 200% all marked only
`c.txt` and `d.txt`, retained `c.txt` focus after refresh, checked every
source file's exact bytes and an empty destination, and closed the app
gracefully. The 200% test now waits for both the new row and settled focus
before evaluating the gesture; its first early snapshot was inconclusive.
The isolated app DLL SHA-256 was
`D7568EFB4548F6DC6545B835A27E2AA2381F62B49A6D336F9EA280A7923683D6`.
Display Settings confirmed restoration to 100% (96 DPI). Native streaming,
Mac alternate-mode gestures beyond pointer/Shift and remaining focus routes
remain open.

**22 October 2026, Mac display modes and selection input:** The signed
Git-built `80598aeb` app (DLL SHA-256
`7409bf7706e9dbe5ed477a0a7b86781fdc485ad256180d5cfd7c20e9e154aa20`)
ran fresh native pointer/Shift selection fixtures on the Mac at both its
1440 × 900 logical/2880 × 1800 pixel Retina mode and a supported
1920 × 1200 logical/1920 × 1200 pixel mode. In each, clicking `a.txt`,
Shift+Down, Up and Shift+Down marked then cleared only the six-byte anchor,
retained exact focus, preserved both source files and left the destination
empty. The tracked `mac-display-run.swift` verified restoration of the
original Retina mode after the 1× test and after a deliberately failing child
command (whose nonzero status propagated); no FileCat process remained. The
guest reports are `mac-selection-retina-20261022.json` and
`mac-selection-display1x-tracked.json`, with ignored host copies. Other
Mac selection gestures at alternate display modes were tested next.

The same 1× Mac mode then passed six more fresh native fixtures: sort reversal
marked only `b.txt`/`c.txt`; filter hide/reveal retired `b.txt` and marked
`c.txt`/`d.txt`; a second tab marked only `e.txt`/`f.txt`; deleted-anchor
refresh showed the expected replacement row and fallback range; Command-click
toggles retained the range anchor; and held Shift released in Finder or the
command line started new, correct gestures. Each test retained exact source
hashes (or the reviewed refresh mutation), an empty target, and the same app
hash; the tracked display runner restored Retina after each. The six JSON
reports are `mac-selection-{sort,filter,tab,refresh,command,focus}-display1x.json`
in the Mac validation folder with ignored host copies. Together with the
earlier normal/narrow Mac, scaled Ubuntu and Windows runs, and the 11 focused
`FileListSelectionTests` passing on the current checkout, FCUX-002's six
acceptance criteria are met. Streaming is verified deterministically by the
real headless `FileListControl` and a gated provider that publishes a second
batch into the **same** store while the pointer anchor exists; timing a local
native enumeration would be less controlled and adds no different input
boundary. Native creation/refresh and all required input/focus gestures were
verified separately on real desktops. FCUX-002 is closed; the shared final
CI gate remains open for the audit as a whole.

**21 October 2026, progress refresh race:** An Ubuntu CI verified-copy test
caught `JobViewModel.Refresh` reading the worker's current item twice. A
transition from `big.bin (verifying)` to the shorter `big.bin` between reads
could make the view slice beyond the name and throw, aborting the Operations
refresh. The view now uses one captured, escaped item name to determine both
the displayed name and verification phase. A concurrent worker/refresh test
alternates a short name with a longer verification label and checks every
rendered name; the focused Windows Release test and the real verified-copy
test both passed. The complete Windows Release App suite passed 2,397 tests
with 63 platform skips and zero failures. Producer, Windows, Ubuntu, macOS
and Windows ARM64 CI lanes all passed on `3f7c50d0`; each later commit still
needs its own final-head CI gate.

**Mac active Copy interruption, 21 October 2026:** Two independent, isolated
4 MiB fixtures ran on the signed `a968d412` Mac ARM64 build. Each approval
recorded an exact 65,536 bytes/s limit and source-to-destination mapping in
the durable journal. The first run was terminated after all source bytes had
reached staging but before the target was published. Restart retained the
journal and complete staging; review showed full destination and explicit
Cancel/Continue. Cancel preserved both; reviewed Continue produced the exact
target SHA-256, removed staging, showed Completed and closed recovery
tracking. The native runner then stopped only that completed test process.

The second run terminated the exact FileCat process earlier, with only
1,048,576 correct prefix bytes written to a preallocated 4,194,304 byte
staging file and a zero-filled unwritten tail. The final target was still
absent and source SHA-256 was
`4bd39e98c6b3a8b06914e7ee08fd2b4615ad6c0f167dc7c2110b0e174c0ddc61`.
Restart exposed the retained Copy and destination; opening and cancelling
review left the 1 MiB prefix, source and journal exact. Reviewed Continue
finished with the same source/target SHA-256, removed staging, showed
Completed and closed recovery tracking. The runner retained both fixtures,
journals and JSON receipts under Mac `FileCatValidation` and stopped only its
completed app process. Other provider interruption paths remain open.

**21 October 2026, finished operation details and editor Find:** The selected
finished operation displays the same explicitly labelled average transfer
speed as its report. The details line now wraps and uses a stronger weight so
the value remains readable in a narrow Operations window; an empty rate stays
hidden for operations that transferred no data. A headless view test completes
a real Copy and checks the visible details text, wrapping, accessible name and
destination bytes. The Hex editor Find row now exposes Previous and Next
buttons alongside a bounded, truthful count of distinct matches visited for
the current query. Changing the query clears the count. The editor's
headless test traverses two matches more than 4 MiB apart in both directions,
checks no-earlier feedback, changed-query reset and unchanged source bytes.
The focused Release App tests passed all six cases with 16 workers; the
final full App suite passed 2,388 cases with 63 platform skips and no failures.
An isolated Windows VM build passed the native Hex editor run: its visible Next
and Previous buttons reached 0x800014 and 0x100014 across a 13 MiB file, the
status counted two distinct visited matches without claiming a file-wide
total, F3 and Shift+F3 still worked, and a changed query reset the count to
one at 0x80001B. The editor closed, exact source bytes remained and the
test-owned fixture was removed. The runner selects the exact app process when
multiple FileCat windows share a title. The re-signed Mac validation app also
passed a fresh native run: accessible Next and Previous crossed matches at
0x104028 and 0x5140D5, the visible distinct-visited count advanced from one
to two, F3 and Shift+F3 still worked, and a changed query reset the count to
one at 0x5140DC. The editor reported no unsaved changes, closed, and left
the exact test-owned source bytes intact. Final CI and other desktop
acceptance remain in [UX_UI_REMAINING.md](UX_UI_REMAINING.md).

The Ubuntu VM passed the same new Hex editor control journey on its updated
Git build. Because Wayland exposed no File-menu AT-SPI action, the native test
used FileCat's accessible command search and the guest's established uinput
Enter helper to open the test-owned editor. Next and Previous reached matches
at 0x100014 and 0x600014 across a 7 MiB file; the visible distinct-visited
count reached two, then reset to one after a changed query. A subsequent run
used physical guest F3 and Shift+F3 to traverse those same matches and check
the no-earlier message. Both runs closed the editor and left the exact source
bytes intact.

**23 October 2026, Ubuntu scaled Hex editor Find:** The exact Git-built
`72641024` app (DLL SHA-256
`6743d4ff91240941b5b4c78707983a35edeffe0ac6c416c2bfbaadd14087d7bb`)
passed the native `linux-atspi-hex-editor-find.py --scaled` route at GNOME
200%. A second `--narrow` run measured a 720 × 500 app-unit editor. AT-SPI
confirmed that the query, Previous, Next and truthful two-match status all
had visible bounds inside the editor in both sizes. Physical F3 and Shift+F3
visited exact offsets `0x100014` and `0x600014`, a changed query reset the
count to one, the editor closed, and the test-owned source bytes stayed exact.
Both receipts are retained in the Ubuntu VM as
`ubuntu-hex-find-scaled-72641024.json` and
`ubuntu-hex-find-narrow-72641024.json`. Windows and Mac scaled/spoken Hex
acceptance remains open.

**23 October 2026, Windows CI refresh test:** Run `39961031398` passed its
producer, Ubuntu, macOS and Windows ARM64 lanes, but the Windows App suite
failed `FileListSmokeTests.Refresh_does_not_pull_a_manually_scrolled_view_back_to_its_focused_row`.
Its setup clicked 75% down the scrollbar track after setting row 100; on
that runner the point could land on the thumb and make no movement. The test
now sends wheel input over the listing, then checks that a real user viewport
change survives refresh and resize. The focused Release test passed five
consecutive local Windows runs with 16 build workers. A green final-head CI
gate remains required. Run `39967112375` on `dfd926b2` subsequently passed
producer, Windows, Ubuntu, macOS and Windows ARM64, including the corrected
refresh assertion.

**23 October 2026, stale Mac accessibility row crash (FCUX-010):** An isolated
Retina Hex editor run on signed `cc31c682` refreshed its 7 MiB fixture and
macOS asked an old file-row peer to focus during the update. The native
callback propagated `InvalidOperationException` from
`FileListItemAutomationPeer.SetFocusCore`, aborting FileCat. The guest kept
`MacHexScaled-dfd926b2` and its diagnostic crash log for inspection; no
transfer occurred. Stale or disabled row patterns now return without acting,
and focus/invoke callbacks that change their own listing cannot carry an
expected invalid-operation exception across Avalonia.Native. The existing
headless tests now assert that stale selection, focus, invoke and scroll
requests leave current marks, focus and destinations alone. All 19 focused
`FileListAutomationTests` passed locally with 16 build workers. The signed
Mac native rerun on exact Git build `6ff4164d` passed without another crash;
final CI remains open.
The `6ff4164d` Ubuntu CI lane found one older AT-SPI parent-row test that
expected native selection callbacks to throw. The test now asserts the safer
contract: all three parent selection actions return without changing marks
or focus. The exact Git-built `116d39ff` Ubuntu VM rerun passed all 14
applicable `DescendantFocusAutomationTests` with one Windows-only skip, using
16 build workers. Its log is `ubuntu-focus-ci-116d39ff.log` under the guest's
`FileCatValidation`. Final CI remains pending.

**24 October 2026, native Mac large-list accessibility (FCUX-010):** The
signed `faf9b566` candidate listed 2,002 files in its isolated source panel
in both Retina 2× and supported 1× display modes. The bounded direct
AXUIElement snapshot visited 494–495 elements and exposed 24 visible rows,
with no eager peer for each of the 2,000 added files. Its snapshots took
0.18–0.44 seconds; ten native Down key events reached the expected tenth item
in 0.52–0.53 seconds. The reports
`mac-large-list-pointerfix-r5.json` and
`mac-large-list-pointerfix-1x-r2.json` record the exact DLL hash, file count,
focus, bounds, timings and original source/empty-target checks. Both runs
removed all 2,000 test-owned files, and the display wrapper restored Retina.
Other VoiceOver item/action routes, provider-backed listings and Orca's
repeated tutorial description remain open.

**23 October 2026, Retina Hex Find after the crash fix (FCUX-007):** The
signed `6ff4164d` app (DLL SHA-256
`26e0adec4db3722766b3007ef2afe7b78b1930d1dbce9565c71730e09e81fd76`)
passed the test-owned 5.4 MiB Hex editor journey at the Mac's 2× Retina mode.
After a requested 720 × 500 logical-point resize, native macOS reported a
clamped 911 × 648 window. AX bounds kept Find, Previous, Next and the truthful
two-match status inside it. Visible controls and F3/Shift+F3 reached exact
offsets `0x104028` and `0x5140D5`, then a changed AX query found `0x5140DC`
with the count reset to one. The editor closed with no unsaved changes,
its exact source bytes remained and the owned fixture was removed. Receipt:
`mac-hex-narrow-6ff4164d-h.json` under Mac `FileCatValidation`. Ctrl+F after
resize reported the Hex status as the focused AX element, so that keyboard
focus route and the unexpected native size clamp remain open. The initial
stale-peer crash receipt and fixture remain isolated for diagnosis.

**23 October 2026, Hex editor native minimum investigation:** The same signed
`6ff4164d` app at a supported 1× Mac display also refused a 720 × 500
request, reporting 1013 × 727 before the wrapper restored Retina. In a
headless infinite-width measure, its long wrapping hint and toolbars reported
a 3850-pixel intrinsic width; macOS used an oversized native minimum even
though the controls fit a finite 720 × 500 viewport. The Hex editor root now
bounds only its unconstrained intrinsic measure to 720 × 500, while real
finite window layouts still use their available width and height. The focused
headless test measures that bound, renders 720 × 500 with Find, Previous,
Next and count inside the window, then verifies Ctrl+F returns focus to Find
after a completed search. It passed locally with 16 build workers. The signed
`234e2d32` build then passed the complete native Hex Find journey on both
Retina 2× and standard 1× Mac displays at approximately 720 × 500 logical
points. macOS sometimes clamped an AX size request near 900–975 × 650–700;
dragging the actual lower-right native resize edge reached 721 × 501 at 2×
and exactly 720 × 500 at 1×. At both sizes AX bounds kept Find, Previous,
Next and the visited-match count inside the window. Native buttons and
F3/Shift+F3 reached `0x104028` and `0x5140D5`; a changed query reset the
count and found `0x5140DC`. The editor closed without unsaved changes and
the exact source bytes remained. The guest receipts are
`mac-hex-narrow-physical-234e2d32.json` and
`mac-hex-narrow-one-x-final-234e2d32.json` under `FileCatValidation`;
the display wrapper restored Retina after the 1× test. A follow-up Retina
run at exactly 720 × 500 opened Find with Ctrl+F and refocused it after
search navigation using a guarded native CGEvent chord; the AX focused element
was "Find in file". System Events' synthetic Ctrl+F alone left AX focus on
the Hex status after navigation, while the native chord worked without any
FileCat code change. That receipt is `mac-hex-narrow-compiled-ctrl-234e2d32.json`.
The later signed `f4467625` build put the visited-match count into the Hex
cursor's accessible value and passed a complete muted VoiceOver journey at
720 × 500. Commit `20df90d0` moved the count to the start of that value so
it is heard before the long byte row. The exact signed `20df90d0` Mac build
(`FileCat.dll` SHA-256
`b238976cc0f24a35f6965fa014289c925761c74b6fa55a1e6b3ddf3fdf3dc80f`)
passed `mac-hex-narrow-voiceover-paced-20df90d0.json` at exactly 720 × 500:
VoiceOver spoke one distinct match, two distinct matches, then one after a
changed query. The visible controls and F3/Shift+F3 kept the same exact
distant offsets and no-earlier result, and source bytes stayed unchanged.
The Mac file-list speech baseline also passed on this same signed build
(`mac-voiceover-list-20df-restarted.json`). A preceding shorter-paced run
heard the first and changed-query counts but queued the second behind the
first long row; the native runner now waits for that phrase before checking
the next polite update. The later Windows scaled and spoken Hex results are recorded above.

**21 October 2026 validation:** An isolated Ubuntu Copy with a 65,536 bytes/s
limit exposed a large-buffer rate defect: each callback waited only once for
at most one second, even when the copied buffer required a longer allowance.
The Core callback now waits in bounded one-second slices until the full
cumulative allowance has elapsed, rechecking cancellation and live limit
changes between slices. Three focused rate tests cover the large-buffer,
limit-change and cancellation cases. The complete Windows Core Release suite
passed 4,432 tests with 150 platform skips, and all three focused rate tests
passed. The Ubuntu desktop subsequently
showed the chosen rate while a real Copy was active. Its preallocated staging
file contained 15,728,640 exact source-prefix bytes and an unwritten zero tail;
the final target was unpublished and a journal existed when the exact test
process was killed. Restart exposed an explicit retained Copy review; opening
and cancelling it left source, staging and journal intact. Reviewed continuation
was then approved and completed a fresh rate-limited Copy. The Ubuntu native
runner verified the unchanged 20 MB source and byte-identical target, removal
of the staging file, a visible Completed result and closed recovery tracking.
The native interruption phase passed again on a fresh fixture and a second
reviewed continuation ended with the same exact-byte result. The current
portable Git build running on Ubuntu then copied a fresh unrestricted 20 MB fixture;
the finished Operations drawer visibly showed
`Average transfer speed: 187 MB/s` with matching source and target hashes.
The complete local Release App
suite passed 2,386 tests with 63 platform skips and no failures.
A Windows x64/ARM64 CI failure in the
Places overflow test also exposed a host drive-count assumption. The test now
accepts a pinned place through the overflow menu if it still cannot fit at
the restored width; focused Places tests pass all three cases.

**Mac interrupted Move, 21 October 2026:** The signed validation app Cut both
exact fixture files and reached an existing-target conflict with no file
mutation. The runner killed only that FileCat process, restarted its exact
fresh data profile and found the retained journal, destination and explicit
Continue move review. Opening the review preserved both trees and showed
Cancel and Continue. Pressing Cancel then crashed the older Mac build in
Avalonia's native `raiseLiveRegionChanged` while FileCat cleared the recovery
action status. Keeping the row's final status nonempty made native Cancel
pass, but approving Continue exposed the same native exception when the
completed journal row disappeared. Recovery announcements now live on a
stable Operations drawer control with a nonempty accessible name; the row's
status remains visible without owning a live region. All 16 focused
interrupted-transfer UI cases pass, including Cancel and row removal checks.
A new signed Git build repeated the whole Mac journey: a fresh two-file owned
Cut published both exact URLs, held the conflict without mutation, wrote one
journal and survived a process kill and restart. The review retained the
exact destination and explicit Cancel/Continue choices. Cancel kept both
trees and the journal unchanged. Reviewed Continue applied conservative Skip:
`a.txt` remained at both locations, `b.txt` moved byte-exactly, the live app
reported one done and one skipped, and recovery tracking closed. The runner
restored its exact fixture and retired its clipboard publication. Both older
crashed profiles and journals remain preserved as validation evidence.

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
both sources remained unchanged and `b.txt` was excluded. Other Mac pointer,
scaling and viewer checks remain open.
With the owner-approved capture helper, a later Mac Copy-review check resized
the same isolated decision form to 640 × 480 and 980 × 680. The reviewed
`b.txt` size, complete destination input and summary, Cancel and Copy
remained exposed; screenshots show the full path selected in its text field,
the field expanding with the window, and the actions visible in both sizes.
Cancel preserved both source files and every existing destination entry/byte.
Other dialogs, long mappings, scaling and spoken review remain open.
A second macOS Accessibility journey opened `a.txt` in the Text viewer,
revealed Find, replaced the query through keyboard input, and pressed Next.
The visible status reported `1 match`; Previous and Next were both exposed.
The viewer closed without changing the exact source bytes. This establishes a
basic Mac Text search route; Page and spoken journeys remain.
A third macOS Accessibility journey clicked `a.txt` to establish a real pointer
anchor, pressed Shift+Down and verified that the six-byte file was marked,
moved Up without changing the mark, then pressed Shift+Down to clear it.
The current-item label and mark summary showed the expected focus and count
after every step, and both source files retained their exact bytes. The
repeatable `mac-ax-selection-keyboard.py` runs each native input and observation
in a separate AppleScript process to allow the app event loop to settle.
The same pointer/Shift journey passed at a native 760 × 600 main-window size,
with both panels, focused `b.txt`, the zero-mark summary and horizontal column
access visible in the owner-approved capture. The narrow runner restored the
main window to its sampled normal size. Other Mac tab, sort, scaling and
assistive-technology routes remain open.
A further macOS Text viewer run used a temporary, isolated source with two
`NEEDLE` matches more than 4 MiB apart. Native Next reached byte offsets
1,065,000 and 5,325,013; Previous returned to the first, then reported no
earlier match. A later unlocked-session run also sent native F3 and Shift+F3;
the visible offsets moved from the first to the second match and back. The
accessibility tree showed `2 matches` while the search
fixture bytes stayed unchanged. Replacing the query with `first` moved Next to
that marker and changed the visible count to `1 match`, without carrying the
old two-match status. The repeatable test removes its own fixture
after closing the viewer and refreshes the panel. Other source modes, changed
queries outside Text, cancellation and Ubuntu input remain open.

<a id="evidence-unix-markdown-20261026"></a>
The Mac now has a dedicated, locally signed `FileCatCapture` helper launched
through the logged-in desktop. After the owner explicitly approved its separate
direct-capture prompt, it produced a 2880 × 1800 screenshot of the FileCat
two-panel window without a permission overlay. The helper's Screen Recording
toggle is enabled. Two extra diagnostic grants were removed, leaving the
pre-existing grants and this dedicated helper. Direct SSH `screencapture`
remains denied. On a fresh, locked, self-contained and signed Mac ARM64 build
from `a968d412` (executable SHA-256
`b7ad4dfaeba9e13ccd0e86722afa6c954fc88ed372358dfc54e346892edc7ba8`),
the approved helper captured Markdown Page at 760 × 620 and 1,220 × 700.
After dismissing an unrelated Apple account notification, the unobstructed
captures showed introductory prose changing from three lines to two and
table columns expanding and reflowing within the viewer. The runner checked
unchanged source bytes and removed only its temporary Markdown file. The same
build's Copy review retained the full destination, summary and
Cancel/Queue/Copy controls at 640 × 480 and 980 × 680; the visually inspected
captures show the footer reachable at both sizes. Cancel restored the review
size and preserved the exact source and destination fixture. Other Mac viewer,
dialog and scaled-layout inspections remain open.
A native Markdown Page search exposed a macOS stack overflow on typing into
the Find field. The title observer had installed a runtime KVO subclass of the
WebView; forwarding unhandled keys to the superclass of the runtime class
re-entered FileCat's own override. The key handlers now forward from the
declared FileCat WebView subclass to WKWebView. A fresh signed macOS build
passed the Markdown test twice with Command+A/Delete and typed input, two
Next actions, Previous, F3 and Shift+F3, accessible controls, a truthful
directional lower bound and exact unchanged Markdown bytes. Mac Page
keyboard Copy/Select and other assistive-technology journeys were still open
at that point.
Native Mac acceptance later found Command+A and Command+C left Page content
unselected and a test-owned clipboard sentinel unchanged. FileCat has no native
Edit menu for AppKit to resolve those standard key equivalents. The Mac Page
engine now dispatches exact Command+A/Command+C to the WKWebView's native
`selectAll:`/`copy:` actions after FileCat's own page shortcuts decline them.
The signed ARM64 candidate passed a first-open native Page test: the screenshot
shows the rendered heading, paragraph and table visibly selected, and Copy
returned all four expected rendered-text markers (130 characters), without
returning Markdown markup or changing the 159-byte source. The isolated
clipboard was seeded with a unique test sentinel before the shortcut. The
native Markdown Find/Next/Previous/F3/Shift+F3 regression passed again, as
did nine related Release App tests with 16 workers. Other focus and spoken
routes remain open.
The full local Release App run on the Page-key fix finished with 2,369 passes,
63 platform skips and one Create Link preview assertion failure after 19 minutes.
The earlier Windows CI run for `de8e5b54` showed the same assertion. The
confirmation button is polled separately from the asynchronous preview; the
test now waits for the reviewed hard-link text as well as button availability.
The focused two-case set passed six consecutive runs. Final-head CI remains
the validation gate.
On the logged-in Ubuntu VMware desktop, a separate AT-SPI Text viewer journey
passed twice. It focused `a.txt`, opened the viewer, entered `alpha` through
the accessible Find field, and observed the visible `1 match` count and the
no-earlier-match result after Previous. The runner closed only the exact
FileCat-owned viewer window and confirmed both source files retained their
bytes. A fresh isolated Ubuntu Text viewer run on Git-built `a97e1a37`
then used uinput physical F3 and Shift+F3, observed the correct no-later and
no-earlier directional messages while `1 match` remained visible, closed its
owned viewer and app, and retained both source files byte-exactly. Other
source-mode keyboard routes remain open.
A separate Ubuntu AT-SPI Markdown Page journey also passed twice. It opened a
temporary, exact source, searched rendered text with two Next actions and
Previous, and WebKitGTK exposed the complete `2 matches` count. The runner
checked source bytes, closed the owned viewer and removed only its test file.
An earlier targeted AT-SPI/X11 synthetic F3 attempt had not changed the
viewer's visible search state in this Wayland desktop session. A fresh
Git-built `a97e1a37` app and self-owned Ubuntu fixture then cleared the Page
Find count before each real uinput F3 and Shift+F3 event. Each shortcut
produced a fresh rendered-page result and exact `2 matches` status. The
runner closed the viewer and app and preserved both adjacent source files;
its executable SHA-256 was
`e9dea1f1cac00a910cd70baeda9480c88e8c753eeaff29f5a996dc6130311be1`.
The same isolated Ubuntu Git build then switched to Info and Hex;
query changes retired the previous count before physical F3 and
Shift+F3 each produced a new visible match count in those modes. The viewer
and test app closed with both source files byte-exact. Scaled and spoken
input remain open.
The preceding `537ccb0b` CI run exposed two asynchronous test races. Its
Windows recovery test dispatched a synthetic click while the action was still
disabled after Cancel; the test now waits for the owned reservation to end and
the button to enable, as physical input must. Its ARM64 damaged-picture test
allowed 10 seconds for a worker with a 30-second decode bound; the test now
waits through that bound with a visible-state diagnostic on timeout. Both
focused Release App tests pass locally. The complete local Release App suite
then passed 2,370 cases with 63 platform skips and no failures using up to 16
build workers. Subsequent CI remains the validation gate.
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
FCUX-018 now also has a genuine process-interruption test for a dependent
rename inverse. A separate Core worker completes a three-file cyclic rename,
admits its Undo, moves the first inverse item into the temporary name, and
holds there. The parent kills only that worker, restarts journal inspection,
and requires the Undo journal's durable `undoOf` and open rename intent. The
recovery review finds the actual temporary name. Because its intended target
is occupied, finishing review restores that item to its pre-Undo name,
reports the occupied target, leaves all three pre-Undo file bytes exact,
removes the temporary name, and closes the reviewed journal. The focused
child-process cases passed 2/2, including the app's actual journal layout.
The complete Windows Core Release suite passed 4,437, skipped 150 and failed
0 with 16 available processors. A native FileCat window close/restart during
a held inverse remains open.
On the Windows 11 VM, the Git-built `006e06ea` app (SHA-256
`9E9EFE08293CF386DCB84AE5BFE650FC8F030EAD76B2E08CC7C41BFA79CAFB08`)
also passed the repeatable `win-ax-dependent-undo-crash.ps1` journey. It
launched the Core worker under a fresh isolated app-layout profile, killed
only that worker at its held inverse rename, and opened the retained Undo row
in the actual FileCat UI. Cancel left the journal and the exact `C`, `B` and
temporary `A` bytes untouched. Approval restored the temporary `A` to
`b.txt` because `a.txt` was occupied; `a.txt=C`, `b.txt=A`, `c.txt=B` and no
temporary name remained. Two recorded temporary paths had never existed at
the interruption point, so the journal correctly stayed open until a second
explicit Mark reviewed decision closed it without changing those bytes.
Native UI Automation verified the full source/target paths, recovery counts,
the occupied-name warning and the retained report. The JSON receipt is in
the guest's `AppData\Local\FileCatValidation\win-undo-last.json`; all three
decision gates passed. The review, report and idle status now describe
unverified recorded paths accurately instead of claiming absent temporary
items were kept. The affected App UI tests passed 38/38.
The active-close policy now keeps the main window alive if a native inverse
call is still running after Stop them and exit's ten-second wait. It requests
stop, shows that FileCat will close after the current safe step, and uses the
existing idle-close route when the job actually retires. A headless App UI
test holds the real `DeleteFile` inverse call beyond the deadline, verifies
the window and job remain active, then releases the call and verifies that
the window closes only after the inverse finishes. The focused test passed
1/1. Native Windows close/restart during a held call remains to be checked.
The delayed exit path also re-enters the ordinary close guard when work
finishes. A Hex editor opened and changed during the wait now presents its
unsaved-edits decision; choosing Keep FileCat open preserves the editor and
original file bytes. The one-shot exit intent retires before this check, so
canceling the new prompt cannot cause a later job completion to close the
window unexpectedly. Both affected close-policy UI regressions passed 2/2.
The complete Windows Release App suite on the `d702adcc` product code then
passed 2,407 tests with 63 platform skips and zero failures using up to
16 processors. The same turn's complete Core Release suite passed 4,437
tests with 150 skips and zero failures. Later native-runner/documentation
commits do not change the app binaries; final-head CI remains pending.
The retained Undo report now has a focused App UI test with 70 outcomes and a
failure at action 46, beyond the abbreviated preview. The displayed full-path
failure, one-failed summary and Copy all text agree. The Core 70-action
execution test also moves its actual failed and guarded-kept actions beyond
row 30. The related Release suites passed 9 App cases (one platform skip) and
14 Core cases with 16 workers. Native failure and mixed-result inspection
remain open.
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
On the signed Mac `20df90d0` build, `mac-ax-viewer-checksum.py` passed a fresh
five-byte source and empty destination (`mac-checksum-scope-20df90d0-r12.json`,
DLL SHA-256 `b238976cc0f24a35f6965fa014289c925761c74b6fa55a1e6b3ddf3fdf3dc80f`).
Native Hex selection at offset zero produced the exact one-byte SHA-256 and
CRC-32; clearing the selection produced the exact whole-file hashes. Each
result included the correct scope, actual/expected byte count, source and
revision evidence. Compute left a clipboard sentinel untouched; the explicit
Copy action published the complete result in each scope. The runner restored
the prior clipboard, verified every source byte and the empty destination,
and retained native panel screenshots with the receipt. Mac incomplete-source,
mid-read mutation and scaled layout checks remain open. A second fresh Mac
fixture passed the same selected-byte and whole-file journeys in a measured
720 × 500 viewer (`mac-checksum-scope-narrow-20df90d0-r1.json`): the scope,
buttons, hashes, byte counts and complete selectable result remained in view.
A third fixture changed one source byte after scope review. The Mac viewer
refused the stale scope visibly, published no hash, kept Copy disabled and
left the clipboard and destination untouched; the runner restored the source
(`mac-checksum-stale-20df90d0-r2.json`).
The same 720 × 500 selected-byte and whole-file journeys then passed at the
supported Mac 1× display mode, with both complete hashes, explicit clipboard
Copy, source and destination invariants and the visible panel in bounds
(`mac-checksum-narrow-1x-20df90d0-r1.json`). The display wrapper restored the
original 1440 × 900 logical / 2880 × 1800 pixel Retina mode afterward.

**24 October 2026, Mac viewer layout:** The signed `faf9b566` candidate passed
`mac-ax-viewer-mode-layout.py` on a test-owned 9,040-byte text file. Its Text,
Hex and Info modes each resized to approximately 720 × 500 and 1,220 × 700
logical points at Retina 2×; six native captures in
`mac-viewer-mode-layout-pointerfix-r8.json` were visually inspected. Text
wrapped the long line in the narrow viewer and used the wider region when
expanded. Hex retained its byte and character columns with a horizontal
scrollbar when needed. Info kept the report and mode controls within the
window at both sizes. The runner verified the exact viewer title and
unchanged source hash, then removed only its own fixture.

The signed candidate then passed `mac-ax-viewer-media-layout.py` at Retina
2× on its own 640 × 480 PNG and a byte-exact Mach-O copy. Picture and
structured Info both reached 720 × 500 and 1,220 × 700 logical points;
`mac-viewer-media-layout-pointerfix-r8.json` records four native captures,
the signed DLL SHA-256, exact viewer titles, unchanged source bytes and
fixture cleanup. Visual inspection confirmed the complete fitted picture,
in-bounds mode controls, readable Mach-O header and libraries, and scrollable
report at both sizes. The runner focuses each AX listing row before using
FileCat's View action, so it does not launch Preview. The Mac reported no
Preview process after the final Retina run. The same Picture and structured
Info route passed again on the supported 1× 1,920 × 1,200 mode
(`mac-viewer-media-layout-pointerfix-1x-r2.json`); four captures were
inspected and the wrapper restored the original 1,440 × 900 logical /
2,880 × 1,800 pixel Retina mode. The shared AX focus-and-View path also
replaced the old row-invoke path in the Text/Hex/Info runner. Those three
modes passed all six narrow/wide captures at 1×
(`mac-viewer-mode-layout-pointerfix-1x-r2.json`), with wrapped Text, aligned
Hex byte/character columns, readable unstructured Info, unchanged source
bytes, and the original Retina mode restored. Other dialogs and their
scaled layouts remain open.
FCUX-007 passed twice on fresh guest-local Windows fixtures: the native Text
viewer opened a 13 MiB file at its end, and successive Previous button
actions revealed matches near 8 MiB and 1 MiB, then reported no earlier
match. The visible line offsets and unchanged source markers were checked.
A separate 13 MiB UTF-16LE/BOM fixture passed the same native Previous
journey: matches near 8 MiB and 1 MiB, then a complete no-earlier-match
status, with both encoded markers and the destination unchanged. The focused
Core backward-search and App viewer-search suites passed 26 and 25 cases,
respectively, using 16 workers. Linux native editor, cancellation,
changed-query source modes and provider-native routes remain open.

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
<a id="evidence-windows-markdown-20261026"></a>
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

<a id="evidence-settings-picker-20261026"></a>
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
On macOS, `mac-ax-markdown-resize.py` opened an isolated Markdown fixture in
native Page mode, asserted actual viewer dimensions of 760 × 620 and
1,220 × 700, and captured each state with the owner-approved helper. Visual
inspection showed the introductory prose reflow from three lines to two and
the four-column table expand to the available article width. The concluding
prose also used the wider width. The runner closed the viewer, confirmed the
exact source bytes and removed only its fixture. Scaling and assistive
technology checks remain open.

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

The interrupted-operation row now refuses to offer a prefilled new transfer
when a recorded source or destination belongs to a nonlocal provider. Its
details direct the user to select the sources and destination again while
retaining cleanup and report access. Local legacy journals still offer a new
review without repeating that instruction in the row. The two remote-source
and remote-destination journal cases and the related 16-case App UI suite pass.
The Windows 11 VM also passed both native unavailable-SFTP cases using the
Git-built isolated app. Each showed reselection guidance and opened its
retained report without Continue or prefilled new-transfer actions; its
non-continuable journal, local sentinel bytes and empty local destination
remained unchanged. The native runner validates the exact app executable and
fixture paths before interacting. This verifies the unsupported-provider
refusal on Windows, not a live supported-provider transfer.
A live Windows-to-Ubuntu SFTP source case now covers the unavailable-intent
path with a real provider. The Ubuntu guest served a 16 MiB `slow.bin` whose
SHA-256 was recorded before the transfer; FileCat first copied a small probe
byte-exactly through the same connection. A rate-limited Copy was terminated
after 32,755 bytes reached an unpublished `.fc-*.tmp` file and its durable
journal recorded the remote source, local destination and provider limitation.
After restart, `sftp-live-recovery-native` passed: the actual interrupted row
required reselection, offered neither Continue nor a prefilled new transfer,
opened its retained report, and preserved the staged marker, local probe and
unpublished target. The scoped cleanup confirmation then deleted that exact
partial, closed recovery tracking and saved a reviewed report. The repeatable
`sftp-live-verify` passed the closed-journal, removed-stage, retained-report,
unchanged-probe and unpublished-target checks. Ubuntu rehashed the original
`slow.bin` afterward; it still matched
`ee815a856e58c1cb389ae562dae630cc507417f6f2bb5bce8c017bc81b0de34f`.
The first immediate post-kill runner verdict was false for the marker and
journal scan; both were confirmed after restart. A second isolated live
fixture (`c827fa2e58fa4a08be67ca1fe5dc0215`) passed the bounded retry:
the native runner terminated only its exact FileCat process after 32,755
staged bytes, then verified the copied marker, retained non-continuable
journal, unpublished target and intact probe. After restart, the recovery
UI, retained report and scoped cleanup confirmation passed again. The
corrected cleanup assertion verified journal closure, removal of the exact
staged file, and a retained reviewed report. The repeatable post-cleanup
verifier also passed. Ubuntu rehashed the second source after cleanup at
`7b581edd11963d6045c7c1a7a1f0a6b5b74d02d2bdc8cb49a1af964c02f3614a`,
matching its initial manifest. The first run had copied the probe through
FileCat; the repeat seeded the same 60-byte marker locally to focus on the
post-kill and cleanup regressions.
The prior `cb9f329b` source passed producer, Windows, Ubuntu, macOS and
Windows ARM64 CI in run `39583832661`; `152101b5` passed the same five
lanes in run `39597252968`.
An interrupted upload to a remote destination may leave a hidden server-side
temporary file that the local recovery reviewer cannot inspect. The cleanup
decision now names that limit, retains the journal and report after any
approved local cleanup, and directs the user to inspect the remote folder
before deliberately marking the recovery reviewed. The operation row and
retained report carry the same warning. A headless UI regression creates an
unfinished remote-upload intent, checks the highlighted decision and saved
report, and confirms that neither cleanup nor a canceled Mark reviewed
action closes its journal; the 17-case focused suite passes. A genuine
Windows-to-Ubuntu SFTP destination upload then started from the Git-built
Windows app at 0.0625 MB/s. The exact FileCat process was terminated after
13,422,990 of 16,777,216 source bytes reached the hidden server file
`.fc-1c94acc9-1`. The file retained the expected source marker and checksum
`f1a5e35292ce0cb81aa808ef31313399afe12f81d8409551cb5768bc1117988a`;
the public target was absent. The durable journal recorded the remote
destination and refused equivalent continuation. On restart, the native
Windows recovery row showed the server-file warning and withheld Continue.
`sftp-upload-cleanup-native` passed the exact-path confirmation, retained
report, open journal and unchanged source checks. Ubuntu then verified the
staged file's resolved path, owner, size, marker and unpublished target before
removing only that test file. `sftp-upload-mark-native` passed the final
exact-target warning, explicit reviewed closure, retained report and source
hash check (`52bcb92e1c590a22aac219ce97a85d977bd0a8e820a7358c4850c5f1f10c2526`).
The source and destination SFTP interruption paths are now exercised on live
providers. Other supported failure/cancellation paths and scaled/spoken
acceptance remain open.

FCUX-001 now also has a real FTP cancellation regression. `FtpJobTests` starts
the local pyftpdlib server, rate-limits an 8 MiB upload, waits for nonzero
bytes under the server's hidden `.fc-` stage, and cancels the active job. It
requires a canceled result, no public target, no remaining stage and an
unchanged source hash. The focused case passes, as does the complete Remote
suite with 2,371 passed and 156 platform/lab skips. Live FTP process
interruption, changed server state and other failure/cleanup choices remain
for native acceptance.

A further six real-server regressions now drop the connection during an
8 MiB rate-limited FTP, explicit-TLS FTPS or implicit-TLS FTPS Move, wait for
the job's Retry/Skip/Cancel decision, then choose Cancel or Skip. Cancel
requires Canceled; Skip on the only item requires Failed with a skipped-step
issue. All six require the byte-exact local source, no published target, the
one partial server stage, and an issue naming that retained stage. They exposed a
FluentFTP null dereference when cleanup tried to list through its closed
control socket; `FtpChannel` now translates that specific disconnected
library failure into a connection-loss error. The focused cases pass 6/6.
The complete Windows Remote Release suite passed 2,405, skipped 161 and
failed 0 with 16 test workers.

The remaining Retry choice has a real-server regression for each of those
three protocols. It kills the server during a 2 MiB rate-limited Move, checks
the exact local source and unpublished target while the decision is pending,
restarts the server on its original port with the same partial stage, and
chooses Retry. All three cases passed: the job reconnects, publishes the
byte-exact complete target, removes its temporary stage, and only then
removes the local source. The complete Windows Remote Release suite passed
2,408, skipped 161 and failed 0 with 16 test workers. Other failure-cleanup
paths and native provider acceptance remain open.

A separate real-server failure cleanup test changes the local Move source's
modified time while a rate-limited hidden upload is underway. On FTP,
explicit-TLS FTPS and implicit-TLS FTPS, the job reaches the source-change
decision, Skip closes it as a failed single-item job, and the original
source bytes remain exact. All three focused cases passed with no published
target or temporary server file. The complete Windows Remote Release suite
passed 2,411, skipped 161 and failed 0 with 16 test workers. Other provider
failure routes and native acceptance remain open.

An isolated child-process regression now covers the FTP crash boundary in
both Copy and Move. The parent starts its own loopback pyftpdlib server and
8 MiB source, waits for a nonzero but incomplete `.fc-` upload, kills only
the child process it started, and scans the actual journal on fresh startup.
Both variants require the exact source hash, no public target, a partial
server stage tied to that journal ID, and an open recovery record; Move also
requires its durable `upload-move` intent. This verifies that a crash cannot
publish a partial target or silently consume the source. The server stage is
not treated as a local cleanup candidate. The user-facing recovery still
keeps the remote journal open for inspection because FileCat has not proved
it can safely delete a remote object after the crash. Focused cases passed
2/2; the complete Windows Remote Release suite passed 2,396, skipped 157,
failed 0 with 16 available processors. Native remote-stage review and safe
cleanup remain open.

The interrupted-upload row, cleanup decision, stop-reminders decision and
retained recovery report now show the candidate remote stage-name prefix
derived from a well-formed journal ID (for example `.fc-deadbeef-`). The
wording says this is a search hint and requires server inspection before any
change; it never authorizes deletion. Malformed or legacy journal names get
the generic warning without an invented prefix. The focused App recovery
and report suite passed 20/20 with no skips. The complete Windows App Release
suite passed 2,404, skipped 63 and failed 0 with 16 available processors.
The Win11 VMware guest cloned exact Git commit `3eb78253`, used SDK 10.0.101
in an isolated validation folder, and built the solution without warnings.
`win-ax-remote-stage-hint.ps1` then opened a fresh interrupted remote Copy in
that Git-built executable (SHA-256
`0FFB1E3A06798D7D4C30C574F0FADF6FBE86DA704631EC354770717C6D271A68`).
The native UI Automation row and approval both showed candidate prefix
`.fc-239b7fee-`; its retained report showed the same prefix. The source SHA-256
remained `900AF33156F0336FCB33D047DA4F68A0C5822F8FAAE789D86CCF26801AF44098`,
the local destination stayed empty, and the journal remained open. The runner
changed no server file. Native spoken review and safe remote-stage cleanup
remain open.

FCUX-001 also guards a normal remote Replace approval against a destination
that changes during a long upload. The approved entry's kind, size and modified
time are captured before upload; publication obtains a fresh listing and
refuses a changed or disappeared entry before deletion or rename. Copy and Move
both discard the unpublished stage, keep the source and explain that the
current destination needs review. Seven controlled FTP/SFTP variants cover
unchanged and changed versions, disappearance, links and directories. A real
pyftpdlib FTP Move changed the target while 1 MiB was uploading; the finished
job failed without deleting the source, the newer target bytes remained and
the stage was removed. The same real-server journey now also passes with the
target changed to different bytes of the same length and its original modified
time restored; the source and changed target hashes remain exact and no stage
remains. The complete Remote Release suite passed 2,379 tests
with 156 platform/lab skips. The remote publication guard now also retains a
SHA-256 fingerprint of an ordinary file selected for Replace and checks it
again before publication. External edit preflight hashes the current server
file, so unchanged size and timestamp with different bytes raises the
edit-conflict review. The approved hash is carried into the job, checked before
uploading and again before publication. Controlled SFTP, FTP and FTPS cases
exercise same-size/same-timestamp changes before and during upload, exact
source and target bytes, and staged cleanup; the focused Remote and App edit
suites passed 75 and 39 cases respectively, and the complete Remote Release
suite passed 2,392 with 156 platform/lab skips; the complete Core Release suite
passed 4,435 with 150 skips. These checks cannot be an atomic
server-side compare-and-swap: a competing writer after the last check is
still possible. A live Ubuntu OpenSSH run from Git head `3ddf9429`
rate-limited a 4 MiB Move into an owned folder, waited for a nonempty hidden
stage, then used a second SFTP connection to atomically replace the target
with different bytes at the same size and modified time. The job refused
publication, kept byte-exact source and changed target, removed the stage,
reported the content conflict and cleaned the isolated folder. The focused
guest test passed one case with no skips. A second Ubuntu guest run from
`7a4d63a5` used a disposable local pyftpdlib server for plain FTP and
explicit-TLS FTPS, each with Copy and Move. During a rate-limited upload,
another connection replaced the destination with different bytes while
retaining its original size and modified time. All four real-server cases
refused publication, kept the exact source and newer destination bytes and
removed their hidden stages; the focused guest run passed four with no skips.
The complete Windows Remote Release suite passed 2,394 with 157 platform/lab
skips after this addition.

The remote Copy/Move form now warns when FTP/SFTP and Replace or Replace if newer
are selected: another app can change the destination after FileCat's final
check, and Keep both: rename the copy avoids replacing that destination. The
warning follows async destination resolution, clears when the user changes to
a local destination or a non-replacing policy, and is exposed as the conflict
control's accessible help text. A running job's FTP/SFTP conflict review shows
the same warning before the decision buttons. Three focused headless App cases
cover remote/local form choices, both destination classes in the per-item
review, and unchanged source/target bytes after Skip. The complete local
Windows App Release suite then passed 2,410 tests, skipped 63 platform cases
and failed 0 with 16 processors in 18 minutes 52 seconds. This informs the
choice; it does not turn FTP/SFTP publication into an atomic compare-and-swap. The
provider failure, interruption and final-check race gates remain open.

The live upload exposed a Copy-form error under Czech locale: a decimal speed
was rejected, then the already closed dialog's controls were reused and
Avalonia reported that a control already had a parent. Transfer option
validation now runs before closing the dialog, keeps an assertive inline
error beside invalid options, accepts local and invariant decimal notation,
and rejects nonfinite, negative, overflowing or positive sub-byte limits.
The offending filter or speed field now carries the same message as accessible
help text while it is invalid, then clears that help on correction. The same
form preserves every choice through correction. The focused App run covers
invalid filter and speed correction, Copy and Queue, field help, and the
interrupted-upload warning/retention cases.

A separately signed macOS arm64 build of `836bd04d` verified the field help
through native AX: submitting `-1` kept the form open and exposed “Speed limit:
enter a finite, nonnegative number of MB/s.” as both visible text and help on
the focused speed field. Changing the same field to `0.0625` removed both;
Cancel created no target and left the pre-existing destination byte-exact.
Actual VoiceOver speech and localized desktop keyboard routes remain open.

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
<a id="evidence-undo-report-20261026"></a>
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
<a id="evidence-clear-history-20261020"></a>
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
originals. Linux native clipboard exchange and Windows preferred/performed
effects remain open.

<a id="evidence-original-desktop-clipboard-20261020"></a>
The macOS guest completed a second FCUX-069 handoff with actual Finder and
the native pasteboard. A clean FileCat profile focused only `b.txt` with no
marks, then Cut published exactly that file URL. Finder selected and copied
`c.txt`; the pasteboard generation advanced and its complete file-URL list
changed to exactly `c.txt`. FileCat Paste copied `c.txt` to the destination with
the exact `67616d6d610a` bytes. The obsolete Cut source `b.txt`, the Finder
source, and both pre-existing `a.txt` files retained their bytes. A second run
passed on the original signed build after temporary diagnostic logging was
removed. The repeatable `mac-ax-clipboard-external-copy.py` requires this
exact manifest and cleans its test-owned `c.txt` and clipboard state. Native
Move effects, delayed reads and Linux exchange remain open.

The Mac native runner then repeated the handoff with **the same** `b.txt`
file URL on both sides of the publication change. The Finder Copy advanced
the native pasteboard generation without changing its file path, and FileCat
Pasted the exact five-byte file into the target as Copy. The original `b.txt`
source survived. This specifically verifies that path equality does not
resurrect the obsolete Cut intent.

A fresh Mac profile also completed the **owned** Cut route. FileCat Cut
published only `b.txt`; FileCat Paste then moved that file into the target
with the exact `626574610a` bytes and removed the source. Both unselected
`a.txt` files remained byte-identical. The repeatable
`mac-ax-clipboard-owned-move.py` checks the native publication generation,
source removal, target bytes and the post-completion state, then restores the
isolated fixtures. That first run did not exercise repeated Paste or
interrupted effects.

The Mac's completed-Move replay initially refused safely but reported
“Some clipboard files could not be read” because the move had removed the
source before the native clipboard could resolve it. `FileClipboard.ReadAsync`
now checks a still-owned, already finished Cut before native file conversion,
revalidating ownership and publication revision first. The second Paste gives
the precise “already been moved” explanation; a later external Copy retains
its own authority. Three regression tests cover completed and partial moves
whose old native payload resolves incompletely, and a later external Copy
of a new file at the same path. The focused clipboard suite passed 42 cases
with one platform skip; the complete Release App suite passed 2,380 cases
with 63 platform skips. The revised Mac native runner moved
`b.txt`, Pastes again, found the explanation in Recent notifications and
confirmed that the exact source/target bytes were unchanged by that replay.
Mac partial Move replay is now covered too. A native two-file Cut published
the exact `a.txt` and `b.txt` URLs. FileCat waited on the existing target
`a.txt` conflict; choosing Skip retained that target and source byte for byte
while moving only `b.txt` into the destination. A second Paste refused the
same publication with the accessible “previous move fully completed 1 of 2
items” explanation and changed neither file tree. The repeatable
`mac-ax-clipboard-partial-move.py` passed three times on the signed ARM64 build,
restoring its exact fixture and retiring its test clipboard each time.
Interrupted Move acceptance remains open.

A further signed Mac run chose Cancel operation at that same conflict. Both
source files and the existing destination stayed byte exact, and a second
Paste refused the original Cut with a 0-of-2 completion explanation. The
runner's Skip route passed again after adding the cancel variant. On the
Windows 11 VMware guest, a fresh Explorer Ctrl+X published exactly the
isolated `a.txt` path in `CF_HDROP` with `Preferred DropEffect = 2` (Move).
FileCat Paste created exact destination bytes and retained the source as a
Copy, because the external publication cannot authorize FileCat to remove
it. The shell's `Performed DropEffect` remained absent after Paste; that
handoff and Explorer's subsequent Cut presentation need further acceptance.
The native runner now records both shell effects and tests the exact
source/target result. The isolated FileCat process closed gracefully.

The Windows shell handoff is now completed for a whole external Copy. After
FileCat verifies that the job fully completed, a bounded background STA
rechecks the same clipboard generation, the exact `CF_HDROP` path set and
Explorer's Move preference before reporting `DROPEFFECT_COPY` through the
current OLE data object's `IDataObject::SetData`. It never reports Move or
asks Explorer to delete-on-paste. A read-only native VM check found Copy in
Explorer's OLE object after FileCat Paste, with the original source and the
new target byte exact. A fresh existing-target conflict was cancelled in its
native review; both files and clipboard generation stayed unchanged, and
Explorer's OLE object had no performed effect. The focused clipboard suite
passed 43 cases with one platform skip. The complete Release App run passed
2,383 cases with 63 platform skips and zero failures across 2,446 cases
using 16 test workers. After the callback-order and bounded-log adjustment,
the final Release build had zero warnings, its focused clipboard suite again
passed 43 with one skip, and a second fresh Windows VM Explorer Cut → FileCat
Paste → read-only OLE check passed on that final build.

Ubuntu's two-file owned Cut also passed a native Cancel operation at the
existing `a.txt` conflict. Its original source and destination bytes stayed
unchanged; a second Paste explained that zero of two roots completed and
refused to replay the Cut. The AT-SPI/uinput runner cleared Commander marks
and retained its exact fixture. Its Skip route passed again after the
runner extension, preserving the earlier one-of-two result.

Ubuntu's dedicated FileCatValidation instance then reached the existing
`a.txt` conflict during an owned two-file Move and was killed by exact
executable, profile and panel arguments. Neither source nor target bytes
changed. Reopening the same profile exposed one interrupted Move with the
exact destination and an explicit Continue action. Its review retained two
source roots, zero finished steps, the destination and Cancel/Continue
controls. Cancel closed the review without changing either tree. The
four-phase AT-SPI/uinput runner records these native results. On 21 October,
the same guest explicitly approved Continue move with its retained Skip
policy. It kept the pre-existing `a.txt` and its source copy, moved `b.txt`
with exact bytes, reported one done and one skipped, and closed recovery
tracking. The final native AT-SPI check confirmed both exact directory trees.
Interruption during active data copying remains open.

On the signed Mac ARM64 `a968d412` build, a separate, retained two-file Cut
published exact file URLs. The exact FileCat process was killed at a held
`a.txt` conflict after a durable journal existed; source and destination
bytes had not changed. Restarting the same profile showed the retained Move
destination and an explicit Continue action. The review displayed both
Cancel and Continue; Cancel kept the journal and exact bytes. A second
reviewed approval kept the conflicting `a.txt` at both ends, moved only
`b.txt` byte-exactly, showed one done/one skipped, and closed recovery
tracking. The original journal ended as Interrupted after continuation and
the continuation journal ended as CompletedWithIssues. The runner restored
only its test-owned source and destination fixture and retired its clipboard
publication. Other active-copy interruption and external Move effects remain
separate acceptance gates.

UXBUG-013 responds to the reported panel header with only a few Places icons
despite unused width. Compact mode now prioritizes common and pinned places,
then fills the available row with the rest; narrow panels still expose the
remainder through More. Three focused Release tests pass for priority,
wide/narrow/wide reflow, pinning and overflow. A Windows 11 VMware guest using
the current Git-built app passed native normal/narrow/wide/restored panel
widths: 10, 6, 11 and 10 visible Places controls respectively, including
More. The narrow row retained More for overflow; the wide row gained Phones
and cameras. Native pinned-place interaction, display-scale and spoken
acceptance remain open.

UXBUG-014 retains a completed operation's average speed in the drawer and
includes it in Open/Copy/Save report. It divides actual copied or read bytes
by monotonic elapsed time since work began, including pauses, decisions and
verification, but excluding queue time and logical credit for skipped bytes
or same-volume renames. Zero-data operations say the rate is not applicable
in the report. Thirty-eight affected Release tests pass, including a real
verified Copy, same-volume Move, skipped Copy, stable finished timing and
rate-format boundaries. Native scaling and spoken presentation remain open.

On 21 October 2026, the Windows 11 guest completed a FileCat-owned Copy from
an isolated fixture. The source survived, destination bytes matched exactly,
and the finished Operations drawer exposed `503,6 bytes/s on average` in the
guest locale. Both Copy report and Open report contained the same actual-data
average; their report bodies matched after excluding their separate capture
timestamps. The complete App Release suite passed 2,386 cases with 63 platform
skips and no failures using 16 workers. The complete Core Release suite passed
4,429 cases with 150 platform skips and no failures. The guest's native Save
report picker also wrote a local report with the same average, 30 actual
processed bytes and one completed item; source and target bytes remained exact.
Scaled layout and spoken acceptance remain open.

The selected finished operation's on-screen details now say `Average transfer
speed: 587,4 bytes/s` in the Windows guest, using the same calculated rate as
Copy/Open report. A second fresh FileCat-owned Copy kept source and target
bytes exact; native UI Automation found the visible details label and matching
report bodies. Twelve focused operation-report/progress tests pass for the
explicit finished label. The prior Save report check remains valid because
its export content is unchanged.

The signed Mac validation app completed a fresh one-file Copy after clearing
stale test marks. Its selected finished Operations details exposed the
accessible `Average transfer speed: 113,8 bytes/s` label. Narrowing the main
window to 760 × 600 kept the identical label; the script restored the original
window size and exact source/destination bytes, removing only its test Copy.
The native runner guards against stale marks and wrong app/panel identity.

The first exploratory Mac partial-Move run also exposed a FileCat process
crash in Avalonia's native live-region bridge while a notification changed.
The native exception attempted to build an announcement with a nil name.
Main-window notifications now always provide a nonempty accessible name that
includes their severity, and only the currently visible notification region
is live. A headless regression covers idle, info, same-text error and
dismissal transitions; all six focused notification tests passed. The signed
Mac build then completed the same conflict and replay journey without a
process exit. The complete Release App rerun passed 2,382 cases with 63
platform skips and zero failures using 16 workers.

<a id="evidence-ubuntu-clipboard-20261020"></a>
The Ubuntu VMware guest also passed a repeatable native owned-Move test.
`linux-atspi-clipboard-owned-move.py` focused only `b.txt` through AT-SPI,
sent physical Enter events from a short-lived guest virtual keyboard, and
used FileCat command search for Cut and Paste. The visible Cut announcement
named one item. Paste moved the exact `626574610a` bytes from source to
destination, removed only source `b.txt`, preserved both `a.txt` files and
restored the isolated fixture after checking it. A GNOME Settings dialog had
initially intercepted native input; closing it let the same keyboard route
work. A second repeatable Ubuntu runner,
`linux-atspi-clipboard-external-copy.py`, passed twice. After FileCat Cut
`b.txt`, Ubuntu Files selected and Copied that same path using native
Home/Right/Ctrl+C input. FileCat Paste reported Copy, kept the exact source
bytes, wrote only `626574610a` at the target, preserved both `a.txt` files,
restored the test fixture and closed its Files window. This verifies that a
newer external publication owns the same URL without reviving FileCat's
earlier Cut. A second two-run variant used Ubuntu Files Cut on that same path;
FileCat again imported Copy, kept the source, wrote exact target bytes and
restored the fixture. Two further twice-passing Ubuntu variants Cut both
`a.txt` and `b.txt` in FileCat, then replaced that publication with Files
Copy or Cut of only `b.txt`. Paste copied only `b.txt`, preserved both source
files and the pre-existing target `a.txt`, then cleared test marks and
restored the fixture. `linux-atspi-clipboard-partial-move.py` then passed
twice on the logged-in Ubuntu desktop. It Cut both exact source files,
reviewed the existing target `a.txt` conflict, chose Skip, and found only
`b.txt` moved with exact bytes. A second Paste exposed the precise “previous
move fully completed 1 of 2 items” refusal and changed neither tree. Both
runs restored source, target and Commander marks. Delayed Paste timing and active data-copy interruption still need native checks.

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
and nonexecuting previews. The distinct current Settings boundary is
[AC-S2-036](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-036). UXBUG-001,
the reported Markdown Page table-width bug, is fixed: tables use available
viewer width and scroll within their own keyboard-focusable region on narrow
windows. Dated Windows, Ubuntu and Mac native Page geometry routes passed;
only [AC-S2-MD](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-md) remains.

Markdown Page search is implemented as UXBUG-002 with the text viewer's
find/next/previous/stop controls. Windows WebView2, Ubuntu WebKitGTK and
macOS WKWebView native search routes passed at their recorded builds. The
distinct scale/accessibility case is
[AC-S2-MD](UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-md).

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

The Windows CI run on `5e7b04ec` exposed two test setup races in the held
link-preview cases: the Create footer was attached before the scrollable
preview joined the visual tree. The tests now wait for that attachment after a
layout pass. All 49 `MutationPreparationTests` pass locally. The modal command
ownership change also passed the complete Windows App suite locally: 2,396
passed with 63 platform skips.

FCUX-004 now also has a repeatable native Windows UI Automation check in
`tests/FileCat.NativeAcceptance/win-ax-modal-guard.ps1`. A fresh self-contained
`c32fc422` guest build opened Copy for an isolated `a.txt`. UI Automation
successfully invoked the obscured toolbar Copy and Delete and key-bar Copy and
Delete actions; each left exactly one Copy review with its destination visible.
Cancel closed the review, no target appeared, and the source SHA-256 remained
`713e5c5a199714f532fa8992ee58eb3dde1fd2c2a351f8dd53bc37d1e576c395`.
The runner records the exact app hash, fixture and four invocation outcomes in
its guest-local JSON report. This closes the Windows background Copy/Delete
accessibility route; other modal focus, scaling and spoken routes remain open.

The matching Ubuntu AT-SPI runner, `linux-atspi-modal-guard.py`, passed twice
against a Git-built `a97e1a37` x64 app in the logged-in guest. AT-SPI invoked
all four obscured toolbar/key-bar Copy/Delete actions while one Copy review
showed the captured destination. Each left the same single review; Cancel
removed it, no target appeared, and source SHA-256 stayed
`713e5c5a199714f532fa8992ee58eb3dde1fd2c2a351f8dd53bc37d1e576c395`.
The second run used a new isolated fixture and streamed the app hash from disk.
Other modal focus, scaling and spoken acceptance remains open.

**22 October 2026, queued nested-dialog focus ownership:** Two new
`DialogFocusOwnershipTests` close an inner modal before its posted focus
restoration runs. Opening a newer prompt leaves focus in that prompt and the
outer layer disabled; deliberately focusing the outer Cancel button instead
keeps that focus and does not restore the old text field. The direct Release
xUnit runner passed all 10 cases in this class with 16 available workers.
Native timing, scaling and spoken dialog focus routes remain open.

The macOS CI lane for `49b023dd` exposed a checksum-result registration race:
a queued check canceled inside `JobAdded` could finish before its verification
group and source tab were attached. Job submission now accepts a caller
registration callback before `JobAdded` or the first scheduling pass. A job
paused at admission also schedules when resumed. Two Core cancellation-order
cases and a queued-pause/resume case pass, as do all 10 focused verification UI
cases on Windows. The complete Windows Core Release suite passes 4,435 tests
with 150 platform/lab skips. In a Git checkout on the arm64 Mac, the formerly
failing case passes, all 10 verification UI cases pass, and 27 Core job
retirement/registration cases pass using the pinned `10.0.101` SDK installed
under the Mac validation directory. The complete macOS Release App suite on
the same Git codebase passed 2,331 tests with 128 platform/lab skips and no
failures using up to 16 build workers. Full final-head CI remains pending.

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

The acceptance wording in the historical rows below records the boundary
known when each row was written. It is not a parallel task list. Use the
[current ledger](UX_UI_ACCEPTANCE_LEDGER.md) for finite remaining scenarios,
including later evidence that closed an older note.

| Audit finding | Implemented behavior | Regression coverage | Historical acceptance note; current status in ledger |
| --- | --- | --- | --- |
| FCUX-001 — interrupted transfer intent | Journal a versioned, bounded snapshot of the destination, source mappings, resolved filter definitions, layout/name, verification, preservation, rate and queue choices. Continue shows that scope and uses conservative Skip. Legacy/incomplete records require a newly reviewed transfer. Existing source/staging/content guards remain in force. Filtered same-volume folder moves cannot bypass traversal through a rename shortcut. | `RecoverableTransferIntentTests`, `RecoveryTransferConflictTests`, `InterruptedTransferIntentUiTests`, existing `Interrupted*` tests, `MaskTests`. A Windows 11 guest resumed a journal-seeded filtered NTFS move, a genuinely interrupted Core worker, and an actually terminated FileCat desktop process. The desktop copy's retained staging contained a nonzero content marker, its journal retained the rate, and Continue reproduced all 32 MiB while cleaning staging. Two fresh guest-local C:-to-R: NTFS copies passed the same real interruption and recovery checks; the disposable VHD was removed afterward. A guest legacy journal refused equivalent continuation; Cancel retained its source/journal and an explicitly reviewed new copy used the chosen mapping. Ubuntu then killed an exact FileCat-owned Move at a held conflict, restarted the same profile, reviewed Continue move and completed with conservative Skip: a.txt stayed at both source and target, b.txt moved byte-exactly, the UI showed one done/one skipped, and recovery tracking closed. Ubuntu also killed an exact FileCat process during a rate-limited 20 MB Copy after verifying 15,728,640 exact written-prefix bytes in a preallocated staging file, an unpublished target and a durable journal. Restart exposed retained intent, destination and an explicit review. Cancel kept the staging and journal; reviewed Continue made a byte-identical target, removed staging, showed Completed and closed recovery tracking. A signed Mac ARM64 app also survived exact-process interruption of a rate-limited 4 MiB Copy after a 1 MiB written prefix in preallocated staging; restart retained the journal and destination, Cancel preserved bytes, and reviewed Continue produced exact source/target SHA-256, removed staging, showed Completed and closed recovery tracking. A child-process FTP Copy/Move test killed a live partial upload and verified the unchanged source, unpublished target, journal identity, and retained remote stage across restart. A native Win11 loopback FTP Copy/Move crash journey verified the real stage hint in the recovery row, confirmation and report; an exact FTP prefix inspection and single-stage deletion preceded the explicit reviewed-journal closure. | Live SFTP source and destination interruption and reviewed cleanup are verified above. Other provider failure/cancellation paths and the server-side final-check race remain open; large or unsupported records deliberately cannot promise equivalent continuation. |
| FCUX-069 — clipboard operation authority | Cut belongs to the current exact FileCat clipboard publication and complete ordinal resource identities. External/unverifiable publications import as Copy. Running/completed/partial moves cannot replay the original Cut; later publications retain their own lifecycle. On Windows, an Explorer file drop that Avalonia advertises with one extra wrapper is accepted only after its converted paths agree with a bounded native `CF_HDROP` list and the clipboard sequence stays stable. | `ClipboardIntentTests`: controlled asynchronous publications, incomplete/mismatched native manifests, changing sequence, owned-Cut refusal and actual source/destination manifests. A Windows 11 VM completed both Cut A → Explorer Copy A and Cut A+B → Explorer Copy A, then FileCat Paste; exact native manifests, source survival, selected destination bytes and exclusion of B passed. A fresh Explorer Cut published `Preferred DropEffect = 2` and its exact `CF_HDROP` path; FileCat retained the source and copied byte-exact target data. Before the shell-feedback fix, `Performed DropEffect` was absent after Paste. With the fix, Explorer's OLE data object reports Copy after the same exact whole-copy route; a cancelled conflict reports no performed effect and preserves both files. The Mac VM Cut `b.txt`, Finder Copied `c.txt`, then FileCat Pasted exactly `c.txt`; a second Mac run Finder Copied that same `b.txt` URL and FileCat Pasted it as Copy. A third Mac run kept the owned Cut and moved b.txt with exact bytes while preserving unselected files. After the source disappeared, a second Paste gave the explicit already-moved refusal without replay. A Mac two-file Cut later skipped existing a.txt, moved only b.txt, and refused replay with 1-of-2 completion; its native runner passed three times with exact bytes and fixture restoration. A separate Mac conflict Cancel retained both source roots and destination bytes, refused replay with 0-of-2 completion, and restored the fixture; the refactored Skip route also passed. Native pasteboard generations, file URLs and source/target bytes passed on the signed build. The Ubuntu guest native owned Cut/Paste moved exact b.txt bytes, removed its source, preserved both unselected a.txt files and restored its fixture. Ubuntu Files then replaced FileCat one-file and two-file Cuts with its own Copy and Cut of only b.txt; all four native variants passed twice, imported as Copy, preserved the source and untouched a.txt bytes, and restored the fixture. The Ubuntu two-file Cut then skipped the existing target a.txt, moved only b.txt, and refused replay with a 1-of-2 explanation; its AT-SPI/uinput runner passed twice and restored the fixture. A separate Ubuntu conflict Cancel kept both source roots and the target byte exact, refused replay with 0-of-2 completion, cleared marks, and the refactored Skip route passed again. On signed Mac `a968d412`, a two-file Cut was process-interrupted at a held conflict; restart retained its exact destination and journal, Cancel preserved both trees, and explicit Continue moved only b.txt while reporting one done/one skipped. Both journals had terminal records, and the fixture was restored byte-exactly. | Delayed Paste timing, Windows Explorer Cut visual presentation and safe supported external Move/delete-on-paste acceptance remain open. |
| FCUX-070 — delayed Paste destination | Capture destination and invoking tab before clipboard reads. File Paste uses that location or explains refusal; tab closure cancels it. Text navigation also respects newer navigation/Paste intent, including pending provider preparation. | `ClipboardIntentTests`: held reads, navigation, panel/tab movement, closure, source survival and actual request destinations | Native clipboard/backend timing. |
| FCUX-002 — range anchor identity | Bind range anchors to the listing store and item identity, resolve through the current view, and retire unavailable/foreign anchors. Reset gestures on focus/window deactivation. | `FileListSelectionTests`: real key/pointer events across sort, filter, tab and focus transitions. Native Win11 sort, filter, refresh, tab, Ctrl-click, Shift+arrows, held-Shift window deactivation, command-line focus loss and creation ahead of a retained anchor passed on isolated fixtures at 100%, 150% and 200%, with normal and 720 × 540 narrow routes at 100%. Mac pointer/Shift selection, Command-click toggling, sort, filter, tab, deleted-anchor refresh, held-Shift deactivation and command-line focus passed at normal and 760 × 600 narrow widths with exact marks, focus and expected bytes. Mac pointer/Shift passed at 2× Retina and 1×, and the 1× sort, filter, tab, refresh, Command-click and focus-loss routes passed with exact bytes and restoration. Ubuntu physical touch/keyboard sort, filter, tab, deleted-anchor refresh, Ctrl-click, deactivation and command-line focus passed at normal and 720 × 540 narrow widths; physical held-modifier touch also passed at 100%, 150% and 200% GNOME display scale with exact marked identities, bytes and focus. | FCUX-002 acceptance complete; the shared final-head CI gate remains for the audit as a whole. |
| FCUX-003 — hidden marks in rename | Show hidden count and explicit unchecked inclusion control. Capture both scopes, regenerate counter/name mapping when scope changes, and reject stale external-editor results. Preserve marks and the original operation origin across tab navigation. | `BulkRenameScopeTests`, existing `BulkRenameDialogTests`: inclusion/exclusion, counters, navigation, actual filesystem results. A rule-race case changes the mask while the editor confirmation is open, rejects the old editor names and submits only the newer two-file mapping; the related seven-case suite passes with 16 workers. The modal prevents ordinary user input in the underlying rule fields, so that headless case covers the concurrent arrival. The exact Git-built `72641024` Ubuntu app then passed the user-accessible post-editor rule change at normal size and in a measured 720 × 540 app-unit window at 200% scale: stale editor names were retired, both exact source bytes survived under rule-derived names, and the private editor list was removed. Receipts are `ubuntu-rename-rule-72641024.json` and `ubuntu-rename-rule-narrow-72641024.json` under the VM validation folder (DLL SHA-256 `6743d4ff91240941b5b4c78707983a35edeffe0ac6c416c2bfbaadd14087d7bb`). The resizable review keeps common fields and complete preview visible while advanced controls expand on demand. Native Windows excluded/included-hidden rename and both 760 × 560 scope reviews passed with exact files and bytes; hidden inclusion and two-file execution also passed at 100%, 150% and 200% in the Git-built VM app. A requested 640 × 420 owner retained the scope choice, mask and approval controls; expansion kept both mappings visible and submitted both exact file bytes. Mac normal/narrow and Ubuntu 100%/150%/200% narrow external-editor round trips passed with exact filesystem results. Ubuntu 200% captures show both mappings and, after focus, complete wrapped source and destination paths in the revised growing detail. The signed Mac 1× and Retina 2× runs exposed both full-path mappings and passed the editor with exact bytes; the original display mode was restored. Physical Return/Space mapping navigation passed with complete path details and exact renamed bytes on Windows, Ubuntu and Mac. A revised live region spoke both complete path pairs through NVDA 2026.2 (34/34 speech steps) and Ubuntu Orca (39/39 speech steps). | The pinned Mac review now passes actual VoiceOver speech for complete Previous/Next path mappings; its scrolling form controls have nonzero AX bounds on the current signed build; the virtualized preview has no AX row children, while the spoken Previous/Next route exposes every full-path mapping. The separate preview size/validation findings FCUX-038/039 and shared small-dialog layout FCUX-011 are covered below. |
| FCUX-004 — background decisions/focus | Pending decisions remain in Operations with an explicit Review action. They cannot stack over unfinished user input. Skip is the safe initial operation-decision action; consequential generic confirmations focus Cancel. Recovery move, cleanup, and newly selected replacement policies use that safe default. Dialog focus/Enter callbacks belong to the current session; canceled/resolved decisions retire their dialog. Main-window command dispatch now ignores accessibility activation of obscured toolbar/key-bar actions while a modal review owns the window; scoped commands from a separate Find window retain their own route. Find's queued startup focus also yields to an active dialog, newer focus, deactivation or closure. | `BackgroundDecisionTests`, `DialogFocusOwnershipTests`, `FindInitialFocusTests`, recovery confirmation tests, existing `OverlayDialogLifetimeTests`, `FindRemainingScopeTests`, operation scope/center tests. The Mac Copy-option run exposed a background AX Copy action opening a second review; `TransferDestinationValidationTests` now reproduces main-command activation during an open Copy form and proves Copy/Delete cannot stack or change files. All 11 focused transfer-dialog cases and the complete Windows App suite (2,396 passed, 63 platform skips) pass. A separately signed Mac build of `02ca4b75` then kept exactly one Copy review open after native AX activation of both obscured Copy and Delete commands. Cancel closed it; the target remained absent, the source `b.txt` SHA-256 stayed `f2c82decdd7181cf98945929a62598db7e6b477e11f6e0eb0ae97020eff151ad`, and the pre-existing destination `a.txt` stayed byte-exact. | Other modal focus, scaling and spoken acceptance on Windows, Ubuntu and macOS remains open; Windows background Copy/Delete accessibility actions passed. |
| FCUX-005 — synchronization identity | Keep separate complete, selectable, wrapping Source and Target fields, full accessible identities, and target-specific Mirror wording synchronized with direction. Reflow choices in narrow windows. The consequential Mirror warning announces its complete target when it changes. | `SyncEndpointTests`: same-name endpoints, both directions, long paths, narrow layout, polite warning and actual submit direction; `linux-atspi-sync-endpoints.py` passed on Git build `015eac1c` at Ubuntu 200%, including 1280 × 1000 physical narrow geometry; `win-ax-sync-endpoints.ps1` passed on Git build `1840791f` at Windows 100%, 150% and 200%, using 640 × 600 logical narrow geometry; `mac-ax-sync-endpoints.py` passed on signed Git build `7072b2e2` at Retina 2× and 640 × 600 logical points. All checked direction reversal, Mirror target and untouched fixture. Git-built `559a67a2` passed five actual NVDA and five Orca speech-log checks for both complete endpoints before/after reversal and the complete Mirror target, plus eight Core and six App overlap, revision, collision, recycling and review tests. | Mac VoiceOver speaks both complete endpoints before and after reversal, then automatically announces the complete Mirror target after selector close. The native 640 by 600 review and Cancel passed on the final signed candidate. Require the shared final-head CI gate; S2 plan-choice/group-cancellation lifecycle remains separate. |
| FCUX-018 — tracked Undo | Reserve one immutable inverse review atomically. Filesystem inverses become typed Undo jobs through normal scheduling/activity/close handling, with complete retained per-action reports, irreversible remainder, safe cancellation boundaries and unresolved rename journals. Registry inverses share the exclusive approval boundary and retain their guarded Registry job/issue path. Original-history removal cannot orphan a running inverse. The review's selectable full-path rows wrap, reflow and emphasize entire pathnames with the theme color. | `UndoLifecycleTests`, `UndoSafetyTests`, `UndoLifecycleUiTests`, existing bulk rename/link/recovery tests. Native Win11 FileCat Move → Cancel → approved Undo restored exact source bytes and identity, with a reflowing full-path UIA row. Another fixture reopened the inverse's retained report and copied its complete text. A third verified Recycle Bin review, Cancel, byte-for-byte restoration and complete report copy. A fourth verified Rename Undo with full path mapping, Cancel and exact restoration. Fresh fixtures verified created-empty-folder and zero-byte-file inverses with wrapped full paths, Cancel and safe removal; the folder case passed again after the shared runner extension. A changed created-file fixture was kept with exact bytes and a retained report naming the full path and reason. A native hard-link inverse kept shared data on Cancel and removed only the created name on approval, with both full paths visible. A radio-group regression test prevents stale symbolic-link preview after choosing Hard link. A changed-original native hard-link inverse kept both names and distinct bytes with a retained report naming the full path and identity reason. Native symbolic-link inverse removed only the link; a changed-link fixture kept both distinct files with a retained one-kept report and full path. Native junction inverse removed only the junction; a replacement-folder fixture kept both distinct directories and reported one kept action with full path. Dependent rename interruption and journal recovery passed a native Windows VM journey. `win-ax-undo-mixed-report.ps1` verified 42 retained full-path outcomes, including 40 restored, one failed and one safely kept, with twelve complete lines beyond report row 30. `win-ax-undo-active-close.ps1` verified native keep/cancel with 5,000 complete outcomes and native stop/exit/restart with a closed journal and exact split inventory. Ubuntu `linux-atspi-undo-narrow.py` passed a real Move/Undo at 200% in a 720 × 540 app-unit review with wrapped complete paths, in-bounds decisions, exact restored bytes and a copied complete retained report at 600 × 460 app units. Windows native `win-ax-undo-mixed-report.ps1` passed 150% and 200% with 42 full-path outcomes and in-bounds decisions and Copy all at both scales. Mac `mac-ax-undo-narrow.py` passed 720 × 540 review with highlighted full-path AX row, in-bounds Cancel and approval, exact Cancel/Undo bytes, and complete Copy all from a 600 × 460 retained report on signed Git build `8c833c35`. Ubuntu `linux-atspi-undo-narrow.py --orca-review-speech` passed actual Orca speech of both complete inverse paths and exposed the full copied report through AT-SPI. Win11 `win-ax-undo-mixed-report.ps1 -NvdaPath` passed actual NVDA speech of both complete inverse paths alongside the 42-outcome retained report. The complete Release App suite passed 2,452 cases, 63 platform skips, 0 failures with 16 workers. | Native Orca, NVDA and VoiceOver speak both complete paths through the visible retained-report line navigator on commit `44f246a2`; final-head CI remains open. |
| FCUX-006 — Settings data preservation | Preserve untouched structured tool definitions. Share a lossless argument/field grammar with explicit empty tokens and quote errors. Keep all launch properties, exclusion masks, comment-marker names and complete submenu structures. Invalid edits stay in the same draft with field/line feedback; protected profiles are inspectable without edits or theme side effects. | `ToolTextRoundTripTests`, `ToolAssociationTests`, user command tests, `SettingsPreservationTests`, existing `SettingsDialogTests`; tests inspect the actual non-executing launch plan and persisted settings | A structured tool editor (FCUX-036) remains a separate S2 improvement. |
| FCUX-007 — Previous text search | Search the full declared preceding range using bounded reverse windows, encoding-aware context/overlap and strict progress. Continuation belongs to the query/source/encoding/mode and active content surface; a visible Stop action cancels owned work. | `ContentSearchBackwardTests`, `ViewerSearchScopeTests`, viewer/editor search lifetime tests: distant/chunk-boundary matches, UTF-16, malformed sequences, overlaps and caller ownership; native 13 MiB UTF-8 and UTF-16LE Previous journeys passed on Windows; Mac Text Find, Next, Previous, F3 and Shift+F3 passed with two matches more than 4 MiB apart, exact visible offsets, `2 matches` status and a changed-query `1 match` state. A signed Mac Hex-mode run found two matches 4,260,013 bytes apart at visible offsets 1,065,005 and 5,325,018; Previous/Next and F3/Shift+F3 returned to exact offsets, the status showed 2 matches then 1 for a changed query, and the test-owned source bytes remained exact. A signed Mac Info-mode run searched a test-owned Mach-O copy, showed 1 match for Mach-O and the no-earlier Previous result, then 2 matches for a changed Load commands query; the copied executable bytes stayed exact. A native Windows Hex editor run opened a test-owned 13 MiB file, found matches at 0x100014 and 0x800014 through F3, returned to 0x100014 through Shift+F3, showed complete no-earlier-match feedback, and found a changed query at 0x80001B. The editor closed, all source bytes matched, and the owned fixture was removed. A signed native Mac Hex editor run found NEEDLE at 0x104028 and 0x5140D5, over 4 MiB apart; F3 and Shift+F3 moved between them, no-earlier feedback was visible, and a changed query found second at 0x5140DC. The editor closed, the exact source bytes remained and the test-owned file was removed. New visible Next/Previous buttons and a bounded visited-match status then passed on Windows and Mac; a native Ubuntu AT-SPI/uinput run reached matches at 0x100014 and 0x600014, showed two visited matches, reset to one for a changed query, closed the editor and preserved exact source bytes. A second Ubuntu guest run used physical F3 and Shift+F3 to traverse the same distant matches and reach the no-earlier feedback; it preserved the fixture and closed the editor. A separate Ubuntu Text viewer run used physical F3 and Shift+F3 on the one-match fixture, observed the no-later/no-earlier messages and retained `1 match`, and preserved exact source bytes while closing the owned viewer and app. | Local Text/Hex Stop and tested query/count routes pass at the dated builds below; remote cancellation/query ownership/provider error is the finite remaining scenario AC-S1-007. The viewer's match count had a fixed accessible name and was never heard; it speaks only its outcome since `eed454ba`, and Orca, NVDA and the macOS AX journey heard "1 match" alone on Git-built `35763947`. Ubuntu Hex count and controls passed natively at 200% in both the default and a measured 720 × 500 app-unit window on Git-built `72641024`; Windows Hex pointer and backward search passed at 200% from the original startup route (`pointer-hex-200-r9.json`); measured narrow 200% Windows Hex layout and first-click pointer route passed (`pointer-hex-narrow-200-r12.json`); Ubuntu 150% narrow Text Find, Hex/Text Stop and checksum layout passed on exact Git head 9a500635; those dated runs do not establish remote provider cancellation; see AC-S1-007; Windows NVDA spoke all three Hex count transitions on native 100%, 150% and 200% journeys (`nvda-hex-r2.json`, `nvda-hex-r5.json`, `nvda-hex-r4.json`), and Ubuntu Orca did so on the 200% native Hex route (`orca-hex-scaled-r5.json`); Mac spoken Find passed at 720 x 500 on signed 20df90d0 with all three actual VoiceOver counts; the Mac visible route passed at 720 × 500 at 1x and 721 × 501 at Retina 2x on `234e2d32`. |
| FCUX-008 — checksums | Review explicit whole-file or selected-byte scope, including one byte; retain complete selectable hashes and actual/expected counts. Copy is explicit. Stop, short reads, failures and revision evidence remain visible without overwriting the clipboard. | `ViewerChecksumScopeTests`, existing checksum lifetime/revision tests: scope, immutable request, narrow results, explicit Copy, cancellation, failures and revision changes; native one-byte/Copy journey passed twice and at 600 × 420 logical pixels on Windows at 100%, 150% and 200%, and deliberate whole-file/Copy, 768 MiB Stop, stale-scope refusal and active-read source mutation journeys passed on Windows. The signed Mac `20df90d0` build passed exact one-byte and whole-file hashes, byte counts, scope and revision, explicit Copy only, and unchanged fixture bytes at default and 720 × 500 sizes in both Retina 2× and supported 1× modes (`mac-checksum-scope-20df90d0-r12.json`, `mac-checksum-scope-narrow-20df90d0-r1.json`, `mac-checksum-narrow-1x-20df90d0-r1.json`); a separate changed-source fixture visibly refused its stale scope and kept the clipboard and destination untouched (`mac-checksum-stale-20df90d0-r2.json`). | Native incomplete sources and mid-read mutation on other supported providers and desktops, and other narrow states beyond the validated Ubuntu 150% journey; sources without strong revision evidence remain explicitly qualified. |
| FCUX-009 — concealed comparison text | Replace trimmed primary cells with a virtualized aligned text surface, synchronized horizontal scrolling and optional wrapping with matched pair heights. Difference navigation reveals the actual changed glyph. Complete lines up to the explicit shaping budget of 16,384 UTF-16 code units appear in the primary surface; exceptional lines have an explicit full-line action into paired, selectable bounded parts. Retire source associations, queued reveal ownership and text layouts on replacement/close. Keep exact equality and comparison computation independent. | `CompareTextViewTests`, `CompareLineDetailsTests`, `ComparisonReplacementRetirementTests`, existing comparison equality/source-change/navigation/lifetime cases. The width/length matrix checks the actual glyph viewport, not just source strings. Windows VM `compare-native` verifies both complete 2,004/2,005-character source values, second-difference navigation, source-faithful Copy and a wrapped selected row intersecting the viewport; a captured desktop view shows both differing suffixes in the primary surface. Mac `mac-ax-compare-long-lines.py` passed native Ctrl+I, F8, both complete Copy line results and visually revealed suffixes at 1200 × 788 and 640 × 600, including aligned Wrap long lines at 640 × 600 in Retina 2× and supported 1× modes (`mac-compare-long-20df90d0-r2.json`, `mac-compare-long-narrow-20df90d0-r3.json`, `mac-compare-long-narrow-wrap-20df90d0-r1.json`, `mac-compare-long-wrap-1x-20df90d0-r1.json`). | Native font fallback, pointer selection and spoken automation on supported desktops, plus Linux acceptance, remain open. Full-document selection/search remains a larger content-surface target; complete source lines remain inspectable and selectable through their detail action. |
| FCUX-010 — file list accessibility | Provide bounded virtual item peers with stable identity, focus versus mark semantics, selection/action/scroll/bounds support and coherent cached events. Give each cached item a lightweight attached control owner so Avalonia's Windows bridge can convert its logical rectangle to screen coordinates; detach that owner when its peer retires. Provide a complete paged marked-item review, including hidden/offscreen marks and original locations for result-set items, and avoid synchronous metadata I/O in accessibility queries. Every window's root peer reports the focused row as the native focused element and raises focus changes for row moves; on Linux the AT-SPI path to that row is attached first and focus is re-announced after activation. | `FileListAutomationTests`, `FileListSelectionTests`, `MarkPagingTests`, `MetadataPeekTests`, updated `AccessibilityTests`, `DescendantFocusAutomationTests`, `PrivateAvaloniaApiTests`; native Windows `accessibility-list` verifies nonzero source-row rectangles and separate mark/focus states on a live two-file fixture; `linux-orca-speech.py`, `win-nvda-speech.ps1` and `mac-ax-focus-speech.py` check spoken or AX focus output. | Windows UI Automation geometry and mark/focus inspection pass in the Win11 VM. Git-built `9479b280`, and again `7da4fc0b`, passed actual Orca 50.2 (Ubuntu) and NVDA 2026.2 (Win11) speech journeys across row moves, panels, a dialog (whose group name NVDA voices on entry since `25711ffc`), window switches and copy notices, and the macOS AX focus and announcement journey VoiceOver relies on. `mac-voiceover-list.py` checks actual VoiceOver speech, unmarked navigation and explicit Space mark/unmark on the signed Mac build. Its `--actions` route additionally hears F3 open the named `b.txt` viewer, Escape restore spoken `b.txt` row focus, and the unmarked state with unchanged fixture bytes (`mac-voiceover-list-actions-20df90d0-r3.json`). Other VoiceOver item/action routes, Orca's repeated tutorial description, Other supported provider types remain open. Large selection uses the explicitly explained paged review rather than constructing unbounded native peers. |

### Related corrections included

FCUX-034 validation-session preservation and FCUX-035 protected-profile wording/editability are addressed in Settings because they are part of the safe-save path. The clipboard and drag branches of FCUX-071 both refuse incomplete mixed-provider export. The S2 findings have their own coverage below.

<a id="s2-continuation"></a>
## S2 implementation history

| Audit finding | Implemented behavior | Regression coverage and historical acceptance note |
| --- | --- | --- |
| FCUX-011 — dialog reachability | Reserve header/footer space and scroll the body within the actual host client area. Adapt forms, Settings pages, bulk-rename fields and secondary actions without replacing controls or drafts. Reveal the exact focused caret through nested scrolling. Keep complete proposed conflict names in selectable body text, escaping invisible characters through the existing filename display contract. | 21 `DialogViewportTests` at the 640 × 420 DIP minimum and three font sizes; all 173 affected dialog, Settings, operation-scope, focus and lifetime cases pass together. A native Mac Copy review retained full destination, scope and footer actions at 640 × 480 and 980 × 680 and Cancel preserved the fixture. The shared prompt's validation message is an assertive live region and its field's help text, and Enter on a rejected value speaks the reason (`6afb92a0`); Orca, NVDA and the macOS AX journey heard it through Quick filter on Git-built `ad3bf946`. Other native forms, long mappings, scaling/font/input and assistive-technology checks remain open. |
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
| FCUX-028 — filter draft validation | Invalid mask text and caret stay in the focused field while the applied filter remains. A field-adjacent, bounded validation message is linked through automation HelpText and an assertive live region. Repair clears only that error; Escape, blur, tab switch and navigation discard a draft. All-items masks retain unrelated location and listing warnings. `Ctrl+Shift+F` directly focuses and selects the active panel's inline filter, available in command search. | `FilterDraftValidationTests` covers invalid quote, regex and saved reference, correction, Escape, blur, tab switch, accessibility properties, Registry context and a real working-set banner. `PanelFilterKeyboardAccessTests` exercises the actual shortcut on each active panel at 640 × 420, draft retention and Escape restoration; it and the four existing draft cases pass 5/5. The extended Ubuntu Orca physical-key journey passed 42/42 checks on Git-built `ebb0c9c6`, including spoken Filter entry, inline error on Enter, and row restored by Escape. Existing `PathLineTests`, working-set, quick-search, operation, rename and 40 Core mask tests pass; the 27-case related App selection and Release build pass. Mac spoken filter acceptance passed on the signed native build, including error and Escape restoration, with a persistent named live region that avoids the Avalonia.Native nil-name crash. Windows NVDA, Ubuntu Orca and Mac VoiceOver spoke the filter field, exact validation error and restored file row on the Git-built native app; the Windows native 37-step journey passed on b6aa9383. Ubuntu 200% native narrow Filter layout passed on Git head c3a8138f with the full error visible and 42/42 Orca steps. Windows 150%/200% physically measured narrow windows passed the 37-step native NVDA journey, exact error display and fixture checks on b6aa9383; The Mac 1× route passed on 24 October 2026; no Mac 1× Filter rerun remains. |
| FCUX-029 — notification lifetime and review | Publish message, severity, action and repeat count as one state. Info expires after six seconds unless hovered/focused; errors require explicit dismissal. A fixed Copy text/History/Dismiss control row and selectable, height-bounded text support inspection. Equivalent messages within thirty seconds share a count; distinct notices use polite or assertive live regions. Tools and command search open the newest 100 retained groups after dismissal or other commands. | `NotificationCenterTests` covers severity transitions, repeated messages without duplicate live-text changes, stale expiry, focus/hover retention, copy, Escape, action visibility, history, the 100-group bound, and nonempty native live-region names through idle/info/error/dismissal. A signed Mac native conflict and replay journey passed after this fixed a nil-name announcement crash. A 54-case related App selection and full Release build pass with zero warnings. Dialog tests scope their buttons to the overlay so notification controls cannot collide with modal actions. `DescendantFocusAutomationTests` checks every Main window notice, including the first and a repeated one, through the Linux announcement path. Git-built `7da4fc0b` was heard announcing the first and a repeated copy notice by Orca 50.2 (Ubuntu) and NVDA 2026.2 (Win11), and the macOS AX journey observed one `AXAnnouncementRequested` for each. Clipboard and scaled-layout acceptance remain open. |
| FCUX-030 — measured panel geometry | Add-panel availability, execution, drag previews and cramped feedback use the measured workspace host. Unmeasured startup gives an explicit refusal. A compact address row moves free space and filtering beneath the editable location and target role while retaining full role semantics. The 260 × 240 DIP panel minimum leaves at least two list rows in the tested compact layout. Cramped restoration explains the reversible F11 route, including a one-panel warning. | `PanelViewportLayoutTests` covers measured admission with chrome changes, narrow and restored address geometry, F11 recovery and actual list capacity. `DockingTests` and `PanelLayoutTests` protect drag constraints and tree math. The 16-case affected App selection, six Core layout cases and Release build pass. Native 100%, 150% and 200% scaling, screen-reader and pointer acceptance remain open. |
| FCUX-031 — keyboard panel resizing | An explicit Panels → Arrange and command-search action focuses the nearest splitter. Arrows adjust eight DIPs, Shift adjusts 32, Tab chooses another boundary, Enter keeps the proportion, and Escape restores only the live layout snapshot. Pointer and keyboard update the same saved split tree under the same minimum sizes. A temporary strip and accessible splitter name show orientation, neighboring panel numbers and percentage; splitters stay out of normal Tab navigation. | Four `PanelKeyboardResizeTests` cover adjustment, cancel, save/reload, nested orientations, both minimums, stale tree/workspace replacement and Commander keys outside resize mode. `CommandSearchTests` checks discovery; `PanelKeysTests` verifies the menu icon. The 35-case focused layout selection and Release build pass with zero warnings. The complete App suite passes 2,155 cases with 62 platform skips. Windows VM `panel-resize-native` passed twice: command search focused the named splitter, Right moved it eight physical pixels and changed its announced share from 50 to 51 percent, then Escape restored its exact rectangle and list focus. Each boundary is a read-only slider whose value is the first side's share (`daec7d64`); Orca receives its value changes (`8c15f80d`). Git-built `eebb8832` speech runners: NVDA, Orca and the macOS AX journey hear the focused boundary, only the new share (51) for Right, and the restored row after Escape; the full App suite passes 2,509 tests with 71 platform skips. | Nested layouts and scaled-display acceptance remain open; the WinAppDriver `panel-resize-native` runner reads the slider now but was not rerun. |
| FCUX-032 — keyboard ancestor menu | Alt+Up, Navigate and command search open a single standard menu based on the same provider-derived segments as pointer breadcrumbs. Each ancestor offers Open here, Open in new tab and Copy path with its full safe target in the accessible name. The drawn path names the current location and explains Alt+Up/Ctrl+L. Escape returns to the current listing; stale menu actions refuse changed tabs/locations, and closure never restores an old row coordinate. | `BreadcrumbMenuTests` drives Alt+Up, Right/Enter for all three actions, clipboard publication, Escape, stale-action refusal, listing focus, Ctrl+L and the no-ancestor refusal. Existing `PathLineTests`, command search and menu-icon tests pass in a focused selection and the complete App suite passes. Windows VM `breadcrumb-native` passed twice: Alt+Up focused the full-path ancestor, Right exposed all three full-path actions, and Escape returned focus to the file list. | Native spoken screen-reader interaction and non-filesystem provider paths remain open. |
| FCUX-033 — bounded breadcrumb presentation | A long root, leaf or one-part path falls back to a root cue, explicit ellipsis and distinguishing leaf tail. Truncation preserves grapheme clusters and complete safe-display escapes. A gap has no hidden navigation target, and visible link geometry uses only contiguous fragments. Full safe path is available in tooltip and accessible name; exact raw Copy remains. | `PathLineElisionTests` covers one/two/many parts at 120/240/480 DIPs, UNC and provider-style roots, suffix identity, emoji/combining marks, escaped bidi/control characters and mapped hit tests. The existing pointer and keyboard breadcrumb tests pass. Core regression passes 4,413 with 150 platform/fixture skips; complete App regression passes 2,155 with 62 skips. Native typography, screen-reader and scaling acceptance remain open. |
| FCUX-076 — Copy representation and completeness | Separate source text from displayed text, preserve original decoded tabs and logical line endings, and capture explicit selection/visible scope. Show retained selectable evidence for the one-MiB budget, partial/short reads and failures. Serialize owned clipboard publications, reject changed-source evidence and keep cancellation limits truthful. Preserve the report's complete original Copy all route. | `ViewerSourceCopyTests` and source-mapping/geometry cases cover multibyte boundaries, giant-line continuations, control characters, caps, short reads, delayed writes, cancellation, source revisions and explicit scope transitions. Native clipboard interoperability remains open. |
| FCUX-072 — binary comparison byte ranges | Alt+B and a labelled action select an exact left or right source interval from decimal or hexadecimal offset and decimal count. Invalid, overflowing, out-of-range and stale requests keep the draft or retire; aligned gaps never become bytes. A bounded selectable preview and read-only document value expose side, source, offset, extent and copied representation. Copy belongs to its current source/selection, checks available revisions, serializes clipboard publication, and keeps the existing 4 MiB spaced-hex/text contract. | `HexCompareRangeTests` and existing comparison/accessibility tests: 79 Windows cases pass together, including 24 focused comparison cases. Native screen-reader, clipboard and 640 × 320 DIP scaling acceptance remain open. |
| FCUX-073 — viewer semantics | Publish semantic values after the current content state commits. Include source-selection extent, loading/read failures, Hex position/column and actual editing state. Retire old request/source ownership and avoid synchronous source I/O from peer queries. | `ViewerGeometryAndSemanticsTests`, `HexEditorSemanticsTests` and existing accessibility/lifetime coverage. The descriptive value stays read-only rather than promising an unsupported binary SetValue API. Native automation/screen-reader bridges remain open. |
| FCUX-036 — advanced Settings editing | Saved filters, user commands and associations have structured list/detail editors, ordering, item-level validation and lossless Advanced text. Program Browse, argument-token insertion, nonexecuting invocation and first-match previews, Settings search, and keyboard remapping work inside the draft. Every Advanced text editor offers UTF-8 file import/export without committing until OK; import is strict and bounded at 8 MiB, and export atomically replaces a confirmed local target. | `SettingsPreservationTests` covers structured/raw round trips, previews, field errors, search and Cancel. `AdvancedTextFileExchangeTests` covers UTF-8 BOM/invalid bytes, size limits, atomic replacement, cancellation and accessible actions in all three editors. The combined related Settings run passes 30 cases with one platform skip. The Windows 11 VM runner passed the native picker Cancel, spaced executable path, invocation preview, Settings draft restoration, and Tab/Enter/Escape Browse path checks. | Native broader keyboard, scaling and assistive-technology acceptance remain open. |
| FCUX-037 — toolbar availability | Refresh toolbar buttons from the same `GetAvailability` admission used by menus, key bar and command search. Track active-tab history, focus, marks, location and switch state. Disabled buttons expose the current refusal in a hoverable wrapper and automation help text; execution still rechecks admission. | `ToolbarAvailabilityTests` covers active-tab Back/Forward, item and provider changes, key-bar/palette agreement, reason exposure and stale invocation. The related 35-case toolbar, tooltip, key-bar, search and accessibility selection passes. Full App Debug 2,170 passed/62 skipped began before the Page-search and toolbar changes; full Core Release 4,417 passed/150 skipped. | Native screen-reader, pointer tooltip and rapid large-listing/remote-provider acceptance remain open. |
| FCUX-038 — complete bulk-rename review | Bind all accepted planner rows to an explicitly virtualized list. Offer Problems and Changed filters, path search, next-problem navigation and selectable full old/new path details. Previous/Next controls and a position status expose every filtered mapping independently of platform list peers. The reviewed and submitted row arrays are the same. | `BulkRenameReviewTests` exercises 3,001 rows with a blocking collision at row 2,501, full paths, filtering, next-problem and previous/next mapping navigation, bounded realized controls and empty-result feedback. A Mac 760 × 600 native AX journey visited both exact mappings and completed the editor rename with byte-exact results; the capture showed both mapping buttons together above the preview. A second signed Mac journey marked 3,001 files, reached the sole last-row collision through Problems, heard the complete mapping and reason through VoiceOver, and canceled with every source hash unchanged and the target empty. Affected tests passed 56 cases with one Unix-only skip, and the viewport/review selection passed 35 cases at the tested font sizes. | Actual VoiceOver complete-path speech passes in the narrow Mac review. The 50,000-row headless publication stays below five seconds with at most 100 realized rows and directly reaches the last-row collision. Native scaled rename behavior belongs to AC-S2-038; another native 50,000-file run is optional after the bounded 50,000-row headless and 3,001-row native journeys. The Mac virtualized list has no AX row children; the tested, spoken Previous/Next controls provide complete-path review. |
| FCUX-039 — responsive rename validation | Separate pure expansion/plan collision checks from target existence checks. Compute and group candidates off the UI thread; use bounded per-device workers for existence, cancel queued work and ignore superseded generations. Show an accessible checking state, disable Rename until validation, and recheck current targets before job admission. The runner still refuses replacement at execution. | `BulkRenameAsyncPreviewTests` holds a target probe while editing and canceling, verifies a newer preview wins, and makes a target appear between preview and confirmation. Core `BulkRenameTests` covers pure candidates, swaps, checked collisions and cancellation. The related App selection passes 38 with one platform skip; seven focused Core cases pass. | Native slow-network, maximum 50,000-row responsiveness and shutdown timing acceptance remain open. |
| FCUX-040 — exact bulk-rename counter validation | Parse start and step as signed 64-bit integers and digits as 1–12 without fallback or clamping. Show field-adjacent, accessible errors while preserving text; empty intermediate input gets neutral guidance. Negative steps count down, zero repeats the value, and checked planner arithmetic marks overflow as a blocking row problem. | `BulkRenameCounterValidationTests` covers malformed/overflowing start and step, invalid/overflowing digits, blank input, zero/negative steps and exact preview values. Core `BulkRenameTests` covers counter overflow with and without a counter token. Eight focused App and eight Core cases pass. | Native focus, screen-reader announcement and scaled-layout acceptance remain open. The counter errors and summary had fixed accessible names and could not be heard by any reader; they speak their text since `eed454ba`, not yet heard natively. |
| FCUX-041 — transfer destination validation | Show destination errors beside the field and keep the form, options, and scope intact. Start and Queue use the same asynchronous resolver and captured folder/new-name mapping. Device-scheduled filesystem probes cannot block editing or Cancel; stale results cannot authorize submission. | `TransferDestinationValidationTests` covers unsupported providers, retained options, both buttons, a single-item rename mapping, a held probe, correction, and cancellation. Existing operation and interrupted-transfer tests wait for destination validation before submission. Focused cases pass; an earlier quiet complete App attempt was stopped before completion. | Native network path, focus, screen-reader, and execution-time destination-change acceptance remain open. The destination error had a fixed accessible name and could not be heard; it speaks its text since `eed454ba`, and Orca, NVDA and the macOS AX journey each heard "Destination: Enter a destination folder." once when the destination was emptied, with the form still open, on Git-built `68fdea8e`. |
| UXBUG-003 — PE Info scrollbar drag | Throttle thumb-driven seeks to one latest viewport about every 32 ms, retire pending seeks on source or other navigation changes, and cache the report's line count for status updates. | `ViewerGeometryAndSemanticsTests` covers 80 rapid scrollbar values, repeated pointer moves down/up through the thumb, and stale seek retirement. A temporary local headless check against the supplied SignalSetup.exe passed; the sample-specific test is not committed. The 66-case related viewer suite passes. | The user manually confirmed the Windows desktop drag on the supplied EXE works. |
| FCUX-042 — operations drawer layout | Persist a vertically resizable drawer while preserving usable workspace; bound and scroll recovery review with one selected item, keep a complete operation row and Open report action at 640 × 420, wrap controls, and collapse the graph until requested. | `OperationsDrawerLayoutTests` covers 1/5/20 interruptions, keyboard navigation, small/wide windows, a selected operation and report entry, pointer/keyboard resize, saved height and shrink/expand restoration. Nine focused cases pass. | Native high-DPI, pointer, keyboard and screen-reader acceptance remains open. |
| UXBUG-004 — Markdown prose width on resize | Let prose blocks use the article's available width, matching table regions as the Page window changes size; retain article padding and table-local horizontal scrolling. | Forty-eight `MarkdownTests` cases pass. Headless Chrome measured prose and table regions at 505 px in a narrow window and 1,441 px in a wide window, with no page-level horizontal overflow. VMware native pointer tests widened the table by 335 px while prose shortened by 25 px; narrowing the viewer kept its document within the window and the table in a keyboard-focusable horizontal scroll region. Mac native Page captures at 760 × 620 and 1,220 × 700 show the same prose and table expansion with exact source bytes retained. | Native Windows/Mac Markdown resize routes pass; only the distinct scale/accessibility boundary AC-S2-MD remains. |
| FCUX-043 — recovery preparation state | Show Reviewing, Awaiting approval, Applying, and Submitted states on the interrupted row; disable conflicting actions and offer Stop. Stopping before approval closes that review's dialog and preserves the old journal. Stopping after approved cleanup retains completed effects and an open-journal report. A submitted job has independent ownership. | `InterruptedDemandTests` adds six focused cases for held open/read calls, approval, new-transfer form, an already deleted partial, and Stop in the job-admission callback. The complete App Release run passes 2,232 with 63 skips after correcting drop-test timing and the collapsed review section. | Native delayed-device, keyboard/screen-reader status, and shutdown timing acceptance remain open. |
| FCUX-044 — explicit Find scope | Keep local, result-set, and Registry search scopes distinct. Unsupported SFTP, FTP, archive, and virtual locations open Find with no local root, explanatory guidance, and a disabled Find action until the user chooses a local scope. Loaded presets identify their own local scope; result-set snapshots ignore preset folders. | Twelve `FindOriginScopeTests` cases pass across an actual This PC command, unsupported providers, deliberate local choice, saved presets, and result-set scope. The related 47-case Find/Registry selection and complete App Release run pass. | Native provider navigation and assistive announcement acceptance remain open. |
| FCUX-045 — Find completion feedback | Give the completion state and search qualifications separate wrapping lines, keep changing path/counters secondary, and expose the existing log beside the summary. Replace the generic empty-folder text with the search's captured criteria/scope, Edit criteria, and Review search log. Distinguish stopped, excluded, inaccessible, warning, regex timeout, and manually emptied results. | `FindFeedbackTests` covers the state matrix; `FindWindowTests` exercises narrow zero-result layout, keyboard log access, and hiding the last result. The 98-case Release Find selection and complete App Release run pass. | Native narrow-window, keyboard, and screen-reader acceptance remains open. |
| FCUX-046 — Find shortcut contexts | Register Find criteria/results actions with stable IDs and preserve their default keys. Resolve actions through the keymap, show effective gestures in menus and hints, explain panel overlaps and same-context conflicts in Settings, and keep text editing, path suggestions and dialogs ahead of Find dispatch. Find-only actions appear as unavailable outside Find instead of offering a no-op. | New Core keymap and App Find/Settings cases pass. The related 56-case App selection passes; the complete Core Release run passes 4,420 with 150 skips using 16 workers. The complete App Release run passes 2,232 with 63 skips. | Native keyboard and shortcut editor acceptance remains open. |
| FCUX-047 — fractional scroll and one-row paging | Accumulate precision-wheel fractions into whole rows in file list, text, hex and binary comparison; reset them at source/layout/navigation changes and list boundaries. Keep PageUp/PageDown moving with one visible row and retain list rename alignment. | Fifteen focused App cases and 117 nearby viewer/list regressions pass. The complete App Release run passes 2,238 with 63 skips. | Native conventional-wheel, precision touchpad and inertial-input acceptance remains open. |
| UXBUG-005 — viewer window size | Remember a deliberate normal viewer size in profile settings and restore it for text, Markdown, PE Info, hex and picture modes. Clamp invalid or oversized values to the active display; an older untouched viewer cannot overwrite a newer size. Debounce normal geometry and discard any pending sample when state changes, including an early maximized-sized event. | Three `ViewerWindowSizeTests` cases cover validation, screen bounds, reopen across viewer modes, restart persistence, concurrent windows and maximize/restore transitions. Native Windows retained a 1,592 × 1,414 viewer through maximize, restore, close and reopen, with the saved normal dimensions unchanged. | Native multi-monitor and DPI acceptance remains open. |
| UXBUG-006 — stale Streams count after deletion | After confirmed Delete removes a stream or attribute, retire only the parent file's cached `hidden` metadata before refreshing the side-data list. Requery the provider when the parent row appears; partial failures also requery so the count reflects actual remaining data. | `HiddenDataUiTests` primes and reads the Streams/Attributes cell, presses Delete, confirms the side item is gone and file contents survive, then checks the parent row's count decreases by one. The related 105-case App selection has 89 passes and 16 skips. A native Windows Delete-key check removed a temporary NTFS stream on disk, and the parent row's Streams cell was blank immediately after navigation back. | Native Unix attribute acceptance remains open. |
| UXBUG-007 — resizable search dialogs | Registry search, Advanced criteria, and Ignored folders dialogs expose a visible drag grip with keyboard arrows and larger Shift steps. Criteria and results fit the available width; lists gain height when expanded. Host shrink clamps the card and later expansion restores its requested size. | `SearchDialogResizeTests` covers drag events, keyboard resize, host shrink/expand, reopening, Registry results and narrow layout, ignored-folder results, and the Advanced grip. Seventy-two related App cases pass. The complete App Release suite passes 2,242 with 63 skips. Native Windows pointer checks expanded and shrank all three dialogs; Advanced criteria and Ignored folders kept their input reachable by scrolling at minimum size. | Native scaling and assistive-technology acceptance remain open. |
| UXBUG-008 — visible viewer Find results | Text, Hex and Info show a background match count, visible Previous/Next buttons, and a Stop count action. Counting is limited to 64 MiB for local sources or 8 MiB for nonlocal sources and 10,000 hits; partial totals are labelled as lower bounds. Page uses WebKitGTK's rendered-page count, preserving an exact result through directional end-of-page searches and labelling its 1,024-match limit as a lower bound. Windows and macOS retain a truthful directional lower bound until their native Find interfaces can supply a total. Query, source and mode changes retire stale counts. | Seven `ContentSearchCountTests` cases cover overlap, encoding, read, limit and cancellation boundaries. `ViewerFindControlsTests`, `ViewerPageSearchTests` and `LinuxPageEngineTests` cover controls, exact/limited/unknown result states, keyboard parity, source ownership and the native GTK count signal. The focused Windows App selection and Linux native CI integration pass. Native Windows WebView2 showed Find, Previous/Next and the labelled directional lower bound. Native Ubuntu AT-SPI Text search passed twice with a visible `1 match` status, Previous feedback, graceful viewer close and unchanged source bytes. Native Ubuntu Markdown Page search also passed twice with the exact `2 matches` count and unchanged source bytes. A fresh guest-local Page run cleared the count before physical F3 and Shift+F3, observed a new exact `2 matches` result after each key, closed the owned viewer/app and kept the fixture bytes exact. The same isolated viewer then switched to Info and Hex, where physical F3 and Shift+F3 each produced fresh mode-specific match counts after query reset. Native macOS Text showed Find, both navigation buttons and a visible `1 match` status after typed input. | Native macOS Page and distant Text matches passed, including Page Command+A query editing, two Next actions, Previous and the truthful directional lower bound. Recorded native Page/Text/Hex search routes pass; only the distinct scale/accessibility boundary AC-S2-MD remains. An exact Page total on Windows/macOS is an optional engine-dependent refinement; their truthful directional lower bound remains the supported result. |
| UXBUG-009 — Details selects active operation | Reopening the operations drawer selects the current active job when the retained selection has finished, then scrolls the selected row into view. While the drawer stays open, progress refreshes and newly added work preserve a deliberate history selection. | `OperationCenterTests` reproduces eight finished jobs, a scrolled history, a new waiting job, Details click, row visibility, and selection preservation after another job arrives. A second case uses a genuinely Running job after eight completed rows. Both focused cases pass; the earlier 44 related App cases pass. | Native pointer, keyboard, and screen-reader acceptance remain open. |
| UXBUG-010 — highlight consequential recovery confirmations | Put interrupted-transfer choices and consequential effects in separate themed, selectable callouts. Choices identify the action, source availability, destination, one-root name mapping, filter, conflict policy, verification and start mode. The Important effects callout identifies incomplete legacy intent, omitted sources, partial-file deletion, Skip/replace/rename effects, move source removal and unresolved review work when present. An emphasized action and conflict choice, bulleted effects, and a disclosure with visually sectioned complete mapping and safety notes make the full review scannable; Cancel is the default for consequential actions, including a new transfer from an incomplete legacy record. Cleanup retains its complete visible review. | The focused `InterruptedTransferIntentUiTests` and six `DialogViewportTests` cases pass, including move, partial cleanup, legacy new copy, keyboard disclosure and minimum-window font sizes. The Windows 11 VM displayed the revised emphasized action, conflict choice, bulleted amber effects and sectioned full review, with Cancel focused and approval pending; `legacy-highlight` and `legacy-cancel` native assertions pass. | Native scaling and screen-reader announcement of the revised hierarchy remain open. |
| UXBUG-011 — highlight long safety confirmations | Reuse the themed, selectable key-points summary for drive/disk recovery scans and searches. It names the source or amount read, read-only scope, mounted-drive risk, stop action and safe recovery destination while keeping every original detail below. The confirmation to stop recovery reminders now calls out that no files change, where the bounded report remains, and any rename or review-limit warning. Explicit caller-supplied facts avoid guessing which sentence matters; consequential actions keep Cancel as their default. | `DialogFocusOwnershipTests` checks summary order, full selectable detail and safe Enter behavior. `RecoverySafetyTests` verifies the scan caller supplies source and destination facts; `InterruptedRenameTests` verifies its rename warning. Eight focused cases pass with six platform skips using 16 xUnit workers. Three `DialogViewportTests` cases pass at 640 × 420 DIP with 14, 21 and 28 point UI fonts, keeping full details reachable and footer actions visible. | Native scan-dialog layout, scaling and assistive-technology acceptance remain open. |
| UXBUG-012 — macOS Page key dispatch stack overflow | Forward unhandled WKWebView keys from FileCat's declared WebView subclass. macOS title observation can install a KVO runtime subclass; deriving the super dispatch class from that runtime class re-enters FileCat's own key override. Dispatch exact Page Command+A/Command+C through WKWebView's native edit actions so rendered content can be selected and copied even without a native Edit menu. | A native macOS Markdown Page test reproduced a process stack overflow during Find input before the fix. After a locked, self-contained ARM64 publish and ad-hoc signing, the same isolated test passed twice with Command+A/Delete, typed query, two Next actions, Previous, a visible truthful match lower bound and unchanged source bytes. A later signed ARM64 candidate visibly selected and copied rendered Page heading, paragraph and table text using Command+A/C; a test clipboard sentinel was replaced, and the source stayed unchanged. The native Find regression and nine related App cases passed. | Other Page focus routes and assistive-technology acceptance remain open. |
| UXBUG-013 — Places row leaves unused width | In compact mode, prioritize common and pinned destinations, then let the row fill its actual width with every other available place. The » menu keeps overflow reachable at narrow widths; the alternative setting preserves location-menu order. | Three `PlaceBarHierarchyTests` cases cover priority, full place membership, wide-to-narrow-to-wide reflow, overflow access and pinning. The focused Release run passes with zero failures. The Windows 11 VM native row exposed 10, 6, 11 and 10 visible controls across normal, narrow, wide and restored panel widths, retaining More for narrow overflow. | Native pinned-place interaction, scaling and spoken acceptance remain open. |
| UXBUG-014 — average speed missing from finished operation details and report | Keep a monotonic elapsed time from first work through completion and divide actual copied/read bytes by it. The selected finished operation's details explicitly label its average; Open/Copy/Save reports state the same rate, actual processed bytes and denominator, including pauses, decisions and verification but excluding queue time. Zero-data renames and skips say the rate is not applicable. | `OperationReportTests` runs a verified Copy, same-volume Move and skipped Copy, checks the finished details and export, and proves the final rate does not drift. `FormattersTests` covers zero, sub-byte, normal and extreme rates. Thirty-eight focused Release App cases before the label change, 12 focused operation/progress cases after it, the latest complete 2,388-pass/63-skip App suite and the 4,429-pass/150-skip Core suite pass. The Windows 11 VM completed two FileCat Copies with exact destination bytes, an explicitly labelled finished average matching Copy/Open report, and a saved local report with 30 actual processed bytes. A further native run narrowed the finished Operations window from 1,254 × 818 to 746 × 613 pixels; the selected details kept the same 587,4 bytes/s average and the exact original bounds returned. Copy and Open reports matched, with source and target bytes unchanged. The updated portable Git build on Ubuntu completed a fresh 20 MB Copy with exact source/target hashes and displayed Average transfer speed: 187 MB/s in the finished Operations drawer. The signed Mac validation app completed a one-file Copy and exposed Average transfer speed: 113,8 bytes/s in the accessible finished details at normal width and 760 × 600; exact fixture bytes and original window size were restored. | Native display-scale and spoken acceptance remain open. |
| UXBUG-015 — invalid Copy option reused a closed dialog | Validate filter and speed before the Copy or Queue form closes. Keep the same control tree and choices available for correction, with an assertive inline error. Accept current-locale and invariant decimal speeds; reject nonfinite, negative, overflowing and positive sub-byte limits instead of silently changing the requested cap. | `TransferDestinationValidationTests` exercises invalid filter and five invalid speeds, then corrects the same dialog through Queue or Copy under Czech locale. The 27-case focused App selection passes. A real Windows-to-Ubuntu SFTP upload started from the fixed Git build with the dot-decimal 0.0625 MB/s speed and passed the live interruption and recovery checks. A separately signed macOS arm64 build of `50698406` opened Copy for isolated `b.txt`, retained `-1` in the expanded speed field with the visible red inline error and no target, then accepted `0.0625` in the same form and copied the exact five source bytes. Source and target SHA-256 both equal `f2c82decdd7181cf98945929a62598db7e6b477e11f6e0eb0ae97020eff151ad`; the pre-existing destination `a.txt` was untouched. The Ubuntu `linux-atspi-copy-option-correction.py` check on Git-built `a97e1a37` retained the same native Copy review after invalid `-1`, exposed the visible error as AT-SPI help on the speed field, cleared both on correction to `0.0625`, and copied exact source bytes (source and target SHA-256 `0f2f03f5c6eda44f1dd7b96a84502bc0c88b085b0ea3bb6fe7081eae2c5fec81`). The Windows `win-ax-copy-option-correction.ps1` check on Git-built `c32fc422` likewise retained the native Copy review after invalid `-1`, exposed the visible error as UI Automation HelpText, cleared both on correction to `0.0625`, and copied byte-exactly (source and target SHA-256 `0f2f03f5c6eda44f1dd7b96a84502bc0c88b085b0ea3bb6fe7081eae2c5fec81`). The [five-lane CI run](https://github.com/benny-cz/FileCat/actions/runs/39615627563) for `50698406` passed producer, Windows, Ubuntu, macOS and Windows ARM64 jobs. | Localized keyboard and spoken error feedback on all supported desktops remain open. The options error had a fixed accessible name, so no reader could hear it; it speaks its text since `eed454ba`. |
| FCUX-048 — listing background context menu | Right-clicking empty listing space opens location actions without changing Commander marks or the focused row. The menu uses a captured tab and location for Paste and New folder even if another panel becomes active, refuses stale tab navigation, rechecks capabilities, and shows disabled reasons on unsupported locations. The Windows item shell menu remains limited to item requests. | `BackgroundContextMenuTests` exercises empty and nonempty pointer routes, dismissal, menu contents, actual Paste and New folder destinations, another active panel, stale navigation, and disabled location feedback. Five focused and 63 related App cases pass with one platform skip. | Native pointer placement, clipboard interoperability, read-only archive/Registry/working-set/disconnected providers, keyboard route and assistive-technology acceptance remain open. |
| FCUX-049 — live viewer theme changes | TextViewer and HexView own theme subscriptions only while attached. Applying a new palette re-resolves semantic text, background, selection, search and edit brushes in the existing controls. ReportWindow no longer duplicates TextViewer's theme notification. | `ViewerThemeLifecycleTests` switches open controls through Classic, Dark, High Contrast, DOS and back while checking brush resources, selection and position; attach/detach checks verify static-event release. Two focused and 116 related App cases pass. | Native theme transition, system high-contrast, quick-view, editor and screen-reader acceptance remain open. |
| FCUX-050 — Psychedelic effect boundaries | Use opaque Psychedelic menu, panel, card and functional bar colors. Keep movement in the backdrop behind content; remove the overlay that copied and shifted rendered text/picture slices. Focus, grid and border brushes no longer animate. Pause the effect clock with the theme animation preference or inactive app so repainting stays static. | `PsychedelicSurfaceTests` checks functional alpha and menu contrast, samples 13 moving backdrop phases, verifies decoration stays below content and the disabled clock freezes, and switches themes back to Psychedelic. Three focused and 54 related App cases pass. | Native full-cycle menu/icon contrast, transparent picture content, system reduced-motion, window scaling and assistive-technology acceptance remain open. |
| FCUX-051 — desktop motion preference | Query Windows client-area animation, macOS accessibility reduced motion, and GNOME `enable-animations` through a bounded, absolute-path system tool. Probe while an animated theme is active and recheck on reactivation; hold motion still until the first read. The app checkbox off and inactive-window pause always win. Unsupported or failed providers leave the explicit app control authoritative. Settings and capability text state the supported desktops and correct Psychedelic effect description. | `MotionPreferenceTests` covers the policy matrix, GNOME desktop/value parsing, live preference transitions, the disabled/inactive clock and the Windows native API read. Thirty-two related theme and Settings App cases pass. | Native Windows/macOS/GNOME preference toggles, unavailable provider behavior, high contrast and assistive-technology acceptance remain open. The poll can lag a change by one interval plus its bounded provider read. |
| FCUX-055 — picture zoom state | Separate Fit and 100% buttons with a visible zoom percentage. A custom 50%, 125% or 200% scale leaves both modes unselected. The 100% command returns to original-scale dimensions; Fit remains a distinct mode even when a small image happens to display at 100%. Keyboard Z retains the Fit/100% toggle. | `PictureViewerTests` exercises buttons, zoom text, custom levels and small-image equality; picture revision checks stay green. Twelve focused and 74 related App cases pass, with 12 platform skips in the related set. | Native pointer, scaling at 100/150/200%, decoder-cap labeling and assistive-technology acceptance remain open. |
| FCUX-052 — refresh changed viewer content | Refresh/F5 reopens the exact provider item, stages new bytes and mode-specific decoding, then replaces Text, Info, Picture or Page in the same window. A persistent banner marks stale snapshots; failures retain the last valid view and show the reason. Picture zoom and approximate pan, text/Info scroll, and selections on an identical revision are preserved. Page waits for native load before committing; cancellation and late provider results retire their sources safely. | `ViewerRefreshTests` covers text, Info, Picture, Page, provider-only sources, failed reopen/load, selection and scroll, close during open, and close during metadata admission. `ViewerProviderAdmissionTests` verifies the original item/device worker and late-source disposal. Nine focused cases pass; related viewer suite passes 326 cases with 12 platform skips. | Native archive/remote reopen and capability behavior, WebView2 page-load rollback, scaling and assistive-technology acceptance remain open. Providers without revision evidence require explicit Refresh to detect a change. |
| FCUX-053 — clear quick-view feedback | A selection-owned preview state reports Loading after the debounce and no-flicker delay, then Text, Hex, Picture or a specific message. Cancel/Retry and Open full viewer are visible actions. Empty, unsupported and denied results explain themselves; picture decode failure and the 64 MiB automatic cap keep Hex visible with a reason. Preview pixels scale with the pane up to 1,024 physical pixels. A late source, thumbnail or decoder result cannot publish over a newer selection. | `QuickViewLifetimeTests` covers held loading, rapid selection, Cancel/Retry, empty/full-viewer action, full-viewer cancellation, unsupported/denied results, damaged and oversized pictures, pane-size cap, and source ownership. Fourteen focused and 68 related quick-view/picture cases pass. | Native Shell thumbnail fallback, slow local/remote storage, small-pane layout, keyboard and assistive-technology acceptance remain open. The full viewer can have a higher decode budget than automatic quick view. |
| FCUX-054 — usable Viewer Go to | Replace the small unvalidated top-level input with the shared viewport-bounded prompt, visible Go/Cancel buttons, a wrapped label, and inline validation. Invalid and out-of-range offsets, percentages and line prefixes keep the draft open. Valid decimal/hex/percent positions and seekable line targets navigate after confirmation; nonseekable lines explain their limitation before scanning. Modal ownership blocks other viewer shortcuts until dismissal. | `ViewerGoToDialogTests` covers the pure parser and bounds, a 480 × 300 dialog with pointer correction, Enter line navigation, Escape, and a nonseekable source. Four focused and 338 related viewer/dialog cases pass, with 12 platform skips in the related set. The complete App rerun through this finding passes 2,296 tests, 63 platform skips and zero failures. | Native pointer, keyboard, scaled layout and remote seek capability acceptance remain open. Go to rejects values through the shared prompt, whose message Orca, NVDA and the macOS AX journey hear through Quick filter on Git-built `ad3bf946`; Go to itself was not run natively. A line beyond the document is reported after the asynchronous scan. |
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

## UXBUG-016 — pointer dismissal for overlay dialogs

Every shared in-window dialog has a visible, accessible close X in its header.
Pressing the pointer on the dimmed backdrop cancels the current dialog through
the same safe result as Esc; clicking inside the card does not dismiss it. The
topmost overlay alone closes, leaving an underlying draft and focus intact.
This covers Create in Registry and other chooser, prompt, confirmation and
custom review dialogs. `OverlayDialogPointerDismissTests` sends actual pointer
input to the close X and backdrop for the Registry chooser, verifies the custom
cancel result, inner-only dismissal and preserved outer draft, and proves an
inside click does not cancel a confirmation. Five focused cases and 51 related
dialog, focus, lifetime and viewport cases pass on Windows with 16 workers.
The owner confirmed both pointer exits on the Windows workstation, then
reported that the font-based X sat off center. It now uses a symmetric vector
shape inside the 30-DIP button; the pointer regression measures the icon's
rendered horizontal and vertical center within 0.5 DIP. The 51-case related
suite passes again after this correction. A guarded Windows VM runner is
tracked for native Registry checks. Its foreground-process check refused to
send F7 when an older FileCat window retained focus; the incidental empty
Create folder prompt from the first exploratory run was canceled without
creating anything. The later Windows VM mouse route and visual centering check
passed after that older foreground validation window closed. Scaled and
assistive-technology labeling remain open. A complete local App suite attempt was stopped before a final
result after 17 minutes; it is not counted as a pass. Final five-lane CI is
pending on the next pushed head.

The current Ubuntu VM checkout was fast-forwarded to the exact vector-fix
commit; all five focused `OverlayDialogPointerDismissTests` passed there.
An isolated native AT-SPI runner located the Create folder X, confirmed its
accessible name, cancel description and 30-by-30 screen-pixel pointer target,
and refused to
claim a pointer result because the Wayland guest's XTest bridge cannot move
the mouse. A stale GNOME Remote Desktop permission prompt was dismissed via
its scoped accessible Cancel action; a retry still timed out in XTest before
delivering the click. The native Ubuntu mouse route remains open. CI for the
preceding pointer-dismiss commit had successful producer, Windows, Ubuntu and
Windows ARM64 jobs; its Mac job was canceled by the later push, so the run was
overall unsuccessful rather than a product-test failure. The later
`d1fc2303` five-lane CI run completed successfully; CI on the final head
remains pending.

The Ubuntu Wayland guest then used a disposable uinput touchscreen for actual
physical pointer events. The native runner passed both X and backdrop taps
against the Git-built isolated app: `Close dialog` had its cancel description
and a 30-by-30 screen-pixel target, the backdrop point was outside the card,
both dialogs closed, and neither source nor destination fixture changed. The
report identifies the executable SHA-256 and `uinput-touch` input route. The
runner now removes its temporary sudo credential from the environment before
launching FileCat.
The Ubuntu Wayland VM subsequently passed the exact mouse-device route with
`linux-uinput-mouse.py` and the same guarded AT-SPI runner. The disposable
relative-motion uinput mouse delivered the X and backdrop left clicks to a
Git-built app (SHA-256
`f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`).
AT-SPI measured the Create folder X at (904, 137), 30 × 30 screen pixels,
with `Close dialog` and its cancel description. Both dialogs closed and the
source/target fixture remained empty. The result identifies `uinput-mouse`;
the guest credential was removed from FileCat's environment and the device
was destroyed after each click. Scaled visual and spoken checks remain open.

The isolated Mac checkout was fast-forwarded to `8772a585`; a locked, self-
contained ARM64 publish was signed in a new validation bundle. On its 2880 ×
1800 Retina desktop, a guarded visual runner fixed the app window to 1280 ×
800 points, sent F7, clicked the visible X and then the backdrop with native
CGEvent mouse input. Four approved screenshots proved open → closed → open →
closed, while both fixture folders remained empty. The blue dialog accent
changed from RGB (58, 150, 255) to (43, 43, 43) after each dismissal; the
vector X's white-pixel centroid was (1931, 281.5) against a button center of
(1932, 280) physical pixels. The repeated runner's JSON result passed.
System Events' `entire contents` query returns zero for this visibly
populated Avalonia window, but direct native AXUIElement traversal reaches its
descendants. On the same signed `8772a585` vector-X build, the direct probe
found one enabled `AXButton` named `Close dialog`, with cancel help and a
30 × 30 point target at (951, 126) inside the 1280 × 797 point window.
Wrong-process and closed-dialog negative controls both failed closed. The
revised `mac-ax-dialog-pointer-dismiss.py` then passed on a fresh fixture:
native CGEvent clicks on X and backdrop each dismissed the dialog, and
source/target stayed empty. Its app SHA-256 was
`e452d757c9f49bb76242e9cb706935584067c472d8d13eeafcc3b250963f28d3`.
Actual VoiceOver speech, Mac scaling and other dialog types still need review.

The Windows 11 VM's previous validation FileCat was confirmed as the sole
foreground owner and closed normally by exact executable path, leaving the
new Git-built test instance free to receive pointer input. A rerun of the
guarded Registry chooser check on build SHA-256
`0FFB1E3A06798D7D4C30C574F0FADF6FBE86DA704631EC354770717C6D271A68`
passed both native mouse exits, with an accessible `Close dialog` name, cancel
help and 30 × 30 pixel bounds. A captured close-button crop at 96 DPI had
92 high-contrast X pixels; its centroid differed from button center by
(-0.22, 0) physical pixels. The runner now enforces a 1.5-pixel center bound
in both axes. Windows 150%/other scale, other dialog types and spoken
acceptance remain open.
The generalized native pointer runner also passed on the Git-built Windows
VM app at `a571e62b` (SHA-256
`5D91B50025AB0084103F1F39E563C2BDD4E12251F2BFA1A34299D16058D75152`).
Both the Create folder and Create in Registry dialogs opened twice; their
accessible Close dialog buttons reported the same cancel help, measured
30 × 30 physical pixels, and their 92 high-contrast X pixels had centroid
offset (-0.22, 0) from center at 96 DPI. A native click on X and then a
native backdrop click each dismissed the respective dialog; the Create
folder source fixture stayed empty. JSON receipts and the close-button crop
are retained under the guest's `AppData\Local\FileCatValidation` and the
host native acceptance output. Further dialog families and scaled/spoken
checks were still pending at that checkpoint.

The Ubuntu Wayland runner now covers a second shared-dialog family, Copy.
Its isolated `a.txt` source was selected with an absolute uinput touch setup
tap, then an actual uinput mouse clicked the Copy review's X and backdrop in
two openings. AT-SPI exposed the 30 × 30 `Close dialog` control, its cancel
description and the destination field. The source SHA-256 stayed
`02d96fb583ff0571cde49db8f2ecffd43cedb0abacf6ed296114231d39328647`
and the target stayed empty. The same final runner passed Create folder again
with native mouse dismissal, after it learned to identify the owned app frame
among transient AT-SPI frames. Both JSON receipts identify the Git-built app
SHA-256 `f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593`.
Other dialog families, display scaling and spoken feedback remain open.

The Ubuntu runner then passed a disposable Recycle Bin Delete review on the
same Git-built app. It selected `a.txt` with the owned touch setup helper,
required the accessible review text to identify that file and the Recycle Bin,
and used the actual uinput mouse for X and backdrop dismissal. AT-SPI measured
the close control at 30 × 30 pixels with the same cancel description. Both
openings closed, the source SHA-256 stayed
`02d96fb583ff0571cde49db8f2ecffd43cedb0abacf6ed296114231d39328647`,
and the target stayed empty. The guest and ignored host JSON reports retain
the exact input and executable identities.

The Windows runner then passed a fresh Copy review fixture on the Git-built
Windows app. It selected `a.txt`, opened Copy twice, verified the shared
30 × 30 close X and its (-0.22, 0) pixel center offset, and canceled with
native X and backdrop clicks. The source SHA-256 stayed
`6C9A89482AB2E0A75957FFBAADD3F37737898C7A75A5FD8DBAA210B5B00AC398`
and the disposable target stayed empty. The final JSON receipt explicitly
records both checks. Registry, Create folder and Copy now pass the native
Windows pointer route; other dialog families and scaled/spoken checks remain.

The generalized runner also passed a fresh Recycle Bin Delete review on the
same Git-built Windows app. It selected the disposable `a.txt`, opened Delete
twice and dismissed it once through the visible X and once through a backdrop
mouse click. The 30 × 30 control exposed the cancel help; the captured
92-pixel vector X centroid offset was (-0.22, 0) at 96 DPI. The source
SHA-256 stayed
`6C9A89482AB2E0A75957FFBAADD3F37737898C7A75A5FD8DBAA210B5B00AC398`
and the target remained empty. The native report and button crop are retained
under the guest validation folder and ignored host test output. Other dialog
families and scaled/spoken checks remain open.

On 22 October, Windows Display Settings selected 150% (144 DPI) and 200%
(192 DPI) in the isolated Win11 VM. The tracked native dialog runner passed
Create folder, Copy, Move, Delete and Create in Registry at **both** scales on
the Git-built app with executable SHA-256
`CDAA633E53B23A0F4611916F8DFB2E3B401517821823BCFA5889AACDDDA0984E`.
Each dialog opened twice, exposed the accessible `Close dialog` button and its
cancel help, and closed by native X click and backdrop click. The vector X
pixel centroid was (0, 0) relative to button center in all ten captures;
targets measured 45 × 45 screen pixels at 150% and 60 × 60 at 200%. Every
fixture remained unchanged with an empty target. The ignored native JSON
reports and button crops are in the guest validation folder and host test
output. Display Settings then restored 100% and reported 96 DPI. Other dialog
families, non-Windows scaled visuals and actual screen-reader speech remain
open.

## UXBUG-017 — native icons cropped at display scaling above 100%

Reported by the owner on the Ubuntu VM at 200%: the panel header icons showed
only in part, while 100% looked right. Every native icon drew its enlarged
top-left part at any scaling above 100% on all three desktops; see the dated
entry at the top for the cause, the `ScaledBitmap` fix (`80598aeb`), the
Skia-rendered `FileCat.Rendering.Tests` regression and the before/after
captures on Ubuntu at 200%, the Retina Mac and the Win11 VM at 150% and 200%,
including Quick View's Shell thumbnail at 200% on Windows. Final five-lane CI
on the pushed head is the remaining gate.

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
- Close the [finite current acceptance scenarios](UX_UI_ACCEPTANCE_LEDGER.md)
  before declaring the audit complete. This dated implementation history is
  evidence, not a second current backlog.

The [session workflow](UX_UI_SESSION_WORKFLOW.md) records what the user's next
“continue” means: resolve a failed gate or active defect, then resume the
highest-severity genuinely unfinished ledger case with regression tests. The
Git checkout and `main` history are the delivery record; native acceptance
closure and final-head CI are tracked separately.
