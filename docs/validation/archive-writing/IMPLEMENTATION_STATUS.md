# Archive writing implementation status

This is an **incomplete implementation**, not format or release qualification. The controlling requirements remain
[the archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md). No new writer is enabled in the
application. An explicit new-profile job refuses instead of entering the legacy ZIP executor.

## Source and workspace

- Baseline: `4282929753f062f5bb2cc130e8316b133e5c8e03`.
- Branch: `codex/archive-writing`.
- Isolated worktree: `E:\FileCat\.tmp\worktrees\archive-writing`. The shared `E:\FileCat` checkout remains on `main`.
- The baseline plan was an untracked user-provided file; this branch retains its unchanged contents.
- Implementation checkpoints: `a8313365` (contracts/proxy IO) and `7e63b39e` (strict TAR/gzip transforms).
- The transferred TAR/gzip foundation delta (before the later native-worker changes) has SHA-256
  `70E85DE4D5DA731DF0C38DD814E1253B87F2D822CE1172CFA02D65DC2BFE2CDA`.
  It contains those foundation production and test sources, applied over the baseline source archive.

## Implemented foundations

The later [captured-member defaults checkpoint](CAPTURED_MEMBER_DEFAULTS.md) corrects ordinary Unix permission bits
and Windows 0644 defaults, with passing native regression checks on all three platforms. It does not complete capture.
The subsequent [exact TAR timestamp checkpoint](EXACT_TAR_TIMESTAMPS.md) adds native nanosecond capture, versioned
input/member metadata and a bounded PAX encoder adapter, with three-platform regression/native and independent-reader
checks. Complete capture/publication/production integration and release qualification remain unfinished.
The subsequent [preparation-space checkpoint](PREPARATION_SPACE_ADMISSION.md) checks the complete stage/evidence,
known snapshots and safety margin against capacity from the retained native destination, before intent or artifact
creation. Unknown capacity remains explicit; full capture/space UI, allocation guarantees and qualification remain open.

