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

| Area | Current behavior | Remaining gate |
|---|---|---|
| Typed contracts | Explicit versioned profiles, raw occurrence keys, portable name validation and cumulative member/metadata/heap/logical-byte budgets | Full request/manifest/receipt wire schema, service registry and session contracts |
| Framed protocol | Nonce, version, sequence, reserved-field/opcode, frame-size, cumulative payload/control and frame-count checks before payload allocation; bounded fragmented records; poisoned channels cannot resume | Rebuild/verification phase contracts, cancellation channel and final platform/package qualification |
| Canonical manifests | Frozen ordered physical members, contiguous exact duplicate occurrences, profile/features and separate full/content/metadata digests; aggregate heap/metadata/logical budgets | Complete format feature inventories, rebuild/verification wire schemas and private sidecars |
| Proxy IO | Child uses fixed input IDs and seekable stage requests; parent checks offsets/lengths/IO amplification before touching a stream; no path/publication/delete authority | Reviewed native input leases, parent-owned manifests and production launch integration |
| Queue | One active in-process reservation and at most eight pending; assessment holds it through exit, final diagnostics and reaping | Other roles, cross-parent scheduling and operation-center phases |
| Legacy ZIP publication extraction | Separate compatibility-only publication/rollback/cleanup boundary; existing codec and journal ordering retained; explicit profiles refused before intent | Object-bound publication, typed provenance, durable archive facts/recovery and separately reviewed ZIP worker migration |
| Archive journal facts | Immutable bounded versioned snapshots, durable transitions, conservative damaged/unknown-record handling, unresolved holds and pruning/closure protection | Private sidecar IO, native object-bound recovery/actions, UI consumer guard/disclosure and actual publication/crash integration |
| Windows boundary spike | Suspended restricted LPAC, actual-token/access checks, exact inherited pipe list, private-profile write protection, finite job commit/CPU, bounded stderr and idempotent kill/reaping; actual guarded application entry tested | Full engine workloads, other namespace/asset/setup-race probes, ordinary user and installed-package/RID qualification |
| TAR assessment process | Actual early entry, numeric read-only source proxy, SHA/raw TAR/runtime-decoder passes, checked catalog phases and terminal digest; parent CPU/progress watchdog and final drain/reaping checks | Qualified AppServices registration, reviewed input leases, cooperative cancellation, transform/fresh verifier and publication integration |
| TAR assessment | Full raw USTAR/PAX framing, checksums, exact UTF-8, numeric/PAX grammar and allowlist, pre-allocation metadata limits, complete payload hashes, end markers and padding; independent runtime-decoder agreement | Coverage-guided campaigns, complete metadata/preservation corpus, scale measurements and native independent tools |
| TAR transform | Stream-only create and exact per-occurrence replace/delete/rename; flattened file/directory additions; order/duplicate/metadata preservation; collision and stale-key refusal; complete expected-manifest verification | Folder preparation/review, native safe save, fresh verifier process, provenance and production dispatch |
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
| Ubuntu VM | Postponed | Postponed | Another tester's pre-existing FileCat picture instance was observed; it was not stopped or modified |

The new tests include 1,000 checksum-repaired TAR mutations and 1,000 gzip mutations. These are deterministic regression
tests, **not** coverage-guided fuzz campaigns or satisfaction of the plan's CPU-hour release floors. The foundation subset
passed 63/63. The manifest/wire/assessment/lifecycle work adds 23 cases, included in the 620-case native replays above.

Two defects found during this work have regressions: the runtime TAR writer emits no end markers if no entry is written,
and the runtime raw DEFLATE stream emits no payload for an entirely empty input. The adapters explicitly emit valid empty
containers. A seekable captured payload view also avoids TarWriter's whole-payload buffering when its output is compressed.

The later narrow ZIP publication extraction passed 52 tests with eight existing Unix-only skips on both Windows host
and Windows VM, and all 60 on Mac. It adds two explicit-profile guard cases. Ubuntu replay remains postponed. See
[the extraction checkpoint](ZIP_PUBLICATION_EXTRACTION.md). This is a compatibility refactor, not qualification of
the legacy pathname operations for new formats; no new profile is enabled.

The later versioned archive-journal checkpoint adds 37 cases and broadens the affected suite to include journal, job
and undo behavior: Windows host and medium-integrity Windows VM each pass 696 with 30 skips (726 total); Mac passes
705 with 21 skips (726 total). The existing headless recovery UI suite passes 244 with two skips on the host. See
[the journal contract checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md). These results do not implement sidecar IO, native
publication/recovery, or the required real archive process-crash matrix. The UI's direct staged-intent review loop
still needs an explicit archive guard and archive-fact reporting before production registration.

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
Control commands require `vmrun -vp a`; guest users are `admin` and `benny` respectively. Never restart or stop another
tester's Ubuntu instance merely to make the machine available.

## Queued follow-up after the current goal is complete

The user requested a separate RAR-writing licensing analysis only after the entire archive-writing implementation goal,
including all required implementation, testing, validation and rechecks, is finished. Preserve that prerequisite.

At that point, research current primary licensing/specification sources and the repository's existing RAR provenance.
Assess a FileCat-owned implementation developed without violating upstream restrictions, including any necessary clean
separation from restricted decompressor sources. If other viable options fail, assess invoking an external `rar` tool,
including user-supplied installation versus redistribution, license obligations and Windows/macOS/Ubuntu feasibility.
Produce an evidence-backed comparison and recommendation. The requested follow-up is analysis; RAR implementation and
enablement are not part of that request. Do not start this analysis before the current goal's completion audit passes.
