# Local move into an existing writable ZIP

This implements the user-requested replacement for the blanket F5/manual-delete refusal in the existing ZIP route.
It does not enable new archive profiles. The amended plan requires the corresponding behavior for future qualified
formats too; those integrations, the complete common transaction boundary and final release qualification remain open.

## Behavior

F6 submits an archive job with explicit `ArchivePlan.MoveSources` intent. F5 continues to copy. Queue mode and a single
incoming member's chosen name are carried to the archive route. Source paths enter the job's write set as well as its
read set, and the durable job header identifies archive-move intent. Archive/source deletion steps are separately
journaled. This route offers no automatic inverse or archive undo.

Move inputs are local regular files and directories. Sources are captured with identity, length/time/attributes,
representable Unix modes and the existing filesystem adapter's download-mark representation. The exact bytes encoded
are hashed; new entries are freshly decoded and compared with those hashes before publication. A whole-file digest
then binds the published archive to the verified stage before any source removal starts. The archive is held during
removal, with continued path/identity/metadata/origin checks. A canceled or failed pre-publication write removes no
sources. Cancellation after publication reports the saved archive and the actual partial move.

Successfully archived inputs are removed only after their identity/content/origin rereview. Windows uses its existing
reviewed handle-bound deletion adapter. Unix uses the ordinary guarded pathname operations used by local cross-volume
moves; it is not an atomic filesystem snapshot or a newly qualified Unix exact-unlink protocol. Directories are removed
individually and only after their captured children were confirmed removed and the directory is empty. No recursive
source-tree deletion is used by the portable route. A newly added child or an unconfirmed child removal keeps the
folder. Failed/skipped roots stay marked; completed roots are unmarked and both panes refresh.

Conflict skipping keeps original files. Existing archive folders merge; replacing duplicate file occurrences updates
every same-name occurrence, so an old later copy cannot hide the moved data. Self-inclusion and file/folder namespace
conflicts refuse before mutation. Source removal failures are reported as a saved archive with incomplete source
removal, rather than as a failed archive write. A journal failure after deletion cannot be mistaken for a retained
source or trigger a repeated deletion.

The namespace check includes accepted incoming files as well as existing members. In particular, adding a file named
`tree` cannot hide an existing implicit folder represented only by `tree/child.txt`. The regression checks that this
refusal changes neither the original archive bytes nor the move source.

Download marks are propagated without decreasing an existing declared Windows zone. Windows uses a zone-only output
when the original archive was unmarked; the original archive's other mark fields remain intact on an increase. The
legacy Unix adapter's quarantine/origin representation is preserved. Source marks are checked again before deletion.
This does not replace the still-required complete typed provenance/policy qualification in the common new-format route.

Ordinary ZIP entries do not store Windows alternate streams, Mac resource forks or arbitrary Unix extended attributes.
An input with additional data/metadata outside the handled origin mark is archived by its primary data and retained
with a specific partial-move report. Stream enumeration now refuses incomplete/error/oversized inventories instead of
treating them as empty. This is an actual source-data constraint, not the former blanket ban on moving into archives.
The native stream-inventory contract now reports an inspection failure (including a missing file) with an I/O error;
the existing stream-listing regression was updated to assert that intended behavior. Adjacent native file-operation
compatibility checks accompany move acceptance. These cases do not establish race-free namespace or additional-stream/
extended-attribute inventory; complete qualification of that boundary remains part of the common transaction work.

Replacement, skipped-name, existing-directory and root lookups use indexes rather than repeated full scans. The
current input count/origin budget is bounded; complete 100,000-item capture/heap/disk/alias qualification is still part
of the larger plan and is not proved by these functional cases.

## Evidence and remaining work

Initial real-file acceptance covers successful file/folder moves, empty folders, both conflict choices, same-byte new
identities, same-length content changes with restored mtime, new children, cancellation before/after publication,
deletion refusal, changed published archives, self-inclusion, duplicate occurrences, ordinary copies, download marks,
Unix modes and existing-folder merge. A follow-up also checks a failed child removal whose path disappears externally;
that cannot make its parent count as a completed move.

