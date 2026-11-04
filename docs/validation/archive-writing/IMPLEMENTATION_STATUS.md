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
- The transferred current-source delta has SHA-256
  `70E85DE4D5DA731DF0C38DD814E1253B87F2D822CE1172CFA02D65DC2BFE2CDA`.
  It contains the modified/new production and test sources, applied over the baseline source archive.

## Implemented foundations

| Area | Current behavior | Remaining gate |
|---|---|---|
| Typed contracts | Explicit versioned profiles, raw occurrence keys, portable name validation and cumulative member/metadata/heap/logical-byte budgets | Full request/manifest/receipt wire schema, service registry and session contracts |
| Framed protocol | Nonce, version, sequence, reserved-field/opcode, frame-size, cumulative payload/control and frame-count checks before payload allocation; poisoned channels cannot resume | Phase machine, real production worker lifecycle, cancellation channel and OS controls |
| Proxy IO | Child uses fixed input IDs and seekable stage requests; parent checks offsets/lengths/IO amplification before touching a stream; no path/publication/delete authority | Reviewed native input leases, parent-owned manifests and production launch integration |
| Queue | One active worker reservation and at most eight pending; cancellation and duplicate release tested | Integrate with actual process lifetime and operation-center phases |
| TAR assessment | Full raw USTAR/PAX framing, checksums, exact UTF-8, numeric/PAX grammar and allowlist, pre-allocation metadata limits, complete payload hashes, end markers and padding; independent runtime-decoder agreement | Coverage-guided campaigns, complete metadata/preservation corpus, scale measurements and native independent tools |
| TAR transform | Stream-only create and exact per-occurrence replace/delete/rename; flattened file/directory additions; order/duplicate/metadata preservation; collision and stale-key refusal; complete expected-manifest verification | Folder preparation/review, native safe save, fresh verifier process, provenance and production dispatch |
| Gzip | Neutral single-member framing, explicit DEFLATE terminal status/consumption, CRC/ISIZE, no concatenation/trailing data, bounded expansion; streaming runtime encoder and valid empty output | Existing-TGZ update adapter, complete output bound, wrapper metadata corpus and independent oracle qualification |
| TAR.GZ creation | Sequential TAR output through gzip without buffering a complete payload; strict wrapper/catalog/manifest readback | Production worker and all inherited release gates |

The raw gzip inflater is the already-vendored SharpCompress/DotNetZip component, whose existing MS-PL/BSD attribution
is retained. It is used because the runtime DeflateStream API does not expose a terminal-state/consumed-byte result.
The encoder remains the runtime DeflateStream. No third-party source/package was added or upgraded. The reachable-code
inventory and artifact SBOM/notices still need review before enabling this path.

## Observed checks, 4 November 2026

The test command is `dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-restore` with filter
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget` and a TRX logger.
SDK: `10.0.101`. Raw logs/TRX files are retained under the worktree's `test-results/archive-writing` and the owned guest
validation directories; they are not evidence of installed-package or GUI qualification.

| Environment | Baseline result | Current-source result | Scope/limitations |
|---|---|---|---|
| Windows x64 host | 506 passed, 28 skipped, 0 failed | 569 passed, 28 skipped, 0 failed (597 total) | Regression/function tests, including new real-child proxy IO; no GUI or native containment qualification |
| macOS ARM64, macOS 27.0.1 / APFS | 515 passed, 19 skipped, 0 failed | 578 passed, 19 skipped, 0 failed (597 total) | Owned checkout under `/Users/benny/FileCatValidation/archive-writing-42829297`; SDK 10.0.101, installed 10.0.1/10.0.12 runtimes; no signed-worker/package qualification |
| Windows 11 x64 VM, build 26340 | Pending | Pending | Initially lacked pinned SDK; isolated offline SDK/feed setup is being completed; no test pass claimed |
| Ubuntu VM | Postponed | Postponed | Another tester's pre-existing FileCat picture instance was observed; it was not stopped or modified |

The new tests include 1,000 checksum-repaired TAR mutations and 1,000 gzip mutations. These are deterministic regression
tests, **not** coverage-guided fuzz campaigns or satisfaction of the plan's CPU-hour release floors. The complete new-test
subset currently passes 63/63 on the host; the full current suite includes it on macOS.

Two defects found during this work have regressions: the runtime TAR writer emits no end markers if no entry is written,
and the runtime raw DEFLATE stream emits no payload for an entirely empty input. The adapters explicitly emit valid empty
containers. A seekable captured payload view also avoids TarWriter's whole-payload buffering when its output is compressed.

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
The Mac and host use the same baseline plus the current-source delta above. The Windows SDK copy is hash-verified
(`8DC778E823BBA1BF6C6D6C608727304663C162790D783187AFCF1B5B6EB8CF17`) and reports 10.0.101. Download from the guest stalled;
only this task's matching downloader was stopped. SDK/reference packs and a locked-package offline feed were copied from
the host. Re-extraction reported existing DLL unlink refusals, and offline restore still asks for missing apphost packs
(including 10.0.14) despite the private SDK. The next setup step is to inspect the owned `msbuild-properties.json` report
and correct SDK/apphost-pack selection; do not reinterpret this setup failure as a passing Windows VM test.
VMware paths are `V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx` (actually Windows 11) and
`V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx`. The user supplied `a` for VM encryption and guest passwords.
Control commands require `vmrun -vp a`; guest users are `admin` and `benny` respectively. Never restart or stop another
tester's Ubuntu instance merely to make the machine available.