| Area | Current behavior | Remaining gate |
|---|---|---|
| Typed contracts | Explicit versioned profiles/enum IDs, raw occurrence keys, portable names, cumulative member/metadata/heap/logical budgets and TAR request/manifest/receipt wire | Other-format schemas, production service registry and session contracts |
| Framed protocol | Nonce, version, sequence, reserved-field/opcode, frame-size, cumulative payload/metadata and finite frame-count checks before payload allocation; bounded fragmented records; poisoned channels cannot resume; distinct TAR assessment/transform/verification roles | Other formats, cancellation channel and final platform/package qualification |
| Canonical manifests | Frozen ordered physical members, contiguous exact duplicate occurrences, profile/features and separate full/content/metadata digests; aggregate name/heap/metadata/logical budgets; shared incremental hashing | Complete format feature inventories, rebuild/verification wire schemas and native sidecar integration |
| Manifest sidecar codec | Scoped versioned stream encoding, incremental bounded review, complete EOF/semantic/file checksums, poisoned failure state; no pathname authority | Native private-file leases/permissions/durability, transaction integration and exact-object recovery/cleanup |
| Windows archive storage | Unregistered held NTFS directory/file leases, exclusive private ACL-at-create stages/sidecars, bounded sealed streams, complete object versions, no-replace handle rename and exact reviewed deletion; [native evidence](WINDOWS_ARCHIVE_STORAGE.md) | Typed provenance/outer metadata, durable journal/publication/recovery integration, directory-entry durability, global lease accounting, other filesystems/platforms and release qualification |
| Typed origin evidence | Absent/present/unavailable raw snapshots; held Windows ADS and Mac/Linux xattr reads; private sealed-stage create-only copying with post-rename capability closure; [Windows checkpoint](ARCHIVE_ORIGIN_CHECKPOINT.md), [Unix observations](UNIX_ORIGIN_OBSERVATIONS.md), [Unix application](UNIX_ORIGIN_APPLICATION.md) | Unix semantic classification/strongest-input aggregation, full promised metadata application, shared evidence budgets, transaction rereads/retention and marked-input qualification |
| Windows outer observations/guards | Frozen held-file owner/group/DACL/label, exact creation/mtime, attributes/link count/origin; mandatory same-object metadata checks before/after full-hash namespace review; [evidence](WINDOWS_ARCHIVE_METADATA.md) | Unqueried audit/resource-policy admission, promised metadata application/prepared-publication state, aggregate budgets, durable publication/recovery and release qualification |
| Unix outer observations | Held regular-file owner/group/mode, nanosecond mtime/ctime/optional birth, link count, native flags, bounded raw ACL and typed origin; [checkpoint](UNIX_OUTER_METADATA.md) | Complete security/xattr admission, promised metadata application, global budgets, publication/reconciliation and release qualification |
| Unix archive storage | Held directory/regular-file leases, private-at-create ext4/APFS artifacts, bounded sealed streams, native sidecar roundtrips, reviewed no-replace rename and checked sync calls; exact unlink deliberately refused; [checkpoint](UNIX_ARCHIVE_STORAGE.md) | Full metadata/origin application, durable journal/reconciliation, reviewed replacement/rollback, exact cleanup or retained-artifact workflow, global budgets and release qualification |
| Declared Windows origin zone | Closed bounded UTF-8/UTF-16LE declaration parsing, exact-byte non-decreasing updates, URL-free zone-only output; independent Windows reader corrected encoding admission; [evidence](WINDOWS_DECLARED_ORIGIN_ZONE.md) | Complete strongest-input aggregation/evidence storage, native Unix representations, transaction integration and marked-input qualification; not an effective trust/policy decision |
| Streamed provenance aggregation | Scoped bounded source-version/raw-mark sidecar, complete EOF/checksum review, all-input Windows declaration aggregation with retained raw URLs, native private-file reopen/seal/copy test; [evidence](ORIGIN_EVIDENCE_AGGREGATION.md) | Durable transaction references/retention, reviewed source-plan binding, global budgets, native Unix representations, final metadata application and actual publication/recovery qualification |
| Proxy IO | Child uses fixed input IDs and bounded stage requests; independent views preserve retained native owners; parent checks offsets/lengths/IO amplification before streams; no worker namespace authority | Scalable native capture/snapshot repositories, global budgets and production launch integration |
| Queue/lifetime | One active in-process reservation and at most eight pending; all three roles share exit, diagnostics/CPU, deadline, cancellation/termination and reaping checks | Cooperative cancellation, cross-parent scheduling, global budgets and operation-center phases |
| Legacy ZIP publication extraction | Separate compatibility-only publication/rollback/cleanup boundary; existing codec and journal ordering retained; explicit profiles refused before intent | Object-bound publication, typed provenance, durable archive facts/recovery and separately reviewed ZIP worker migration |
| Archive journal facts | Immutable bounded versioned snapshots, durable transitions, schema-2 provenance reservations/finalized versions/counts, conservative legacy/damaged/unknown/out-of-order retention and read-only reporting; [provenance journal checkpoint](ARCHIVE_ORIGIN_JOURNAL.md) | Native sidecar storage/integration, object-bound recovery/actions and actual publication/crash integration |
| Archive recovery UI guard | Fresh journal reads at every entry point; generic archive mutations refused; recorded facts/paths exposed in a copyable read-only report | Native object-bound archive review/resolution, complete archive workflow/accessibility and package qualification |
| Windows boundary spike | Suspended restricted LPAC, actual-token/access checks, exact inherited pipe list, private-profile write protection, finite job commit/CPU, bounded stderr and idempotent kill/reaping; actual guarded application entry tested | Full engine workloads, other namespace/asset/setup-race probes, ordinary user and installed-package/RID qualification |
| Linux boundary development lane | Unregistered stream-only launcher/worker, native delegated cgroup memory/pids, finite CPU, pidfd/parent-death lifetime, all-thread seccomp and Landlock, bounded stderr and exact empty-controller cleanup; [native checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) | Production entry/roles, cancellation, complete package/update/descriptor/resource races, ordinary desktop routes, ARM64/other filesystems/kernels and release qualification |
| TAR assessment process | Actual early entry, numeric read-only source proxy, bounded physical SHA/raw TAR/runtime-decoder passes, checked catalog phases and terminal digest; shared parent watchdog and final drain/reaping checks | Qualified AppServices registration, reviewed input leases, cooperative cancellation and publication integration |
| Fresh TAR verification process | Separate role/nonce/process derives the complete manifest without the parent's expectation; complete physical reads, terminal/drain/reaping checks and exact manifest comparison, now bridged to sealed native stages | Other formats, full native/package qualification, scalable capture, metadata/journal/publication integration and cooperative cancellation |
| Parent TAR expectation | Frozen positive numeric inputs, exact occurrence edits and flattened additions; complete source admission, combined source/input budgets, independent expected order/content/metadata/features before encoding; [evidence](TAR_PARENT_EXPECTED_MANIFEST.md) | Reviewed native source/folder capture and lease binding, complete output-bound audit, production integration and complete release qualification |
| TAR transform process | Typed bounded request/receipt, per-reference input aliases, finite parent stage/IO ceiling, isolated engine execution, terminal/reaping and parent manifest comparison; [evidence](TAR_TRANSFORM_WORKER.md) | Complete profile-bound audit, scalable capture, metadata/journal integration, cooperative cancellation, production contracts/registration and release qualification |
| Retained native TAR stage | Native object/content/metadata bindings, disposable independent read views, exact-bound fresh artifact claim, actual sealing, fresh verification and final stage/all-input rereview; [evidence](RETAINED_NATIVE_STAGE_PIPELINE.md) | Full metadata/origin admission/application, scalable capture/global budgets, durable journal/publication/recovery and production/qualification integration |
| Durable stage creation | Exact single-attempt native reservation; versioned flushed intent before creation and immutable identity/bound binding after creation; conservative recovery/reporting and real external process-termination checkpoints; [evidence](STAGE_CREATION_RESERVATION.md) | Remaining publication/recovery integration and full crash/durability/production qualification |
| Journaled native TAR preparation | Frozen schema-4 sidecar reservations, private native assessed/expected/provenance/observed manifests, complete readback, source/destination binding and durable Rebuilding/Finalizing/Verifying with distinct finalized/verified stage versions; [checkpoint](JOURNALED_TAR_PREPARATION.md) | Full capture/defaults/global budgets, metadata/origin application, publication/recovery, production workflows and full qualification |
| TAR assessment | Full raw USTAR/PAX framing, checksums, exact UTF-8, numeric/PAX grammar and allowlist, pre-allocation metadata limits, complete payload hashes, end markers and padding; independent runtime-decoder agreement | Coverage-guided campaigns, complete metadata/preservation corpus, scale measurements and native independent tools |
| TAR transform | Stream-only create and exact per-occurrence replace/delete/rename; whole-source namespace/logical budgets; order/duplicate/metadata preservation; native stage, bounded worker and fresh verifier roundtrips | Durable safe save/metadata/provenance, scalable folder preparation/review and production dispatch |
| Gzip | Neutral single-member framing, explicit DEFLATE terminal status/consumption, CRC/ISIZE, no concatenation/trailing data, bounded expansion; streaming runtime encoder and valid empty output | Existing-TGZ update adapter, complete output bound, wrapper metadata corpus and independent oracle qualification |
| TAR.GZ creation | Sequential TAR output through gzip without buffering a complete payload; strict wrapper/catalog/manifest readback | Production worker and all inherited release gates |