The UI exercises actual F5/F6 dialogs, queued F6, archive-job intent, completed source deletion and pane refresh. These
are Avalonia headless UI tests with real filesystem jobs. They are not native interactive desktop GUI acceptance.
Native desktop journeys, the remaining metadata/namespace/crash/scale matrix and full independent final review remain
required. No claim of absence of all possible bugs or full archive-writing completion follows from this checkpoint.

The subsequent [native desktop follow-up](ZIP_MOVE_NATIVE_GUI.md) adds Mac keyboard/AX and Ubuntu desktop journeys,
fixes edited archive destinations, changes the conflict default to Skip existing and corrects singular summaries.
Its 85-case regression evidence supersedes the 71-case UI counts below for that follow-up's tested source. Native
Windows desktop, complete Mac visual acceptance and the larger qualification matrices remain open.

| Run | Real-file/core checks | UI and adjacent preparation/destination/scope checks |
|---|---|---|
| Windows first guest pass | 13 passed/26 skipped core; 17 passed/1 skipped actual Windows move adapter | 71 passed |
| Mac first and indexed follow-up | 38 passed/1 skipped | 71 passed |
| Ubuntu first and indexed follow-up | 38 passed/1 skipped | 71 passed |
| Windows final guest replay | 13 passed/27 skipped core; 18 passed/1 skipped actual Windows move adapter | 71 passed |
| Mac final child-accounting replay | 39 passed/1 skipped; corrected test-hook replay 18 passed/1 skipped | 71 passed |
| Ubuntu final child-accounting replay | 39 passed/1 skipped; corrected test-hook replay 18 passed/1 skipped | 71 passed |
| Windows final namespace/compatibility replay | 13 passed/28 skipped core; 19 move and 18 adjacent native checks passed/1 skipped | 71 passed |
| Mac final namespace replay | 40 passed/1 skipped | 71 passed |
| Ubuntu final namespace replay | 40 passed/1 skipped | 71 passed |

The final Windows suites ran under the existing medium-integrity harness (12288 before/8192 after; every child exited
0), using .NET 10.0.14. Mac is arm64/APFS and Ubuntu is x64/ext4. The Windows-only alternate-stream and Unix-only mode
checks are skipped on the opposite platform. Core move cases deliberately run in the Windows platform project there,
so the portable adapter is never counted as native Windows deletion evidence. The host's final native replay also
passed 18 checks with one Unix-only skip.
The final Windows replay adds the implicit-folder collision regression and the 18 adjacent native file-operation
checks. All three medium-integrity children exited 0; a later independent result collector verified exact TRX counters,
the original success marker and an empty FileCat/test-process inventory. The first collection attempt preceded run
completion, and no test was restarted on that observation failure.

The first final Windows replay exposed a test-hook path comparison using mixed slash styles, which prevented the
intended injected deletion refusal. Normalize both paths in that hook; the corrected replay above passes. An earlier
sandboxed host VSTest run could not connect to its test host and was aborted; it is not counted as a pass. Successful
host and guest replays followed. The corrected hook also passed the move-only suite again on Mac and Ubuntu; runtime
code and the already-passing 71-case Unix UI/preparation suites did not change for that test-only correction.

Post-run inventories found no owned FileCat/dotnet/testhost instances left. The Mac's temporary `caffeinate` assertions
remain active (idle display sleep prevention, idle system sleep prevention and user-active assertion), as requested.

Immutable overlays and raw TRX evidence are retained in ignored `.tmp/test-results/archive-writing/move-v1` and the
task-owned guest directories. Reports added later do not change tested runtime code.

