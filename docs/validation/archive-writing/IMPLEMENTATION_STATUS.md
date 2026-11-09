# Archive writing implementation status

**9 November: late retained-copy outcome fix and stateful fuzz preparation are in flight.**
Candidate `8d0946ff49c16b1950017f31ec52dff66407ac0e` keeps the result of a running native
saved-copy publication through late IO shutdown, disposes its receipts and reports a confirmed
copy while the edit UI is active. The host App/test build has zero warnings; Mac's complete v44
Core/Remote/App cohort is running, and the Windows packet is staged behind its v43 full cohort.
Ubuntu remains occupied by FZ01. The test-only v12 FZ09 packet adds the production archive
transaction collector to instrumentation, requires it in image preparation, and is staged for
Ubuntu replay/canary/short-run checks after v40. Its two-hour-per-target and shared campaign
floors have not started or been claimed. The
[Unix cleanup boundary](UNIX_EDIT_EXACT_REMOVAL_BOUNDARY.md) records why same-user replacements
still prohibit pathname-based native Discard.

**9 November: Mac full recovery-byte regressions pass; Windows full retry is running.**
The runner-only v41 cohort uses the v40 product sources. Mac passed all 30 connected and complete
Core/Remote/App gates with zero failures, including the exact 335-new-case Core inventory and no
missing prior case or lost prior pass. Its independently verified sealed bundle is 29,697,811 bytes /
SHA-256 `f685aca5fc7b4876819ed30e05a017d88d6774569367a35accb891533420553f`.
Windows v41 passed complete Core and the same inventory, then preserved a sealed failure when the
unchanged 8 GiB preflight reserve refused Remote. Twenty-six old extracted guest cohorts were
checked against retained host indexes before removal; the cleanup receipt records 26 verified and
26 removed folders, zero errors, and 10,190,790,656 bytes free afterward. Two v42 retry attempts
stopped before tests because the packet's expected predecessor hash and runner predecessor folder
did not match the actual sealed v41 source ledger. The v43 runner uses that exact v41 ledger and is
now executing the full Windows cohort. No failed attempt is counted as a pass. Ubuntu follows its
active FZ01 campaign, focused v40 batch and first native FZ09 launch. Full release qualification
remains open.

**9 November: retained-copy Save verified copy UI passes Mac and Windows focused gates.**
The recovery dialog offers the action only for a complete unused capture when a native destination
adapter is present. It uses a local save picker, exclusive native publication, and reports a
published copy separately from later receipt uncertainty. A two-case headless dialog check
reviews without publishing and follows the picker through a real native copy. Candidate
`a521bf45285110a2c836d9caec824e207fb99a5e` passes Mac's 27 and Windows' 28 connected
focused gates, including the two-case dialog gate, with zero failures. The sealed Mac evidence
is 17,565,568 bytes / SHA-256 `dc4d8abaa11f12d002ec2e07a7755a2076eb7d6c9304e07a48e1b9c80156efb1`;
Windows is 18,930,371 bytes / `a91ad57620a7a84fdc2934ec371e1f1bc460057beafbbf56814a5c282dfa4956`.
The [independent verifier](../../eng/validation/archive-writing/verify_edit_recovered_save_copy_ui_v40.py)
passes source/compiler/DLL/PDB/raw-test binding. The default factory remains disabled. Ubuntu's
direct v36→v40 packet is staged, but execution waits for its FZ01 long campaign to finish.

**9 November: restarted retained-capture Save copy passes Mac and Windows focused gates.**
A new internal action reopens one complete, unlinked capture through native handles, binds it to
the fully validated historical state, checks exact payload/record bytes and metadata, admits the
new source intent under the root storage gate, and uses the existing exclusive new-file native
publication protocol. The original working file and captured bytes are retained. Nine shared
cases cover restart, later state, destination collision, corrupt/foreign source evidence,
repeat attempt, stale state and cancellation. Candidate `248100915688bb5e76e073776755703e5531147d`
passes 27 Mac and 28 Windows connected gates, including recovered-save-copy9, capture33,
saved-copy-session26 and admission45 on each, with zero failures. Mac's sealed transport is
17,539,497 bytes / SHA-256 `3be7514445ae3729125319e64228d43b3598a1c38066e7262031514715779071`;
Windows is 18,901,969 bytes / `61ceb7374f527ddae1720f691962e2c45f61b1c86a248987accb665ab92dfaf8`.
The [independent verifier](../../eng/validation/archive-writing/verify_edit_recovered_save_copy_v39.py)
passes source/compiler/DLL/PDB/raw-test binding. Windows' first 8 GiB reserve refusal remains
in its sealed evidence; after exact hash/size checks against host copies, 528,229,959 bytes of
duplicate older guest tar files were removed and the same candidate resumed without lowering
the reserve. No default UI/factory route is enabled. Ubuntu waits for FZ01. Recovery UI,
partial-outcome handling, crash/resource matrix and complete inventories remain open.

**9 November: historical retained-capture recovery passes Mac and Windows guest gates.**
The native review now binds each capture to its exact record in the fully validated bounded
session-state chain, so a later legitimate state revision does not hide an older complete capture.
Three new shared cases cover one and two later revisions and a saved-copy intent linked to the
earlier state. Candidate `a59c7d2aa5515cb81f6c59a2af8a0e138a2437b8` passes all 26 Mac and
27 Windows connected focused gates, including capture recovery33 and saved-copy sessions26 on
each, with zero failures. The sealed Mac evidence is 17,546,759 bytes / SHA-256
`5189bbd807e501aad149678df79f1b8074b19308a1106aa3032d4d93d46bb0be`;
Windows is 18,907,206 bytes / `7983b336985294c24dad4c99db7b7cf44a99df9d4864385343f6690654a92911`.
Independent source/compiler/DLL/PDB/raw-result verification passes; owned processes are zero
and Mac's wake helper remains. Ubuntu's same-candidate packet is sealed but waits for FZ01.
Full inventories for these bytes, recovery actions and all other plan gates remain open.

**9 November: FZ09 recovery-record packet is built; legacy adoption boundary is recorded.**
The test-only v7 harness builds cleanly, adds instrumented production session, capture, saved-copy
intent and receipt parser targets with synthetic schema/corruption seeds; its sealed native-fuzz packet is queued behind
the active Ubuntu FZ01 campaign. Separately, Mac/Windows each pass all28 uninstrumented guest
v7 replays against exact v38 product DLLs (eight accepted, twenty controlled refusals), source/seed
hashes independently checked. No FZ09 campaign floor is claimed. A legacy-adoption draft exposed that old
inherited permissions cannot be opened under version-3's exact private lease; the draft was removed.
The [adoption boundary](LEGACY_EDIT_ADOPTION_BOUNDARY.md) now requires a pinned read-only legacy
source and separate private destination before migration, routing or retirement can proceed.

**9 November: retained-copy recovery UI passes Mac and Windows focused guest gates.**
`v37` passes the new read-only retained-copy review case, all 26 connected Mac and 27 Windows focused
gates, 45 native storage-admission cases on each, and the applicable Discard cases with zero failures.
The sealed Mac transport is 17,540,399 bytes / SHA-256
`6009c4c7ea6a4176a54914530a102eb65655100dfa642a95f304eb6119189f13`; Windows is
18,909,575 bytes / `55bdb00cae83c1de66814fd922e6608685ecfdbb14ea0d18d59fa4f887a0aaf6`.
Independent verification binds the exact source, compiler documents, DLL/PDBs and test counts to candidate
`6cd1b63e92f0b498ea3883d8cdf39c4b7affc93d`. Owned processes are zero on both guests; Mac's
wake helper remains. Ubuntu's `v37` UI batch waits for its active long FZ01 campaign. The dialog only
reviews preserved data; fresh recovery actions, adoption, default composition, full inventories and all
remaining qualification remain open.

**9 November: coverage-guided fuzz canary passes; structured FZ01 campaign is running.**
The [test-only setup](FUZZ_CAMPAIGN_SETUP.md) pins the .NET 10/SharpFuzz/native driver artifacts and
uses separately instrumented guest copies of the verified product DLLs. Ubuntu's canary-only input
adaptation found a post-validation defect in the real Core guard; the artifact crashes only the
patched image. A clean strict TAR/raw-decoder 60-second gate completed 391,877 inputs with no crash.
Four individually accepted synthetic member/PAX seeds now feed a bounded 12,000-second Ubuntu run.
Its nightly floor is **not yet earned**. Other fuzz targets/floors, linked-native tooling provenance,
three-platform and release qualification remain open. The next unregistered native-edit candidate
adds a read-only retained-capture recovery dialog; its Mac/Windows guest UI gates pass above.

**9 November: reviewed Discard allocation and mapped-editor follow-up passes everywhere applicable.**
`v36` passes Windows40 discard cases (including writable mapped pages after the editor's original file handle
closes),6 native-store UI cases and45 admission cases. Mac/Ubuntu each pass45 admission,3 Unix refusal-preservation
and3 applicable UI cases; their3 destructive UI cases remain explicitly skipped because Unix deletion is unavailable.
Every connected focused gate passes on all three systems. Exact raw/compiler/DLL/PDB evidence is preserved and
verified, owned instances are zero and Mac's wake helper remains. The128-inspection allocation ceiling passes;
empty-gate hashing no longer allocates an unused1 MiB buffer per inspection. This candidate's new full inventories
remain unrun. Native registration, Unix exact removal, recovery/adoption/overwrite, full native desktop acceptance,
canary-validated fuzz/RID/package/release gates and final independent review remain open.

**9 November: reviewed Windows Discard and its confirmation flow pass connected guest gates.**
`v35` passes Windows39 real-file discard cases and6 native-store headless UI cases. Mac/Ubuntu each pass3
unavailable-operation preservation cases and3 applicable UI cases; exact Unix deletion remains unavailable.
All three also pass admission44 and their complete connected focused selections. Sealed raw/source/DLL/PDB
bundles are verified, owned instances are zero and Mac's wake helper remains. New full inventories are unrun.
A follow-up now avoids an unused1 MiB buffer in every empty gate inspection, adds a128-review allocation ceiling
and tests an editor retaining writable mapped pages after closing its file handle. The `v36` candidate queues
admission45 and Windows discard40; its host Windows build passes. No production factory/profile is enabled.

**9 November: mutation gates pass on all three systems; reviewed native Discard/UI candidate queued.**
`v34` passes all44 storage/mutation cases and every connected focused gate on Windows/Mac/Ubuntu.
Each sealed raw/source/DLL/PDB bundle is verified with zero owned instances; Mac's wake helper remains.
Windows' initial 8 GiB startup refusal is preserved separately inside its resumed same-candidate bundle.
Two matching preserved-host duplicate archives reclaimed772,614,362 bytes; sources/extracted evidence remain.
The startup collector omitted two lengths after moving files; raw data remains unchanged and its sealed
transport independently proves the actual lengths/hashes. This metadata defect is explicit in the checkpoint.
The preceding full milestone also passes: Mac/Ubuntu `v31`, Windows `v33`, each bound to its own candidate.

The [next connected candidate](NATIVE_EDIT_STORAGE_ACCOUNTING.md) implements fresh native Windows Discard
ownership, full preflight and exact-handle removal for reviewed working/retained files, earlier capture states,
interrupted copies and saved-copy intents. The confirmation action uses its typed complete/partial outcome.
It adds39 native Windows,3 Unix refusal-preservation and6 headless UI cases; Core/Windows/App builds pass.
Fresh guest affected gates are queued. Unix deletion, recovery/adoption, overwrite/composition, native desktop,
fuzz/RID/package/release and final independent review remain required. No production factory/profile is enabled.