The raw gzip inflater is the already-vendored SharpCompress/DotNetZip component, whose existing MS-PL/BSD attribution
is retained. It is used because the runtime DeflateStream API does not expose a terminal-state/consumed-byte result.
The encoder remains the runtime DeflateStream. No third-party source/package was added or upgraded. The reachable-code
inventory and artifact SBOM/notices still need review before enabling this path.

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
this as a completion gate within the active goal, before the deferred RAR analysis. Re-read the controlling plan and
final diff from the reviewer's perspective; assess actual behavior and evidence without relying on implementation
assumptions or test success as proof of correctness.

Cover UX, UI, functionality and security: complete workflows and truthful unavailable states; defaults, focus, keyboard
and accessibility; naming, occurrence selection, fidelity, cancellation and recovery; confinement, IPC/resource bounds,
identities/aliases, publication/rollback/cleanup ownership, provenance and metadata; licensing/package boundaries and
all-platform qualification. Record reproducible findings and severity, fix findings, and repeat appropriate tests/live
validation and review until no known actionable findings remain. Preserve explicit limitations; do not claim absence
of all possible bugs. A final review report must identify the reviewed commit/artifacts, coverage, fixes and remaining
qualification limits. Do not mark the goal complete or start the RAR analysis before this gate is satisfied.

## Queued follow-up after the current goal is complete

The user requested a separate RAR-writing licensing analysis only after the entire archive-writing implementation goal,
including all required implementation, testing, validation and rechecks, is finished. Preserve that prerequisite.

At that point, research current primary licensing/specification sources and the repository's existing RAR provenance.
Assess a FileCat-owned implementation developed without violating upstream restrictions, including any necessary clean
separation from restricted decompressor sources. If other viable options fail, assess invoking an external `rar` tool,
including user-supplied installation versus redistribution, license obligations and Windows/macOS/Ubuntu feasibility.
Produce an evidence-backed comparison and recommendation. The requested follow-up is analysis; RAR implementation and
enablement are not part of that request. Do not start this analysis before the current goal's completion audit passes.