- Initial 15-file source overlay: `456F313D3F622779CA3784B1B0E8666029A26A0D5CAAC25198BED311526B4699`.
- Corrected queued-UI driver/source overlay: `DEBBB65850F485FB34069AA115E5EBE0E122532A0F34D7D2A315A242B0F1EC4F`.
- Indexed planner/root-lookup follow-up: `33D97D0FB87F35EF5384E93169B235248DA8AFFE41C0C7E5E50808A0AF58BEE2`.
- Final child-accounting/source follow-up: `B6EC8936E5AEB08BC672A8D3062F8875795FB64291B6C483E5DBF43599ED4161`.
- First Windows 88-file managed overlay: `4CB20445BD0A6DF5D7726B5FF254D8F5A5826DFB3A51BB3FCD3A5D147A2C07C6`.
- First Windows evidence: `531E2FCB013960E81E4C039BBD016524FF5D384819BD923A0E520C64D3856504`.
- First Mac evidence: `75A9B7D61A0F2ABF098468D0593C22B5C6EC590B69E0762A4274BA385A3572B1`.
- First Ubuntu evidence: `056BECC011E14AAFF92617203B0BB7BC3640077EF1A8C99698ABEC89703E7AE6`.
- Final Windows managed overlay: `82602A8DEAD9847CB065AEE659FFB2ACDA79868A162955720FCCEFC1E67BD021`.
- Corrected shared test-hook source overlay: `72A3241E5415C17F89C277C0D4D9F6194B37DA3D49B604A5B5461ECF3C3558C7`.
- Final Windows evidence: `5EF274641BD20F3D06EDEF9F38CE5D0A07B1C0A84A3D7C6A84867B9CE2877879`.
- Mac final runtime/UI evidence: `62880FE9D01A63971F83DB883D99E199612565A850B0C1C51DB5EB3A3C0038E0`.
- Ubuntu final runtime/UI evidence: `70C0478B8BE34B51288DA1C3328F9F9096A029F8658719A6542468FB41B9458D`.
- Mac corrected-hook evidence: `EEC24DD9DEE77CC3768EE094721C275995821FB27C181B4BF95A6950EF0C5132`.
- Ubuntu corrected-hook evidence: `51F37A746EB22EB2712FBEE3BDE8FD8BE47C87E94211BBB58791AB3934E385BE`.
- Final namespace-fix source overlay: `4934F9F819184702493076C37D17D5C154EA20C53B2085002BA9B29772DAEA86`.
- Final namespace-fix Windows managed overlay: `6926B10FA73BFDEACF70AE88973880EF9A5AAEC85FAC0B96897DCE9CEBE67928`.
- Final Windows namespace/compatibility evidence: `668B33952F79E9964856F1942E9D7F293906A003085084B63F1D6E6F47BF3907`.
- Final Mac namespace/UI evidence: `FFB6BC7472CF43BDA27C0D676C4FC7AC24BB755F198CEB56A138607E294A4288`.
- Final Ubuntu namespace/UI evidence: `9B6DE644A277D897A4A6E708D347FDC7365B1CC20DACFF10016365C42A1D9173`.

The refreshed self-contained win-x64 development application has an SPDX inventory at ignored
`.tmp/test-results/archive-writing/move-artifacts-v2/application.spdx.json`, SHA-256
`40E49B4EC6893454347DA76AFAA69C70B1E4B43AE1DBDF3DB58A43F9E3D68BB4`. Its 261 file hashes were independently rechecked;
34 reviewed NuGet packages and one runtime pack contribute 235 files matching the reviewed package archives. No
dependency or licensing inventory changed. The source identity explicitly records base
`55d0c5ec571a9c75152968aea3a6a2c54781bbca` plus the captured dirty-state tar SHA-256
`bb6bd9d8972215bca6a98645a2dc62fd89a71729f7a8092d4944bbfb65e12abc`; it does not assert a reproducible commit identity.
Final report edits follow that capture and do not change the tested runtime inputs. Release RID/package qualification
remains open.