**9 November: native retained-storage admission passes Ubuntu's full milestone; connected mutation corrections queued.**
Ubuntu `v31` passes native admission34 and every affected gate plus complete Core8,649/185 skipped,
Remote2,543/126 and App2,765/188. Its sealed raw/source/DLL/PDB evidence is verified with zero owned instances.
Mac's same-candidate full suites also pass: Core8,627/207 skipped, Remote2,638/31 and App2,763/190. Its sealed evidence is verified, owned instances are zero and the wake helper remains. Windows `v33`
passes admission34, connected affected gates and complete Core/Remote; App remains confirmed live and sealing is pending.
Its `v32` failed fixture evidence remains preserved and verified, independently from this correction.

The next connected `v34` candidate serializes saved-copy intent and exact live capture cleanup with storage
admission, uses root-before-snapshot lock ordering, retires all owners/permits after cleanup refusal and checks
new-root parent/child paths. Native Discard's unsafe recursive-path fallback is disabled until exact native
ownership is implemented. Ten new failure/race/retention cases expand the native matrix to44. All44 and every connected focused gate pass on Mac/Ubuntu; sealed source/binary/raw evidence is verified with zero owned instances. Windows follows its active v33 full retry. New complete inventories remain explicitly unrun for these newer runtime bytes. Core and Windows-platform builds pass cleanly. A subsequent UI correction catches unsupported Discard refusals and builds App cleanly; its runtime validation belongs to the next UI candidate.
Safe discard, recovery actions/startup UI, overwrite/adoption/composition and the full original plan remain open.
No native store/profile is enabled. The [cadence](VALIDATION_CADENCE.md) reserves the next complete inventory
for a connected recovery workflow milestone or a finding requiring broader coverage.

**9 November: connected native saved-copy gates pass everywhere; Windows full retry also passes.**
`v29` passes native session25, strict intent14, cancellation fixture2 and all connected gates on Windows/Mac/Ubuntu.
The Windows complete inventories pass: Core8,139/666 skipped (275 required additions, no lost baseline cases/passes),
Remote2,405/264 skipped and App2,835/118 skipped. Sealed raw/source/DLL/PDB evidence is verified, with zero owned
instances on each system and the Mac wake helper retained. Mac/Ubuntu `v29` full suites remain explicitly unrun;
their preceding `v28` full milestone is attached to its own runtime bytes.

The [native storage-accounting draft](NATIVE_EDIT_STORAGE_ACCOUNTING.md) now compiles and measures bounded logical
data plus native metadata without hashing payloads. Windows uses metadata-only handles and a complete bounded
stream-size catalogue. Root-wide write admission, persistent contingency charges, space checks and native
resource/concurrency/crash qualification remain required for the next connected batch. No store/profile is enabled.

**9 November: preceding native edit full milestone passes on Mac/Ubuntu; Windows capacity failure is preserved.**
`v28` completes Core/Remote/App on Mac and Ubuntu with zero failures, 235 required new Core cases and no missing
baseline cases/lost passes. Sealed raw/source/DLL/PDB evidence is preserved and verified, with zero owned
instances and the Mac wake helper retained. Windows Core encountered a confirmed disk-full error and an
unbounded cancellation-fixture wait. Its exact process/dump/exception evidence is retained; 76 checksum-matched
duplicate archives were reclaimed and the independent Remote/App suites completed successfully. That failed
cohort remains preserved separately from the successful `v29` retry above.

The connected [native Save copy session/recovery implementation](NATIVE_EDIT_SAVE_COPY_SESSIONS.md) passes `v29`
on Mac/Ubuntu: native session25, strict intent14, cancellation fixture2 and all connected focused gates. Sealed
raw/source/DLL/PDB evidence is verified, with zero owned instances and the Mac wake helper preserved. Windows
runs affected gates and full inventories on the same candidate, with successful results recorded above.
Mac/Ubuntu full suites for these newer runtime bytes remain unrun until the next complete workflow milestone. The [cadence reassessment](VALIDATION_CADENCE.md)
adds compact read-only observation and keeps all final QA requirements. Overwrite, cleanup, retained resources,
UI composition and the wider plan remain open; no native store/profile is enabled.

**9 November: saved-copy publication/encoding gates pass on all three systems; startup-reader follow-up queued.**
`v26` passes record25 (including 10,000 seeded mutations), native publication25 Windows / 26 Mac/Ubuntu and
all connected focused gates. Exact source/DLL/PDB evidence is preserved and verified with zero owned instances;
Mac's wake helper remains. The original 10,193-input probe now returns 100 accepted / 10,093 controlled refusals /
zero escaping exceptions. Review identified the same delayed Unicode failure in legacy session/capture readers.
Their correction builds cleanly and `v27` expands session recovery to 25 and native capture recovery to 31 cases.
Fresh guest qualification and then complete runtime milestone inventories remain required. No store/profile is enabled.

**8 November: malformed receipt UTF-8 refusal corrected; expanded regression candidate queued.**
The [encoding probe](SAVE_COPY_RECEIPT_ENCODING.md) found `InvalidOperationException` escaping through delayed
JSON Unicode materialization on malformed receipt bytes. Exact failed probe/source/binary evidence is preserved.
Complete bounded UTF-8 validation and controlled Unicode refusal are implemented with five new cases, including
10,000 deterministic mutations. `v26` queues record25 and native publication/connected regressions on all three
systems. The preceding `v23` complete Core/Remote/App inventories now pass everywhere and remain attached to
their own runtime bytes; no store/profile is enabled.

**8 November: native saved-copy exclusive publication and receipts pass Windows/Ubuntu focused gates.**
[Saved-copy publication](NATIVE_EDIT_SAVE_COPY_PUBLICATION.md) adds single-owner native name reviews, complete
receipt readback and confirmed/uncertain outcomes that survive late cancellation or receipt failure. Core and
Windows-platform builds pass without warnings/errors. The first `v24` native gates preserve Windows sharing-reader
and Ubuntu umask fixture failures, with record20 passing. The corrected `v25` candidate passes publication25 Windows /
26 Ubuntu and 20 strict receipt cases on both; all connected focused regressions also pass. Exact sealed bundles
and source/binary bindings are preserved, with zero owned instances and unchanged product binaries across the
fixture correction. Mac follows its active `v23` full
inventory. The changed candidate's complete inventories are explicitly not run yet under
the [measured cadence](VALIDATION_CADENCE.md). Replacement, restart discovery/actions, cleanup/crashes and UI/job
integration remain open; native store/profile registration and the existing guard remain unchanged.

**8 November: a Windows full-run close failure is diagnosed and a correction is under test.**
[Comparison cleanup](COMPARISON_CLOSE_FAILURE_DIAGNOSTIC.md) records the owned-source close exception
that escaped through an async-finally continuation and left the xUnit unhandled-exception handler waiting.
Its dump and failed cohort are preserved. Candidate `fa3975d5` returns the selected failure through normal
task completion after both owners retire; the original 104-case matrix and four deferred regression
variants pass all 108 `v23` cases on Windows/Mac/Ubuntu. The corrected complete Core inventories pass on Windows
(8,093 passed / 611 skipped), Ubuntu (8,514 / 185) and Mac (8,492 / 207). Windows/Ubuntu complete Remote/App also
pass and their exact raw bundles are preserved and verified; Mac's complete App inventory remains active.

**8 November: retained Save copy destination preparation implemented; first native Unix gates pass.**
[Saved-copy preparation](NATIVE_EDIT_SAVE_COPY_PREPARATION.md) copies retained views into a fresh native
private stage, checks complete input/output bytes and preserves exact origin/time and private security.
Mac and Ubuntu each pass all 17 native cases in `awe-storage-v22`; Windows passes them in `v23`.
Mac/Ubuntu `v22` complete Core/Remote/App inventories also pass and their raw evidence is preserved and verified.
Publication/overwrite/recovery and application integration remain required, and the native
Save copy guard/factory qualification remain unchanged.

**8 November: native archive snapshot requests bind the originating target/profile; live gates queued.**
[Request binding](NATIVE_EDIT_REQUEST_BINDING.md) closes a gap between the generic replacement shape
and the archive/member/profile actually reviewed for that session. The snapshot fixture now uses native
archive records and tests redirection, remote-to-archive refusal and explicit rebases. Windows-platform
and Core builds pass. Candidate `39e0f0c6`, `awe-storage-v21`, will combine this with the checksum test
expansion after the active full batches finish. Its first snapshot35 Windows / 34 Mac / 34 Ubuntu and
checksum UI12 gates pass, as does Ubuntu's SFTP23 gate. Complete inventories remain pending. The prior
Mac/Ubuntu `v18` and Windows `v20` complete Core/Remote/App inventories pass and their exact evidence is
preserved; all three now run the same frozen `v21` implementation. No factory/profile is enabled.

**8 November: retained SFTP edit uploads implemented; three-platform qualification is running.**
[Retained SFTP edits](RETAINED_SFTP_EDITS.md) bind a captured edit to its originating server file,
verify consumed bytes and require complete stage readback before guarded replacement. The first
Mac/Ubuntu iteration exposed a connection-pool cleanup fixture error; its failures are preserved and
the corrected 23-case matrix passes on all three platforms as `awe-storage-v18`. Complete Core/Remote/App
inventories continue on Mac/Ubuntu. The Windows `v19` diagnosis proves Defender blocks the deceptive
executable-name fixture; FileCat reports that refusal correctly. The `v20` test expansion requires
that native refusal explicitly and adds a readable variant; all 12 checksum UI checks and 23 retained
SFTP cases pass on Windows before its active full rerun (one platform-specific checksum UI case skips).
The previous complete Core suites pass on all three platforms. Complete Mac/Ubuntu App suites pass;
Windows has one checksum fixture expectation under correction. No factory/profile is enabled.

**8 November: retained native edit snapshots drive legacy ZIP jobs; first integration gates pass.**
[Retained ZIP edits](RETAINED_LEGACY_ZIP_EDITS.md) remove the source-path byte reopen, bind a single originating
session replacement and verify source/output hashes. All three platforms pass 18 real-file cases.
The full application inventory found a canceled-admission retirement defect; its fix passes 29 Core
and 18 history UI checks on each platform. Ubuntu's four picture-fixture failures are corrected by
matching the production runtime environment; its 12 boundary checks pass. The `feb9105e` full Core and
application rerun is complete, with the Windows checksum-dialog finding above. Failures remain preserved.

**8 November: Windows capture-tree cleanup and fresh read-only recovery implemented; full regression rerun active.**
[Capture recovery](ARCHIVE_EDIT_CAPTURE_RECOVERY.md) retains native creation authority through Windows
payload/record/directory removal, preserves exact opaque origin marks and reports retained copies after restart.
Candidate `f62d81f9` passes 27 recovery and 35 Windows / 34 Unix snapshot cases plus its connected gates on
all three platforms. Record-byte hardening and the corrected Windows boundary fixture pass the later
focused gates. The `08097526` complete Mac/Ubuntu inventories exposed missing fixture setup; `b9929987`
restores the earlier case-sensitive image/private-keyring environment for the full Core/application retry.
The progress checkpoint preserves those failures. Unix destructive cleanup, restart actions/UI, durable retained-disk
admission, Save copy, ZIP/SFTP/discard/adoption/composition and the remaining qualification gates stay open.
The application still uses the legacy store; no profile or native-store factory is enabled.

**8 November: native snapshot capture and queued retained-input views pass focused platform gates.**
[Native snapshots](ARCHIVE_EDIT_SNAPSHOTS.md) retain actual file ownership through job reconciliation,
preserve exact bytes/origin/time, bind one replacement to a session and avoid queued source pathname reopens.
All platforms pass 26 snapshot and ten commit-copy cases, their native storage/recovery gates and the
connected Core/application edit selections. The [checkpoint](edit-snapshots-checkpoint.json) binds exact
compiler/binary bytes, preserves fixture failures and proves unchanged product binaries across the retry.
All owned instances are closed; Mac's wake helper remains active. Capture-record/directory cleanup,
Unix payload cleanup, native Save copy, ZIP/SFTP integration, discard/migration, resource bounds,
application wiring and the complete qualification/final-review gates remain open. The factory stays disabled.

**8 November: native edit storage and working reads implemented behind an explicit test factory.**
[Retained edit storage](ARCHIVE_EDIT_STORAGE.md) adds private native creation, interrupted-copy recovery,
exclusive immutable state revisions, bounded complete history review and atomic-editor-save admission.
The Mac/Ubuntu correction matrices pass 19 record, 16 directory, 19 native storage, 110 Core and 190
application cases each. Windows passes 19/16/14/100/190 with ten Unix-only Core cases skipped after an
initial sharing refusal and a later idle-machine retry. [Thirteen preserved bundles](edit-storage-checkpoint.json)
bind exact sources/binaries and all early failures, including Ubuntu's corrected umask admission defect.
The application factory remains unchanged; native snapshot/discard ownership, migration/composition,
complete platform regressions and every release/final-review gate remain open.

**8 November: connected native archive edit/readback matrix passes, ownership gates open, unregistered.**
[Native edit sessions](ARCHIVE_EDIT_SESSIONS.md) connect F4, immutable commit copies, explicit conflict/rebase,
positive publication facts and actual published-member readback. The connected packet also preserves old/future
session records, avoids metadata filename collisions and makes new Unix edit files private. The retained `14cb2678`
cohort and its Windows fixture timeout are preserved. Candidate `f758c487` passes the focused edit gate,
259 application cases on every platform and complete Core inventories (Windows 8,065 / 465 skipped;
Mac 8,318 / 207; Ubuntu 8,340 / 185). Windows also passes ten real F4/Commit cases, 217 native checks,
18 crash cases, 14 application-entry cases and, after a separate navigation-fixture correction, ten member/F5/F6
cases. The [checkpoint](edit-sessions-checkpoint.json) binds exact compiler bytes and retained failures;
all owned instances are closed and Mac's wake helper remains active. Native namespace/cleanup and migration
hardening, remaining UX cases, Unix mutation/confinement and all release/final-review gates remain open.

**8 November: RAR5/7z password browsing and read ownership implemented, unregistered.**
[Password browsing](ARCHIVE_PASSWORD_BROWSING.md) connects masked password retry, source-bound readers,
restored-tab Refresh, nested archive/viewer lifetime and queued extraction after tab closure. All platforms pass
20 Core and 27 connected UI cases, the affected recovery/application selections and complete Core inventories.
The [checkpoint](password-browsing-checkpoint.json) binds 21 changed C# inputs and 12 DLL/PDB pairs per platform,
preserves the Mac fixture and recovery-container failures, and records zero owned instances with the Mac wake
helper retained. New writer registration remains disabled. Versioned native archive edit sessions are next;
member UX edges, Unix native mutation/confinement and the remaining release/final-review gates stay open.

**8 November: connected member/F5/F6 component matrix passes, unregistered.**
[Member commands](ARCHIVE_MEMBER_COMMANDS.md) connect exact occurrences and recursive folders, listing-revision
checks, password retry, typed destination review and skip-aware F6 source removal. All platforms pass 116
focused Core, 53 connected UI and their complete Core inventories. Windows also passes ten real member UI
cases, 217 native regressions, crash/application checks and independent readback of all ten member vectors.
The [checkpoint](member-commands-checkpoint.json) binds frozen source and DLL/PDB bytes, identifies Mac's
test-only startup repair and Windows' reader-only continuation, and records zero remaining owned instances.
Mac's wake helper remains active. Further UX/session, Unix native and release/final-review gates remain open;
no new profile is registered or qualified.

**8 November: retained target review and existing-archive Pack updates implemented, unregistered.**
[Target review](ARCHIVE_TARGET_UPDATE_UI.md) binds queued updates to a complete manifest and native source
observation, preserves existing RAR5/7z password modes and retains preparation ownership through cancellation.
All three platforms pass 28 Core preflight, 16 update UI, 20 creation UI, eight mutation preparation and 21 password
cases, the affected application selection and complete Core inventories. Windows additionally passes ten real
creation/update UI cases, ten native review/update preflights, 217 native regressions, 18 crash cases, 14
application-entry/cleanup cases and independent readback of the new archives. The [checkpoint](target-update-ui-checkpoint.json)
binds exact compiler bytes to the frozen Git candidate and records original failures, explicit Windows Core
reuse after a test-only correction, and zero remaining owned instances. Mac's wake helper remains active.
The next connected chunk covers open-archive F5/F6 and member commands. Browsing/edit password sessions, Unix
native mutation/confinement, full production qualification and the final independent review remain unfinished.

**8 November: shared creation options and encoded password admission implemented, unregistered.**
[Creation UI](ARCHIVE_CREATION_UI.md) connects explicit format/compression/7z options and masked password
confirmation to captured job inputs. All platforms pass 20 creation UI cases, 21 password cases, the affected
application regression selection and complete Core suites. Windows also passes ten real confined-worker UI
creations, 217 native regressions, 18 cleanup crash cases and independent readback of all ten new archives.
The [checkpoint](creation-ui-checkpoint.json) records source/PDB bindings, failed runs and instance cleanup.
Delayed suffix validation and refused-draft control ownership defects are fixed. No new writer is registered;
shared member/update/edit/password journeys and the remaining platform/release/final-review gates stay open.

**8 November: automatic native cleanup and creation recovery implemented, unregistered.**
[Automatic cleanup and creation recovery](ARCHIVE_AUTOMATIC_CLEANUP.md) pass the focused platform checks,
complete Core regressions, Windows native/crash/application matrix and independent readers. The
[checkpoint](native-cleanup-checkpoint.json) binds current source/PDB inputs and archive bytes and records zero
remaining owned test instances on all three platforms.
The Windows candidate cleans creation/update artifacts by default, preserves committed outcomes on cleanup failure,
and exposes creation/RAR5/7z retained-file review. No new writer is registered and the overall goal remains active.

**8 November: Windows native F6 and shared source accounting implemented, unregistered.**
[Native moves](NATIVE_ARCHIVE_MOVES.md) add captured-source removal after reviewed publication, directory
change tracking, exclusions and partial/canceled move accounting. The legacy ZIP case-distinct source accounting
fix passes on the actual Windows adapter. The [checkpoint](native-archive-move-checkpoint.json) records the complete
Core evidence, Mac's four source-preserving APFS fixture repairs, 23 native source guards, twenty actual Windows
create/update moves, 197 inherited native cases, ten application-entry cases, independent readers, source/PDB
binding and instance cleanup. Original failures and the publication-reader lifetime fix are retained. Unix native
moves, cleanup/recovery, shared UI and remaining qualification/final review stay open. No new profile is enabled.

**8 November: case-distinct native source selection fixed.**
[Source-case follow-up](NATIVE_ARCHIVE_SOURCE_CASE.md) preserves distinct file and parent-directory spellings
before native capture. The real NTFS reproduction showed two distinct files collapsed into one source by the
previous Windows string maps. Two new core cases and twenty actual application-worker cases cover the fix across
all six container formats and every RAR/7z password mode. The [checkpoint](native-source-case-checkpoint.json)
records the complete 3,866-case affected archive closure on each platform, 116 focused cases, 197 Windows native
cases, independent readers/source bindings and process cleanup. Unchanged non-archive code retains the preceding
full-core baseline. Native F6/cleanup, shared UI and the remaining qualification/final-review gates remain open.

**8 November: native manager execution and review decisions implemented, unregistered.**
[Native jobs](NATIVE_ARCHIVE_JOBS.md) connect exact catalog/member/folder scopes, multi-parent source capture,
bounded safe previews, replacement/exclusion/7z precision decisions, role-bound workers and native publication.
Existing native targets are rechecked before parsing. A complete duplicate group can relocate together;
partial/new collisions still refuse. The [checkpoint](native-archive-job-checkpoint.json) records 114 focused
cases on each platform, 12 Unix/14 Windows native job preflights, complete 8,320-case Unix/8,325-case Windows core
suites, 177 Windows native cases, ten application-entry cases, independent readers and source/DLL/PDB binding.
Original failures and validation-helper repairs are retained. Checked deltas, verified binary reuse and reader-only
continuations speed the batches while preserving coverage. Default application composition remains empty.
Safe creation cleanup, native F6, recovery UI coverage, creation/member/edit/password UI, Unix update/confinement
and the remaining qualification/final-review gates are open.

**7 November: typed production job dispatch and password ownership implemented, unregistered.**
[Job component](ARCHIVE_JOB_BACKENDS.md) adds per-manager backend composition, frozen queued changes and secret
cleanup on cancellation, admission/handoff/journal failure and completion. All 45 new cases pass on all three
platforms. The [checkpoint](archive-job-backend-checkpoint.json) binds the complete core suites (8,239 cases on
Mac/Ubuntu; 8,244 on Windows), independent readers, Windows native/application-entry selections and cleanup.
Ubuntu's first environment failure and private-keyring rerun are retained. Native backend execution, shared
decisions and UI/edit/F6 integration remain open. Default production composition is empty.

**7 November: shared parent workflow review implemented, unregistered.**
[Shared workflow adapter](SHARED_ARCHIVE_WORKFLOW_REVIEW.md) connects exact member/recursive scopes, directory
merges with provenance, duplicate replacement, exclusion review and declared 7z input precision to all six
container planners. It adds 62 focused cases; the final 3,738-case core matrix passes on Windows/Mac/Ubuntu.
The [checkpoint](shared-workflow-review-checkpoint.json) records the passed independent readers, source/DLL/PDB
bindings, 163 Windows native cases and owned-process cleanup. The subsequent typed job component is recorded
above; native executor/availability qualification and UI/password/edit/F6 integration remain open.

**7 November: 7z shared pipeline implemented, unregistered.**
[Semantic preservation and worker/native checkpoint](SEVENZIP_WORKER_PIPELINE.md) connects parent planning,
packed reuse, exact optional metadata, role-bound passwords and captured/native creation. Windows application
updates and snapshot restoration now pass all password modes. The common 3,666-case platform matrix and the
163-case Windows native selection pass. Shared production workflows/UI, Unix confinement/update and complete
resource/crash/fuzz/package qualification remain open; no RAR/7z profile is enabled.

This is an **incomplete implementation**, not format or release qualification. The controlling requirements remain
[the archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md). No new writer is enabled in the
application. An explicit new-profile job refuses instead of entering the legacy ZIP executor.

**Next coherent implementation chunk:** native ownership hardening for archive edit directories, working files,
commit snapshots, record replacement and cleanup, including malformed/future-profile recovery and inherited ACLs.
Preserve legitimate atomic editor saves, legacy ZIP/remote sessions and unknown-version working copies.
Then finish member UX edges and qualify Unix confinement/update, moves and cleanup/recovery.
Present the declared 7z precision policy and resolve remaining header compatibility gaps before enabling a profile. Reuse
passed codec/native evidence when its compiler inputs are unchanged; batch the shared UI and native acceptance
work, then complete resource/crash/fuzz/RID/package gates and the final independent review.

The 7z catalog now requires complete graph/codec endings, packed and plaintext checksums/SHA, exact sizes and a final
whole-source revision. A failed empty Deflate BCJ2 branch is fixed, with all password modes covered. Aligned nonzero
ARM64/RISC-V offsets and decoder cleanup are included. The graph's conservative memory reservation is separate from
the persistent metadata limit and does not qualify the native process envelope. See the checkpoint for official
7-Zip's Zstandard gap, the independent fallback coverage, frozen compiler inputs and retained failed phases.

Current user-requested order (7 November): the recursive-selection checkpoint is recorded in `685fe564`;
[independent compressed RAR5 feasibility](RAR5_FEASIBILITY.md), committed in `4672ac14`, supports technical go.
The equivalent [7z source/coverage analysis](SEVENZIP_FEASIBILITY.md) now selects bounded FileCat container code,
existing permitted encoders and reviewed missing public-domain codec/filter paths. It verifies 29 pinned writer/header
candidates and 21 SDK candidates; the analysis itself imports no code and establishes no runtime qualification.
Subsequent bounded implementation checkpoints are described below.

The latest working priority in plan §1.2 places RAR/7z integration next: complete strict catalogs, then connect
both formats to the shared transaction/password/create/update/edit/F6 workflows before optional XZ/ISO work and
the final independent review. The [strict RAR5 logical catalog](RAR5_LOGICAL_CATALOG.md) now implements complete
content/end/filter observations, while preservation admission and the corresponding strict 7z catalog remain open.
Its validation record distinguishes the initial fixture-helper failures, focused correction and decoder-lifetime
follow-up; component evidence does not enable either format in the application.

The subsequent [strict 7z framing/metadata checkpoint](SEVENZIP_PHYSICAL_FRAMING.md) observes exact raw metadata,
coder graphs and physical ownership, including bounded compressed/encrypted headers. It also closes partially
constructed decoder graphs on failure. That checkpoint left logical member/header codec-ending checks and
preservation admission open, with encoded-header ending verification explicitly pending.

The next [LZMA/LZMA2 verification component](SEVENZIP_LZMA_VERIFICATION.md) adds exact marked/markerless finish,
compressed-byte consumption, chunk/history/property checks and owned-buffer cleanup. Its independent official-SDK
comparison covers 224 accept/refuse vectors. It supplies a prerequisite for the complete 7z catalog; subsequent
graph integration is recorded below, and the component does not enable a production writer.

The [complete 7z header verifier](SEVENZIP_HEADER_VERIFICATION.md) now connects that strict LZMA/LZMA2 component to
the encoded-header graph, with exact Copy and AES consumption, bounded padding and aggregate decoder reservations.
Successful framing observations require complete header endings. Logical member graphs and the complete strict
catalog still remain open; no production profile is enabled.

The [PPMd7/BCJ2 strict verification components](SEVENZIP_RANGE_VERIFICATION.md) add exact known-size range endings,
all four BCJ2 branches, shared consumed-byte accounting and owned PPMd model cleanup. The final 102 new cases and native
SDK comparisons cover positive and malformed streams on all three platforms, with LZMA regression repeated after
the shared input change. Complete member graph/catalog integration remains open.

The [raw-Deflate verification component](SEVENZIP_DEFLATE_VERIFICATION.md) now checks complete final blocks,
exact sizes/consumption and real history. It fixes backward copies before produced history, pooled-window cleanup
and the separate vendored encoder's sync/async leave-open finalization. All 75 new cases pass within the 2,849-case
platform matrices; independent native zlib agrees on 265 vectors on every platform. Initial fixture/assertion and
Unix comparison-launch failures are retained separately. Complete graphs/catalog and shared RAR/7z adapters remain
the immediate integration work; no new profile is enabled.

The [RAR5 preservation assembly component](RAR5_PRESERVATION.md) now represents exact optional semantic metadata
and archive/header features, adds per-member packed SHA-256 witnesses to the complete logical catalog, and assembles
retained compressed/encrypted content together with newly encoded inputs. The 115-case component matrix and
independent metadata/native-reader checks exercise creation, rename/reassembly and mixed records. Parent preservation
planning, worker/runtime secret consumers, native publication and application workflows remain open; this component
does not register a new format.

The subsequent [RAR5 parent/stream rewrite checkpoint](RAR5_TRANSFORM_PLANNING.md) now derives trusted expected
manifests from exact occurrence edits and reviewed numeric inputs, verifies the entire source before output,
assembles retained/new records, and checks the whole source again afterward. It covers rename/replace/delete/add
together across all methods/password modes, including wrong-password/remove-all and bounded final-EOF behavior.
The final 49 new cases pass in 2,898-case platform matrices with independent metadata/native-reader checks. Shared
worker/secret consumers, captured-owner/native publication and application/UI integration remain open; no new
profile is registered. This RAR work advances independently of the remaining 7z member graph/catalog.

The [RAR shared worker/password pipeline](RAR5_WORKER_PIPELINE.md) now carries those plans through real parent
clients and distinct transform/assessment/fresh-verification subprocesses. Fixed protocol-2 endpoints consume
nonce/role/request-bound one-use grants with job-local crypto budgets; protocol-1 TAR bytes remain unchanged.
Parent name admission matches the existing 64 KiB UTF-8 RAR policy, and IO allowances cover both encoding passes.
The corrected 60-case component passes within the 2,958-case platform matrices, with independent metadata/native
reader checks. Initial test/admission failures are preserved separately. Native protocol-2 bootstrap/resource/package,
captured-owner/publication and application/UI qualification remain open; no new format is registered.

The [RAR captured/native pipeline](RAR5_NATIVE_PIPELINE.md) now retains capture-owner costs and directory evidence,
borrows job-owned passwords for fresh role-bound grants, and performs actual native capture/sealing/new-file
publication on Mac/Ubuntu/Windows. Windows application protocol-2 workers also perform identity-preserving NTFS
updates and fresh retained-snapshot restoration in all password modes. The 2,994-case broad platform matrices pass;
affected-case follow-ups pass 18 native RAR cases on each Unix system and 24 on Windows, plus ten application-entry
cases. Test-budget/read-sharing corrections and a static compiler-audit coverage correction are documented and frozen
separately. Unix production confinement/update, full UI/password/edit/F6 workflows, all release qualification gates
and complete 7z member graph/catalog/shared integration remain open. No new profile is enabled.

The [RAR/7z public-options checkpoint](RAR_7Z_PUBLIC_OPTIONS.md) advances shared integration: explicit version-2
codec/filter/header/method and password modes now participate in every canonical request, digest, sidecar, creation
witness and journal binding, with version-1 bytes preserved. Public options map to existing stream encoders and
one-use password sessions, and the encrypted output tests use that adapter. Complete container preservation/
transaction/worker/UI workflows and runtime role-bound secret consumption still remain open.

The priority decision is in plan §4.3.2: bounded RAR C# implementation first, then needed 7z codecs/filters/container;
strict catalogs and shared worker/transaction/capture/resource integration; complete workflows/UI; large guarded
three-platform qualification batches plus actual six-RID/package/fuzz gates; final independent UX/UI/function/security
review and fix/retest. Prepare coherent source/dependency/runner packets and collect evidence/cleanup in each batch.
Do not create one machine session per small edit or omit needed coverage because upstream lacks an encoder.

Both encoders require explicit method/version/filter/mode inventories and all needed feasible encoding paths. Gaps
must distinguish missing work, profile policy and demonstrated blockers. RAR methods 0–5 and 7z's seven current
codecs, one-stream filters/swaps, compressed headers and four-stream BCJ2 now have implementation checkpoints. Remaining
version/filter/metadata inventories and broader qualification remain work, not impossible features. 7z FILETIME's 100 ns limit
and over-budget dictionaries/models need explicit policies. Neither analysis enables a production profile.

The user chose option 3 with fallback to option 1: a documented feasibility no-go authorizes stored RAR5 and optional
installed `rar` compression where lawful and qualified. The primary route has not received a no-go; fallback is not
activated. The goal remains active and implementation/release qualification and independent final review remain open.

The subsequent 7 November instruction makes **password support required for both RAR and 7z**, superseding initial
unencrypted-only policy. Plan §4.3.1 requires payload/header encryption, password-aware read/browse/extract/update/edit
and fresh verification, bounded KDF/cipher handling, session-only secret grants and complete UI/negative/native tests.
Public password flows and reader refusal text remain integration gaps even with the internal cipher foundation below. Passwords/keys
must not enter persistent settings, journals/logs/evidence or process arguments.

The [bounded C# RAR5 codec/creation checkpoint](RAR5_CSHARP_ENCODER.md) now implements store and genuinely distinct
bounded search presets for methods 1–5. All 23 new cases pass on Windows/Mac/Ubuntu within 444-case regression
batches, and independent native APIs verify all 84 generated records per platform. The six archive hashes match
across all three systems. It remains stream-only and unregistered: required password/data/header encryption,
strict update/source/verification catalogs, worker/secret grants, preservation, workflows and full gates remain open.
See that record for exact evidence and corrected harness failures; this is not complete RAR feature support.

The [7z container/codecs and password-derivation checkpoint](SEVENZIP_CSHARP_CODECS_AND_KDF.md) now writes Copy,
LZMA/LZMA2/BZip2/Deflate/Zstandard using streamed raw headers and bounded presets. All 21 new 7z and 19 KDF cases
pass within the 522-case batches on all three platforms; independent APIs verify 42 records per platform and archive
hashes match. Mac name-presentation normalization is checked against exact raw UTF-16 names. These are not full 7z
or password features: PPMd/filters/compressed headers, actual cipher/header/read/secret-grant integration and remaining
workflow/qualification gates stay open. Profiles remain unregistered. The later checkpoints also test simultaneous
composed/decomposed names; all owned test processes are closed and other testers are preserved.

The [PPMd7/filter checkpoint](SEVENZIP_PPMD_AND_FILTERS.md), committed in `1466b3c7`, implements bounded PPMd7,
delta, branch and swap encoding with 19 exact SDK comparisons and independent three-platform container checks.
The [streamed-header checkpoint](SEVENZIP_STREAMED_HEADERS.md), committed in `47a9c86c`, adds raw/compressed headers,
including a 1,304,142-byte raw header, exact independent metadata readback and 574-case passing platform matrices.
These are foundations with recorded method/reader gaps, not completed AW6 or public feature availability.

The [password foundation](ARCHIVE_PASSWORD_FOUNDATION.md) now creates and reopens encrypted RAR5 and 7z, including
optional hidden names, all current codecs and raw/compressed 7z headers. Each platform's 653-case matrix has zero
failures; independent readers verify 52 encrypted archives/194 payloads through official 7-Zip and 29 encrypted
7z archives/196 metadata records/140 payloads through py7zr, including Zstandard. Every transferred evidence file
is manifest-verified. Current internal sessions bound and dispose password/KDF/cipher state; versioned worker secret
grants, view/edit lifetime, raw catalogs, shared transactions, complete UI, BCJ2, legacy cipher inventory and the full
resource/security/crash/fuzz/six-RID/package gates remain required. No new profile is registered. The next shared
integration chunk includes password-aware catalog/verification contracts rather than freezing interfaces around TAR.

The [worker secret-grant foundation](WORKER_SECRET_GRANTS.md) adds explicit protocol 2, one-use role/request-bound
password frames, owned-buffer clearing and explicit JSON nonserialization. The full affected matrix passes 770 cases
with platform skips and zero failures on each machine. Nine real child delivery/refusal/cancellation journeys pass
for all three roles. A focused replay qualifies a create-only recorder fix after Windows PID reuse overwrote one
exit record; all nine separate records now survive per machine. Both source/evidence sets and all current C# inputs
are verified. This is shared transport/API work: actual password-aware request consumers, native secret-role admission,
parent job/view/edit lifetime and complete UI remain open.

The [BCJ2 checkpoint](SEVENZIP_BCJ2.md) adds fixed-buffer raw encoding and four sequential packing passes with one
active compressor, frozen-source checks and four distinct encryption IVs. The full matrix passes 846 cases with
platform skips; the follow-up 81-case suite covers constructor/disposal failures as well as all codec/header/password
combinations. SDK checks match 72 streams; official 7-Zip verifies 36 archives/72 payloads, and composed independent
py7zr/AES/codec plus SDK BCJ2 checks verify all 42 graph payloads including Zstandard. Libarchive qualifies zero of
these graphs; its failures are retained. Direct tests fix Deflate small-read data loss and encrypted BCJ2 stream
cleanup. All 30 earlier unencrypted archives remain unchanged; SBOM controls and actual compiler-source audits pass.
Source/evidence identities and the exact standard-user setup corrections are recorded in that checkpoint.

The next chunk is strict password-aware RAR/7z source/update/verification catalogs and typed shared worker/profile
integration, including four-pass BCJ2 traffic/CPU/decode accounting and aggregate memory ownership. Continue through
shared transactions, complete UI/workflows, native/resource/crash/fuzz/six-RID/package gates and final independent
review. No new profile is registered or complete; the explicit goal remains active.

The [RAR5 physical framing checkpoint](RAR5_PHYSICAL_FRAMING.md) now observes complete admitted framing with exact
metadata presence, occurrences, timestamps, raw native owner bytes, encryption descriptors and physical revision
hashes. All 62 new cases pass within the same 913-case matrix on Windows/Mac/Ubuntu, including truncated prefixes,
header reservation/size limits, cumulative metadata/member limits and mid-payload cancellation. Current C# inputs,
all three frozen bundles and the writing-only dependency closure are verified. This physical catalog still needs
strict decompression/end/filter/checksum/plaintext-SHA verification before it can produce a complete assessment;
preservation-compatible writing and the remaining shared/native/workflow gates stay open.
The same record includes a 334-case boundary follow-up: RAR creation now respects the reader's 64 KiB UTF-8 name
limit before IO and chunks large headers to the proxy ceiling. All six new cases pass on every machine, official
7-Zip verifies the three accepted boundary archives/payloads, and the pinned old assembly reproduces five expected
failures. Existing six archive hashes remain unchanged; all six full/follow-up bundles are locally manifest-verified.

## Source and workspace

- Baseline: `4282929753f062f5bb2cc130e8316b133e5c8e03`.
- Branch: `codex/archive-writing`.
- Isolated worktree: `E:\FileCat\.tmp\worktrees\archive-writing`. The shared `E:\FileCat` checkout remains on `main`.
- The baseline plan was an untracked user-provided file; this branch retains it with the user's authorized additions for local move-into and feasible licence-compatible RAR writing before final review.
- Implementation checkpoints: `a8313365` (contracts/proxy IO) and `7e63b39e` (strict TAR/gzip transforms).
- The transferred TAR/gzip foundation delta (before the later native-worker changes) has SHA-256
  `70E85DE4D5DA731DF0C38DD814E1253B87F2D822CE1172CFA02D65DC2BFE2CDA`.
  It contains those foundation production and test sources, applied over the baseline source archive.

## Implemented foundations

The [bounded recursive-selection checkpoint](RECURSIVE_SELECTION_CAPTURE.md) freezes exact roots, uses native
directory enumeration and lazy ancestor-bound file leases, preserves empty folders and records explicit exclusions.
Native destination-ancestor checks refuse staging inside excluded source subtrees. Mac/Ubuntu suites, 220 native
Windows regressions, ten final recursive Windows cases and eight actual Mac-hosted ARM64 VM helpers pass; independent
readers verify all four TAR-family outputs. Global preparation/snapshot/resource ownership, full review UI and all
remaining format/RID/package/fuzz gates remain open; unsupported-name samples need finer leaf detail before UI use.

The [shared Linux native-open follow-up](UNIX_ARM64_OPEN_FLAGS.md) fixes remaining x64-only flags in Unix archive
storage, mount-info reads and POSIX ACL directory opens. Actual ARM64 guest directory/file/link probes pass as part
of 29 exact kernel checks; all 16 archive roles and lifecycle suites pass. The affected 211-case suite passes with
explicit platform skips on Windows, Mac and Ubuntu. Host/provider memory and the remaining production gates stay open.

The [dedicated VM archive-worker checkpoint](DEDICATED_VM_ARCHIVE_WORKER.md) adds a non-UI worker and writing-only
dependency graph, strict shared guest bootstrap/transport and canonical locked musl publication. Live ARM64 guests
exposed and fixed architecture-specific open flags and musl clone/signal bugs. Both tested bundles complete 16 actual
roles with independent extraction; 23 kernel-policy checks and extra-process startup refusals pass. Focused policy/TAR
regressions pass 51 tests on each desktop platform. Compiler hashes verify 810 source documents per bundle. Host/provider
memory, production packaging/admission and remaining plan gates stay open; this enables no writer profile.

The later [captured-member defaults checkpoint](CAPTURED_MEMBER_DEFAULTS.md) corrects ordinary Unix permission bits
and Windows 0644 defaults, with passing native regression checks on all three platforms. It does not complete capture.
The subsequent [exact TAR timestamp checkpoint](EXACT_TAR_TIMESTAMPS.md) adds native nanosecond capture, versioned
input/member metadata and a bounded PAX encoder adapter, with three-platform regression/native and independent-reader
checks. Complete capture/publication/production integration and release qualification remain unfinished.
The subsequent [preparation-space checkpoint](PREPARATION_SPACE_ADMISSION.md) checks the complete stage/evidence,
known snapshots and safety margin against capacity from the retained native destination, before intent or artifact
creation. Unknown capacity remains explicit; full capture/space UI, allocation guarantees and qualification remain open.
The later [Unix visible-attribute checkpoint](UNIX_EXTENDED_ATTRIBUTE_INVENTORY.md) adds bounded descriptor-based
name/value inventories to native rereview and origin/rename reconciliation. Hidden policy coverage, complete outer
admission/application, publication and production integration remain unfinished.
The later [private new-file publication checkpoint](PRIVATE_NEW_FILE_PUBLICATION.md) adds schema-5 creation evidence,
native exclusive claim and conservative reconciliation for creator-private, unmarked new archives. It retains all
artifacts and leaves profiles disabled; complete metadata admission, marked creation, replacement, cleanup/recovery,
the full crash matrix and production qualification remain unfinished.
The later [creation-provenance integration](MARKED_CREATION_PROVENANCE.md) binds sealed origin records to the exact
captured sources, applies a canonical Windows aggregate before fresh verification and supports private marked Windows
creation through the unregistered publication bridge. Unix marked creation, full metadata preservation, replacement,
cleanup/recovery and production qualification remain unfinished; this does not enable a writer profile.
The [Windows resource-policy follow-up](WINDOWS_RESOURCE_POLICY_OBSERVATIONS.md) captures resource attributes and CAP
identifiers with the retained security snapshot, detects value changes without payload/time/DACL changes, and refuses
a late changed stage before publication. Audit/other unqueried security state, metadata application and existing-file
replacement remain unfinished.
The [NTFS transactional-content investigation](WINDOWS_TRANSACTIONAL_CONTENT_SPIKE.md) passes 12 owned native probes,
including a read-only pin across commit and independent preservation checks for seeded audit/resource/CAP descriptors.
It changed no production route or source-lease contract. At that investigation checkpoint, the typed recipe, old-data
snapshot, journal/recovery, hard-link, durability and application integration were still required for qualification.
The subsequent [journaled NTFS integration](WINDOWS_TRANSACTIONAL_CONTENT_INTEGRATION.md) adds the separate typed
content-update recipe, verified private original-byte retention with provenance, exact preparation/input binding and
the transaction-to-normal-reader pin transfer. All 88 Windows confined-worker/native cases pass, including 20 new
actual update cases across the four TAR profiles; common recovery/UI regressions pass on all three platforms. One
native-discovered identity/digest contract mismatch is fixed and the failed evidence retained. Complete crash/recovery/
cleanup, broader metadata/resources/aliases and production admission remain unfinished; the bridge stays unregistered.
The [NTFS process-crash follow-up](NTFS_CONTENT_PROCESS_CRASHES.md) moves that coordinator behind typed native
contracts and validates sixteen actual externally killed snapshot/prepare/commit/rollback boundaries. All 104 Windows
native cases pass; common/UI and prior creation/crash regressions pass on all three platforms. Recovery retains every
surviving artifact, including a committed file whose outcome append was interrupted. Active restoration/cleanup,
deletion crash points, broader filesystem/resource/alias qualification and the remaining plan are still unfinished.
The [live NTFS cleanup follow-up](LIVE_NTFS_ARTIFACT_CLEANUP.md) persists a complete sealed-artifact catalog before target
mutation and implements creator-owned, object-bound unlink with durable per-object intents/outcomes and the original
snapshot last. All 131 Windows native cases pass, including twenty deletion crashes; common/UI and prior regressions
pass on all three platforms. Completed cleanup clears only its own recovery hold. Interrupted-cleanup resumption,
native recovery/restoration, broader qualification and production workflows remain unfinished.
The [NTFS cleanup-recovery follow-up](NTFS_CLEANUP_RECOVERY.md) adds an exclusive retained journal reopener, fresh
native target/catalog inspection and a separate bounded reconciliation history distinguishing confirmed unlinks from
observed absence. Eight actual crash-to-recovery paths and five changed/stale/busy/incomplete refusals pass within all
144 Windows native cases; common/UI regressions pass on all three platforms. Recovery preserves archive bytes and can
finish exact private cleanup.
The [recovery-process crash follow-up](NTFS_RECOVERY_PROCESS_CRASHES.md) adds shared reconciliation sequencing and
31 actual recovery-parent kill cases, covering all six intent/unlink/outcome boundaries and persisted failure holds.
All 175 Windows native cases and the three-platform common/UI regressions pass. Restoration, production actions and
broader gates remain unfinished; profiles stay disabled.
The [ordinary Windows standard-user follow-up](WINDOWS_STANDARD_USER_QUALIFICATION.md) passes 170 native cases with
five expected administrator-only skips, with a loaded real user profile and raw token-group proof of absent Admin SID.
It also fixes the validation transfer procedure: complete app/probe packages and all 469 actual hashes are checked,
including all 261 app files against SPDX. Earlier host inventories do not establish exact incremental runtime transfer.
This closes that explicit identity/artifact validation gap; the broader implementation and qualification gates remain open.
The [fresh snapshot-restoration follow-up](SNAPSHOT_RESTORATION.md) uses new confined assessment/verification, a fresh
job and native transaction, and a separate current-version rollback copy before restoring exact retained original
bytes. It adds durable linked restoration facts, restored-target cleanup reconciliation and read-only reporting.
All 187 administrator native cases and 182 standard-user cases pass (five administrator-only skips); common/UI
regressions pass on Windows, Mac and Ubuntu. Restoration process-crash/failure qualification, production actions
and the broader gates remain unfinished; writer profiles stay disabled.
The [restoration-process crash follow-up](SNAPSHOT_RESTORATION_PROCESS_CRASHES.md) passes twelve actual external
parent-termination boundaries and a fresh retry after uncertain intent. It reproduces and fixes a retry refused
because a fresh write has a new modification-time metadata digest, while enforcing distinct job/journal/transaction
identities and immutable same-attempt outcomes. All 200 administrator native cases and 195 standard-user cases pass
(five administrator-only skips), with passing common/UI/independent-fixture checks on Windows, Mac and Ubuntu.
Cancellation/failure matrices, restoration cleanup-deletion crashes, production actions and wider gates remain open.
The [restoration cancellation/failure follow-up](SNAPSHOT_RESTORATION_FAILURES.md) passes nine additional actual
native cases before/after commit and during backup handling, with separate exact catalog reconciliation and retained
uncataloged evidence. All 209 administrator native cases and 204 standard-user cases pass (five administrator-only
skips), with passing Mac/Ubuntu builds and Windows-only guards. No production fix is needed for these cases. Broader
fault/resource coverage, restoration cleanup-deletion crashes and the remaining production/release gates stay open.
The [restoration cleanup-crash follow-up](SNAPSHOT_RESTORATION_CLEANUP_CRASHES.md) passes all eighteen per-artifact
intent/unlink/outcome kill points. Fresh scopes distinguish observed absence from confirmed deletion, reconcile each
catalog separately and preserve the old source snapshot through new-job cleanup. All 227 administrator native cases
and 222 standard-user cases pass (five administrator-only skips), plus passing Mac/Ubuntu builds and Windows guards.
No production fix is needed. Wider fault/resources, metadata/security/aliases/durability, production recovery actions
and remaining format/platform/workflow gates are unfinished.
The [retained archive cleanup action](RETAINED_ARCHIVE_CLEANUP_UI.md) connects fresh native NTFS review to an explicit
keep-archive/cleanup approval dialog, exact reconciliation and retained closure reports. It fixes a clipped action
found on the actual Windows guest; 273 common UI cases pass on each platform, two actual Windows native UI cases pass,
and all 227 administrator/222 standard-user native cases pass (five administrator-only skips). The guest desktop
confirms safe Enter cancellation, exact six-file cleanup and unchanged archive/origin bytes. Further drawer guidance,
report accessibility/clipping, restoration UI and the broader implementation/qualification gates remain open.
The [drawer follow-up](ARCHIVE_CLEANUP_DRAWER_FOLLOWUP.md) resolves those reproduced guidance, report-row accessibility,
completed-button clipping and closure-heading defects. All three platforms pass 276 full UI cases; Windows also passes
two actual native UI cases, 33 standard-user focused cases and the repeated 227/222 native matrices (five privileged
skips). The final guest desktop confirms Cancel preservation, exact approved cleanup, the concise accessible row and
visible report action/consistent viewer, then exits normally. Restoration UI and the wider gates remain unfinished.
The [private creation process-crash follow-up](PRIVATE_CREATION_PROCESS_CRASHES.md) passes ten new publication/metadata
boundaries and thirteen neighboring crash cases on each platform. It leaves production and recovery authority unchanged.
The [TAR.GZ worker pipeline](TAR_GZIP_WORKER_PIPELINE.md) adds strict wrapper-aware assessment, streamed create/update
transformation and fresh verification, with fixed-pass replay and native preparation/publication cases. Application
registration, update publication and the remaining release gates stay unfinished.
That checkpoint also retains two intermittent Ubuntu controlled-worker monitor refusals as an open reliability
investigation, despite the subsequent passing broad/native replays. Their cause has not been established.
The subsequent [catalog/lifecycle follow-up](TAR_GZIP_CATALOG_REUSE.md) removes a duplicate retained gzip-update
catalog and tightens compressed replay to four physical passes. It preserves monitor/terminal/conversation causes,
and reproduces and fixes an exited-process CPU observation bug in the controlled test child. The two older refusals
did not capture their precise cause; complete production lifecycle/release qualification remains unfinished.
The [TAR.BZ2 worker checkpoint](TAR_BZIP2_WORKER_PIPELINE.md) adds strict single-stream assessment, streamed rebuild,
fresh verification, preserved block size, bounded buffered proxy IO, independent bidirectional fixtures and native
preparation/private-creation checks. Format enablement and complete common/platform release gates remain unfinished.
The [TAR.ZST worker checkpoint](TAR_ZSTD_WORKER_PIPELINE.md) adds pre-codec frame/window/block admission, bounded
streamed rebuild, checksummed output, separate fresh verification, pinned independent C interoperability and native
huge-window refusal. A resource-scoped test collection fixes accidental oversubscription of the unchanged bounded
shared queue. Full workflows, common transactions, worker/platform and release qualification remain unfinished.
The [cooperative cancellation checkpoint](WORKER_COOPERATIVE_CANCELLATION.md) adds a separate EOF-only lifetime pipe,
bounded interruptible worker protocol IO and a two-second grace before native termination, with queue ownership through
actual reap. It fixes a reproduced Unix pipe-disposal deadlock. Fresh Windows/Mac/Ubuntu regressions, independent
archive readback, actual Windows LPAC/application and Linux containment cases pass. Complete workflow, cancellation/
publication races and platform/release qualification remain unfinished.

| Area | Current behavior | Remaining gate |
|---|---|---|
| Typed contracts | Explicit versioned profiles/enum IDs, raw occurrence keys, portable names, cumulative member/metadata/heap/logical budgets and TAR request/manifest/receipt wire | Other-format schemas, production service registry and session contracts |
| Framed protocol | Nonce, version, sequence, reserved-field/opcode, frame-size, cumulative payload/metadata and finite frame-count checks before payload allocation; bounded fragmented records; poisoned channels cannot resume; distinct TAR assessment/transform/verification roles; separate EOF-only cancellation pipe and bounded interruptible worker IO | Other formats, full cancellation/publication races and final platform/package qualification |
| Canonical manifests | Frozen ordered physical members, contiguous exact duplicate occurrences, profile/features and separate full/content/metadata digests; aggregate name/heap/metadata/logical budgets; shared incremental hashing | Complete format feature inventories, rebuild/verification wire schemas and native sidecar integration |
| Manifest sidecar codec | Scoped versioned stream encoding, incremental bounded review, complete EOF/semantic/file checksums, poisoned failure state; no pathname authority | Native private-file leases/permissions/durability, transaction integration and exact-object recovery/cleanup |
| Windows archive storage | Unregistered held NTFS directory/file leases, exclusive private ACL-at-create stages/sidecars, bounded sealed streams, complete object versions, no-replace handle rename and exact reviewed deletion; [native evidence](WINDOWS_ARCHIVE_STORAGE.md) | Typed provenance/outer metadata, durable journal/publication/recovery integration, directory-entry durability, global lease accounting, other filesystems/platforms and release qualification |
| Typed origin evidence | Absent/present/unavailable raw snapshots; held Windows ADS and Mac/Linux xattr reads; private sealed-stage create-only copying with post-rename capability closure; [Windows checkpoint](ARCHIVE_ORIGIN_CHECKPOINT.md), [Unix observations](UNIX_ORIGIN_OBSERVATIONS.md), [Unix application](UNIX_ORIGIN_APPLICATION.md), [bound Windows creation application/publication](MARKED_CREATION_PROVENANCE.md) | Unix semantic classification/strongest-input aggregation, full promised metadata application, directory capture, global budgets, update reconciliation and release qualification |
| Windows outer observations/guards | Frozen held-file owner/group/DACL/label/resource-attribute/CAP observations, exact creation/mtime, attributes/link count/origin; mandatory same-object metadata checks before/after full-hash namespace review; [initial evidence](WINDOWS_ARCHIVE_METADATA.md), [resource-policy evidence](WINDOWS_RESOURCE_POLICY_OBSERVATIONS.md) | Audit/other unqueried security-policy admission, promised metadata application/prepared-publication state, aggregate budgets, durable publication/recovery and release qualification |
| Unix outer observations | Held regular-file owner/group/mode, nanosecond mtime/ctime/optional birth, link count, native flags, bounded raw ACL and typed origin; [checkpoint](UNIX_OUTER_METADATA.md) | Complete security/xattr admission, promised metadata application, global budgets, publication/reconciliation and release qualification |
| Unix archive storage | Held directory/regular-file leases, private-at-create ext4/APFS artifacts, bounded sealed streams, native sidecar roundtrips, reviewed no-replace rename and checked sync calls; exact unlink deliberately refused; [checkpoint](UNIX_ARCHIVE_STORAGE.md) | Full metadata/origin application, durable journal/reconciliation, reviewed replacement/rollback, exact cleanup or retained-artifact workflow, global budgets and release qualification |
| Declared Windows origin zone | Closed bounded UTF-8/UTF-16LE declaration parsing, exact-byte non-decreasing updates, URL-free zone-only output; independent Windows reader corrected encoding admission; [declaration evidence](WINDOWS_DECLARED_ORIGIN_ZONE.md), [complete captured-input binding and native creation integration](MARKED_CREATION_PROVENANCE.md) | Existing-file native application/reconciliation, Unix representations, directory capture and release qualification; a declaration is not an effective trust/policy decision |
| Streamed provenance aggregation | Scoped bounded source-version/raw-mark sidecar, complete EOF/checksum review, all-input Windows declaration aggregation with retained raw URLs, native private-file reopen/seal/copy test; [evidence](ORIGIN_EVIDENCE_AGGREGATION.md) | Durable transaction references/retention, reviewed source-plan binding, global budgets, native Unix representations, final metadata application and actual publication/recovery qualification |
| Proxy IO | Child uses fixed input IDs and bounded stage requests; independent views preserve retained native owners; parent checks offsets/lengths/IO amplification before streams; no worker namespace authority | Scalable native capture/snapshot repositories, global budgets and production launch integration |
| Queue/lifetime | One active in-process reservation and at most eight pending; all three roles share exit, diagnostics/CPU, deadline, cancellation/termination and reaping checks | Cooperative cancellation, cross-parent scheduling, global budgets and operation-center phases |
| Legacy ZIP publication extraction | Separate compatibility-only publication/rollback/cleanup boundary; existing codec and journal ordering retained; explicit profiles refused before intent | Object-bound publication, typed provenance, durable archive facts/recovery and separately reviewed ZIP worker migration |
| Archive journal facts | Immutable bounded versioned snapshots, durable transitions, schema-2 provenance reservations/finalized versions/counts, conservative legacy/damaged/unknown/out-of-order retention and read-only reporting; [provenance journal checkpoint](ARCHIVE_ORIGIN_JOURNAL.md) | Native sidecar storage/integration, object-bound recovery/actions and actual publication/crash integration |
| Archive recovery UI guard | Fresh journal reads at every entry point; generic archive mutations refused; read-only reports plus explicit native NTFS retained-file review/approval and exact cleanup for complete supported catalogs; reproduced drawer/report findings fixed | Restoration action, Unix/native adapters, complete archive workflow/accessibility and package qualification |
| Windows boundary spike | Suspended restricted LPAC, actual-token/access checks, exact inherited pipe list, private-profile write protection, finite job commit/CPU, bounded stderr and idempotent kill/reaping; actual guarded application entry tested | Full engine workloads, other namespace/asset/setup-race probes, ordinary user and installed-package/RID qualification |
| Linux boundary development lane | Unregistered stream-only launcher/worker, native delegated cgroup memory/pids, finite CPU, pidfd/parent-death lifetime, all-thread seccomp and Landlock, dedicated cancellation pipe, bounded stderr and exact empty-controller cleanup; [native checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) | Production entry/roles, complete package/update/descriptor/resource/cancellation races, ordinary desktop routes, ARM64/other filesystems/kernels and release qualification |
| TAR assessment process | Actual early entry, numeric read-only source proxy, bounded physical SHA/raw TAR/runtime-decoder passes, checked catalog phases and terminal digest; shared parent watchdog, cooperative cancellation and final drain/reaping checks | Qualified AppServices registration, reviewed input leases, full cancellation/publication integration |
| Fresh TAR verification process | Separate role/nonce/process derives the complete manifest without the parent's expectation; complete physical reads, cooperative cancellation, terminal/drain/reaping checks and exact manifest comparison, now bridged to sealed native stages | Other formats, full native/package qualification, scalable capture, metadata/journal/publication integration and full cancellation races |
| Parent TAR expectation | Frozen positive numeric inputs, exact occurrence edits and flattened additions; complete source admission, combined source/input budgets, independent expected order/content/metadata/features before encoding; [evidence](TAR_PARENT_EXPECTED_MANIFEST.md) | Reviewed native source/folder capture and lease binding, complete output-bound audit, production integration and complete release qualification |
| TAR transform process | Typed bounded request/receipt, per-reference input aliases, finite parent stage/IO ceiling, isolated engine execution, cooperative cancellation, terminal/reaping and parent manifest comparison; [evidence](TAR_TRANSFORM_WORKER.md) | Complete profile-bound audit, scalable capture, metadata/journal/cancellation integration, production contracts/registration and release qualification |
| Retained native TAR stage | Native object/content/metadata bindings, disposable independent read views, exact-bound fresh artifact claim, actual sealing, fresh verification and final stage/all-input rereview; [evidence](RETAINED_NATIVE_STAGE_PIPELINE.md) | Full metadata/origin admission/application, scalable capture/global budgets, durable journal/publication/recovery and production/qualification integration |
| Durable stage creation | Exact single-attempt native reservation; versioned flushed intent before creation and immutable identity/bound binding after creation; conservative recovery/reporting and real external process-termination checkpoints; [evidence](STAGE_CREATION_RESERVATION.md) | Remaining publication/recovery integration and full crash/durability/production qualification |
| Journaled native TAR preparation | Frozen schema-4 sidecar reservations, private native assessed/expected/provenance/observed manifests, complete readback, source/destination binding and durable Rebuilding/Finalizing/Verifying with distinct finalized/verified stage versions; [checkpoint](JOURNALED_TAR_PREPARATION.md) | Full capture/defaults/global budgets, metadata/origin application, publication/recovery, production workflows and full qualification |
| TAR assessment | Full raw USTAR/PAX framing, checksums, exact UTF-8, numeric/PAX grammar and allowlist, pre-allocation metadata limits, complete payload hashes, end markers and padding; independent runtime-decoder agreement | Coverage-guided campaigns, complete metadata/preservation corpus, scale measurements and native independent tools |
| TAR transform | Stream-only create and exact per-occurrence replace/delete/rename; whole-source namespace/logical budgets; order/duplicate/metadata preservation; native stage, bounded worker and fresh verifier roundtrips | Durable safe save/metadata/provenance, scalable folder preparation/review and production dispatch |
| Gzip/TAR.GZ | Neutral single-member framing, explicit DEFLATE terminal status/consumption, CRC/ISIZE, bounded expansion; streamed create/update and fresh verification, fixed-pass replay and independent fixtures | Production registration, complete bound/scale/fuzz/wrapper corpus and inherited publication/release gates |
| BZip2/TAR.BZ2 | Complete single-stream CRC/footer/padding/EOF admission, bounded expansion, streamed create/update/fresh verification and preserved block size; native preparation/private-creation and independent fixtures | Production registration, complete bound/scale/fuzz corpus and inherited metadata/publication/release gates |
| Zstandard/TAR.ZST | Pre-codec single-frame/window/block admission, complete decoder/checksum/EOF validation, bounded streaming and checked encoder options; create/update/fresh verification, native private creation and independent C fixtures | Production registration, complete bound/scale/fuzz corpus and inherited metadata/publication/release gates |

The raw gzip inflater is the already-vendored SharpCompress/DotNetZip component, whose existing MS-PL/BSD attribution
is retained. It is used because the runtime DeflateStream API does not expose a terminal-state/consumed-byte result.
The encoder remains the runtime DeflateStream. No upstream source/package was added or upgraded. The BZip2 follow-up
updates the fork's reachable-code SBOM description and passes the Windows development-payload notice/SBOM validators.
Complete reachability review and exact release-artifact SBOM/notices for every enabled RID remain required.

## Observed checks, 4–5 November 2026

The test command is `dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-restore` with filter
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget` and a TRX logger.
SDK: `10.0.101`. Raw logs/TRX files are retained under the worktree's `.tmp/test-results/archive-writing` and the owned guest
validation directories; they are not evidence of installed-package or GUI qualification.

| Environment | Baseline result | Current-source result | Scope/limitations |
|---|---|---|---|
| Windows x64 host | 506 passed, 28 skipped, 0 failed | 592 passed, 28 skipped, 0 failed (620 total) | Includes real-child TAR assessment and protocol/lifecycle adversaries; no GUI/package qualification |
| macOS ARM64, macOS 27.0.1 / APFS | 515 passed, 19 skipped, 0 failed | 601 passed, 19 skipped, 0 failed (620 total) | Owned source checkout; SDK 10.0.101; controlled child tests do not qualify a signed confined worker |
| Windows 11 x64 VM, build 26340 | Not replayed natively | 592 passed, 28 skipped, 0 failed (620 total) | Host-built binaries; actual .NET 10.0.14 and medium integrity verified; no GUI/package qualification |
| Ubuntu VM | See later replay below | See later replay below | Initially postponed for another tester; subsequently available with explicit user authorization |

The new tests include 1,000 checksum-repaired TAR mutations and 1,000 gzip mutations. These are deterministic regression
tests, **not** coverage-guided fuzz campaigns or satisfaction of the plan's CPU-hour release floors. The foundation subset
passed 63/63. The manifest/wire/assessment/lifecycle work adds 23 cases, included in the 620-case native replays above.

Two defects found during this work have regressions: the runtime TAR writer emits no end markers if no entry is written,
and the runtime raw DEFLATE stream emits no payload for an entirely empty input. The adapters explicitly emit valid empty
containers. A seekable captured payload view also avoids TarWriter's whole-payload buffering when its output is compressed.

The later narrow ZIP publication extraction passed 52 tests with eight existing Unix-only skips on both Windows host
and Windows VM, and all 60 on Mac. It adds two explicit-profile guard cases. The later Ubuntu replay includes these cases. See
[the extraction checkpoint](ZIP_PUBLICATION_EXTRACTION.md). This is a compatibility refactor, not qualification of
the legacy pathname operations for new formats; no new profile is enabled.

The later versioned archive-journal checkpoint adds 37 cases and broadens the affected suite to include journal, job
and undo behavior: Windows host and medium-integrity Windows VM each pass 696 with 30 skips (726 total); Mac passes
705 with 21 skips (726 total). The existing headless recovery UI suite passes 244 with two skips on the host. See
[the journal contract checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md). These results do not implement sidecar IO, native
publication/recovery, or the required real archive process-crash matrix. The subsequent
[read-only recovery UI checkpoint](ARCHIVE_RECOVERY_UI.md) guards the direct staged-intent loop and exposes recorded
archive facts without opening the artifacts. Native archive recovery actions still need implementation before production
registration.

The UI guard adds nine headless cases. Its final affected UI replay passes 253 with two skips on both host and Windows
VM, and 249 with six skips on Mac (255 total in each environment). Stale rows cannot enter generic archive cleanup,
transfer retry or unresolved tracking closure, and reports do not open archive artifact content.

After the user's explicit authorization to stop the existing Ubuntu picture-test instances, the broader native Ubuntu
replay passed: original Core baseline 596 passed / five skipped (601 total), current Core 721 passed / five skipped
(726 total); baseline headless recovery UI 240 passed / six skipped (246 total), current UI 249 passed / six skipped
(255 total). No failures. These are separate owned source builds on Ubuntu/ext4, SDK 10.0.101 with serviced CoreCLR
10.0.14. See [the Ubuntu checkpoint](UBUNTU_REGRESSION_CHECKPOINT.md) for source/artifact hashes, setup details and limits.
All test and private SDK build-node processes were closed; no new profile or native publication capability was enabled.

The streamed sidecar checkpoint adds 61 cases and enforces the plan's explicit aggregate member-name ceiling. The
affected Core replay now passes 757 with 30 skips on both Windows host and VM, 766 with 21 skips on Mac, and 782 with
five skips on Ubuntu (787 total in each). Canonical digests remain schema-compatible. See
[the sidecar checkpoint](MANIFEST_SIDECAR_CHECKPOINT.md). The stream codec does not create private files or implement
native publication/recovery; those remain necessary before enabling a profile.

The Windows native worker lane now separately passes **25/25**: twelve archive authority/resource/lifetime/assessment
cases and thirteen existing restricted-worker regressions. The tracked runner was exercised successfully on the guest. A healthy
64 MiB framed stream-proxy copy/readback, native 64 MiB allocation, denial of an additional 1 GiB commit, finite CPU,
bounded stderr, unrelated inherited-handle denial, repeat disposal and real parent death are included. This is a
development boundary/assessment checkpoint, not complete write-pipeline or installed-package qualification. The actual
self-contained application assessed a TAR inside LPAC; forged startup facts refused before source IO. Seven early-entry
cases pass on Windows and Mac. See the [assessment report](ASSESSMENT_VALIDATION.md) and
[native spike report](NATIVE_WORKER_SPIKES.md) for artifact hashes, failed iterations, limitations and remaining gates.

The Mac ad-hoc signed App Sandbox probe admitted its own executable read and refused unrelated file read/write,
network access and `fork`. Its attempted absolute 1 GiB `RLIMIT_AS` returned EINVAL and a 2 GiB mapping still succeeded.
The hard whole-process memory gate remains **unqualified**; no Mac writer is enabled from these partial denials.
The [Darwin footprint-limit follow-up](MACOS_FOOTPRINT_LIMIT_PROBE.md) tests the SDK-declared Mach setter as ordinary
UID 501. Its 64 MiB request returns `KERN_NO_ACCESS`; a control touches 96 MiB with a 102,105,568-byte physical footprint.
Both children exit/reap. That direct per-user call does not provide a boundary in this environment. Another enforceable
Mac worker-memory route and full qualification remain required; no profile is enabled.

The [supported Linux VM candidate](MACOS_VIRTUAL_MACHINE_BOUNDARY_SPIKE.md) starts and stops under ordinary Mac UID 501
with the public virtualization entitlement. Its owned Linux allocation child runs as UID 1000 and cannot raise its
cgroup limit: a 32 MiB control completes, while a larger allocation is OOM-killed at an exact 1 GiB peak. All owned
children are reaped. This is mechanism evidence, not archive-worker or production qualification. Actual worker/broker
integration, the complete guest/host memory envelope, all remaining confinement/resource/lifetime gates and guest-system
source/build/license closure remain open. Fixed guest RAM does not establish a whole-host/helper RSS bound.

The [actual managed guest workflow](MACOS_MANAGED_VM_ARCHIVE_WORKERS.md) now passes create, assess, replace and fresh
verify for TAR/gzip/BZip2/Zstd via the existing production clients/roles and a second raw serial stream. Its final
ordinary-user Mac runner records 16 distinct zero-exit/reaped helpers, natural shutdown, exact independent BSD tar/
native-Zstd payload checks and byte-identical original allocation probes. Terminal readiness, interactive Console
byte conversion and post-exit diagnostic failures were fixed during the spike. Production registration, all remaining
resource/confinement/lifetime/package gates and guest-source/license closure remain open. Small cgroup peaks exclude
some prepopulated initramfs/runtime file pages and must not be reported as full process/VM/host memory qualification.

The native development host's [parent-death follow-up](MACOS_VM_PARENT_DEATH.md) adds an actual-parent check, installed
kernel exit watch, monotonic deadlines and bounded explicit VM stop. The 16 healthy archive roles still pass. Twenty
owned-parent SIGKILL/SIGTERM cases pass with protocol pipes deliberately kept open, including blocked/spinning guests;
eight invalid-parent cases refuse before VM setup. Active guests reach state zero through successful stop completion,
all direct parents are reaped and all helpers disappear after adopter reaping. This qualifies that private experiment's
observed lifetime mechanism, not the production backend or complete package/resource/cancellation gates.

The [dedicated cancellation/helper-death follow-up](MACOS_VM_CANCELLATION.md) adds a verified inherited FIFO and third
raw serial channel, reusing Core's bounded transport with the guest token. Four live cancellation cases pass: actual
assessment/session cancellation and blocked input exit cooperatively in 29–59 ms, while full output and a spinning
guest complete explicit VM stops in about 1.52 seconds. Ten bad descriptors refuse before setup. Twelve direct helper
SIGTERM/SIGKILL cases also observe the newly created dedicated virtualization provider exiting/disappearing with all
protocol/control pipes held open, including full unread output. The 16 healthy roles and 20 parent-death/8 invalid-parent
cases regress successfully. Startup/release cancellation, the complete resource/confinement/package and guest-license
gates remain open; no production backend is registered.

The [fixed guest RAM/resource follow-up](MACOS_VM_MEMORY_ENVELOPE.md) changes the managed experiment from 1536 MiB to
exactly 1 GiB and refuses all other RAM settings. All healthy/lifetime cases regress successfully. A 256 MiB unmanaged
entropy allocation completes; a 1280 MiB attempt is killed after 800 MiB touched. Draining/blocked entropy output runs
for ten seconds each with per-process accounting and complete cleanup. The provider reaches 1,233,207,296-byte RSS /
1,086,506,424-byte footprint, exceeding 1 GiB despite the fixed guest RAM. This explicitly leaves the whole host/helper/
provider memory envelope unqualified. No limit is waived and no production profile is enabled from these measurements.

## Outstanding work

The later [Linux worker checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) adds 41 cases. Its final native Ubuntu replay
passes 40 with one platform-guard skip; Windows VM and Mac each pass 26 with 15 Linux-native skips. The existing native
Windows containment suite separately passes 25/25 with the updated shared Core/probe binaries. No failures. The Linux
lane remains internal and unregistered; no profile or complete AW packet is enabled or qualified by this checkpoint.

The [held Unix outer-metadata checkpoint](UNIX_OUTER_METADATA.md) adds 46 cases. Its affected origin/metadata suite
passes 192 with 36 skips on Windows, 221 with seven skips on Mac and 218 with ten skips on Ubuntu (228 total per
platform), with no failures. Native C observers independently agree on every stat/flag field on Mac and Ubuntu.
Private Unix storage, metadata application and journaled publication/recovery remain unfinished.

The later [Unix storage checkpoint](UNIX_ARCHIVE_STORAGE.md) adds 32 cases: the 96-case affected storage/observation
replay passes 56 with 40 skips on Windows, 88 with eight skips on Mac, and 86 with ten skips on Ubuntu. The stricter
mount-bound Linux worker admission separately passes 40 with one platform-guard skip. No failures. Native private
files and reviewed no-replace primitives now exist, but complete metadata application, journaled publication/recovery
and exact cleanup remain unfinished; the Unix unlink API explicitly retains evidence rather than using path deletion.

The [Unix origin-application checkpoint](UNIX_ORIGIN_APPLICATION.md) adds 13 Core native cases and one Windows case.
The affected Unix storage/observation/origin replay passes 100 with nine skips on Mac, 98 with eleven skips on Ubuntu,
and 56 with 53 skips on Windows (109 total). The affected Windows native archive/publication lane passes 121 with two
skips (123 total). No failures. Origin bytes can be copied create-only to sealed new private artifacts; renamed/recovery
leases cannot write them. Strongest-input aggregation and complete transaction integration remain unfinished.

The later [schema-2 provenance journal checkpoint](ARCHIVE_ORIGIN_JOURNAL.md) adds 43 Core and three headless UI cases.
Its affected Core replay passes 955 with 30 skips on the Windows VM, 964 with 21 skips on Mac, and 980 with five skips
on Ubuntu (985 total). Recovery UI passes 256 with two skips on Windows and 252 with six skips on Mac/Ubuntu (258 total).
No failures. Legacy completed journals stay visible for archive-specific provenance review; recorded artifacts are not
opened or cleaned by the report. Native publication, real crash recovery and complete UI/package qualification remain
unfinished; these results complete no AW packet or full-goal gate.

AW0 is partial: baseline replay exists, but instrumented canary validation, tool inventories and scale/resource measurement
are unfinished. AW1 is partial: protocol/proxy/admission exist, but no production platform confinement/publication adapter
has qualified. AW2 is partial: typed dispatch refuses safely, but shared publication/recovery and worker-backed ZIP remain
unfinished. AW3 is partial as described above. AW4–AW7 (complete UI/provider/edit-session workflows, BZ2/Zstd, audited 7z and
standalone formats) remain unfinished. AW8–AW9 remain the optional XZ/ISO work defined by the plan.

Do not mark any packet complete, enable a format, claim all-platform live acceptance, or claim absence of bugs from these
results. Continue with production worker confinement/lifecycle and native publication/transaction facts, then connect and
qualify the transforms and UI. Preserve existing ZIP behavior throughout the separate publication and worker refactors.

Guest use checks for other test instances before starting. Close only instances launched by this task and confirm exit
after each test. A temporary `caffeinate -diu` assertion keeps the Mac awake until reboot at the user's request; it is not
a test instance and is intentionally retained.

The Windows guest setup is owned at `C:\Users\admin\FileCatValidation\ArchiveWriting-42829297`.
The Mac's current run includes the full assessment source and final accounting corrections described in the assessment
report. Windows native probes use the host-built binaries
identified in the spike report. The Windows SDK copy is hash-verified
(`8DC778E823BBA1BF6C6D6C608727304663C162790D783187AFCF1B5B6EB8CF17`) and reports 10.0.101. Download from the guest stalled;
only this task's matching downloader was stopped. SDK/reference packs and a locked-package offline feed were copied from
the host. The repository deliberately pins serviced runtime/apphost packs 10.0.14 separately from SDK 10.0.101's bundled
10.0.1 packs. Copying the missing serviced apphost packs resolved offline restore. Test execution uses the installed
10.0.14 host with the private SDK's VSTest console. Earlier elevated/setup runs failed or timed out and are not passes;
only the medium-integrity functional run and the separately recorded successful native lanes are counted above.
VMware paths are `V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx` (actually Windows 11) and
`V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx`. The user supplied `a` for VM encryption and guest passwords.
Control commands require `vmrun -vp a`; guest users are `admin` and `benny` respectively. On 5 November the user explicitly
authorized stopping the pre-existing Ubuntu picture-test FileCat PID 65891 and image viewers 66892/67247 so this work
could continue. They were checked against their exact command lines before being signaled. This exception does not
change the rule to postpone on other occupied machines or authorize stopping unrelated future test instances.

## Required independent review after the implementation loop

On 5 November the user required a separate independent-review pass after the full implementation/testing loop. Treat
this as a completion gate within the active goal, after the RAR analysis and any feasible implementation. Re-read the controlling plan and
final diff from the reviewer's perspective; assess actual behavior and evidence without relying on implementation
assumptions or test success as proof of correctness.

Cover UX, UI, functionality and security: complete workflows and truthful unavailable states; defaults, focus, keyboard
and accessibility; naming, occurrence selection, fidelity, cancellation and recovery; confinement, IPC/resource bounds,
identities/aliases, publication/rollback/cleanup ownership, provenance and metadata; licensing/package boundaries and
all-platform qualification. Record reproducible findings and severity, fix findings, and repeat appropriate tests/live
validation and review until no known actionable findings remain. Preserve explicit limitations; do not claim absence
of all possible bugs. A final review report must identify the reviewed commit/artifacts, coverage, fixes and remaining
qualification limits. Do not mark the goal complete before this gate is satisfied.

## RAR analysis and feasible implementation before independent review

After the main archive-writing implementation/validation loop, research RAR writing before the final independent
review. The user's later clarification supersedes the earlier review-first order and analysis-only scope: the analysis
should lead to implementation and validation where a license-compatible, technically viable path exists.

At that point, research current primary licensing/specification sources and the repository's existing RAR provenance.
Assess a FileCat-owned implementation developed without violating upstream restrictions, including any necessary clean
separation from restricted decompressor sources. If other viable options fail, assess invoking an external `rar` tool,
including user-supplied installation versus redistribution, license obligations and Windows/macOS/Ubuntu feasibility.
Produce an evidence-backed comparison and recommendation, then implement/validate a viable option if possible. Do not
waive license, security, platform, packaging or preservation requirements to obtain a RAR writer. Record specific
evidence if no conforming implementation is feasible; the final independent review follows this RAR phase.
## User-requested local move into a writable archive

The existing local ZIP route now replaces the F5/manual-delete refusal with F6 move intent, verified publication,
unchanged-source removal, skipped-input retention, queue/name propagation and honest partial results. Real-file/native
adapter and headless UI checks passed on Windows, Mac and Ubuntu; counts, immutable evidence and limitations are in
`LOCAL_MOVE_INTO_ZIP.md`. The follow-up `ZIP_MOVE_NATIVE_GUI.md` adds Mac keyboard/AX and Ubuntu desktop journeys and fixes
edited target mapping, safe conflict defaults and singular summaries, with 85 passing adjacent UI checks on each
platform. Native Windows desktop, complete Mac visual acceptance and full crash/namespace/metadata/scale qualification
are still open. These checkpoints do not enable new profiles or complete the common transaction boundary.

The amended plan §11.6.1 also applies to future qualified formats. RAR feasibility should lead to an implementation
where lawful and feasible, before the final independent review. The main implementation loop and those final phases
remain unfinished; keep the goal active.
