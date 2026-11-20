# Bounded recursive native selection capture — 6 November 2026

This is a component checkpoint on `codex/archive-writing`, following `1e7be732`. No new production writer or UI
capability is enabled. The [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains
open. Its RAR primary/fallback decision is separately committed in `b657a243`; AW10a follows recording this checkpoint.

## Implemented behavior

`ArchiveCapturedSelection` freezes every selected root before native work, walks iteratively, preserves explicit
empty folders, and sorts flattened additions by exact ordinal member name. It bounds all visited entries, including
exclusions, to 100,000, relative depth to 256, and names/retained costs through the same captured-input owner and
plan budget. The root list, path graph, descriptors, samples and factories are charged with the native observations;
a plan cannot omit that owner's cost or folder bindings. A failure disposes the exclusive source authority and
does not expose a partial selection. A wholly excluded selection refuses accidental empty-archive creation.

Links/reparse points, special Unix entries and unrepresentable names are counted as explicit exclusions, with at
most 128 retained samples. Planning requires acknowledgement when exclusions exist. Enumeration/metadata access
failures currently refuse the capture; per-item unavailable-source review remains unfinished. Unsupported-name
samples can identify the containing folder rather than the exact decoded leaf; refine that detail before UI exposure.

The Unix adapter uses a separate held-directory cursor (`openat`/`fdopendir`/`readdir`), strict bounded native record
decoding and directory-relative no-follow `statx`/`fstatat` classification. It checks the enumeration inode against the
native observation. The Windows adapter reuses the bounded `NtQueryDirectoryFile` namespace parser, checks exact
names/kinds and NTFS file identity, and excludes reparse points. Native directory observations are rereviewed after
the complete walk. No child directory or file handle is retained per catalog member.

Each lazy file lease traverses the captured ancestor identities from the retained pane root before opening its fixed
leaf. An ephemeral parent and file share one disposal lifetime; global input permits still cap concurrent native
file/observation leases at 16. Disposed root authority is checked before use. Native tests exercise 1,000 Unicode
children across enumeration batches and refuse same-byte replacement ancestors and changed file bytes.

Preparation now checks native destination ancestor identities before creating artifacts. This catches staging below
a selected folder even when an intervening subtree was excluded. The Unix test selects `tree`, excludes its `NUL`
child, requests output inside that child and proves refusal with zero artifact-creation calls. Windows checks pinned
ancestor identities, including lexical aliases, without a string-prefix containment assumption.

The native record layouts were checked against [Apple's dirent ABI](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/dirent.h),
[glibc's 64-bit dirent ABI](https://github.com/bminor/glibc/blob/master/sysdeps/unix/sysv/linux/bits/dirent.h),
[directory-stream lifetime/error semantics](https://man7.org/linux/man-pages/man3/readdir.3.html) and the existing
[Windows directory query contract](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntquerydirectoryfile).
These facts do not establish an atomic recursive filesystem snapshot.

## Actual validation

Tests ran only on the disposable Windows/Ubuntu VMs and the Mac. The host was used for editing, compilation and
artifact inspection. Busy guards postponed tests while other testers' processes were present; those processes were
not killed. The final owned test/worker censuses were empty. The Mac's until-reboot wake helper remains separate.

| Environment | Result | Actual scope |
|---|---|---|
| macOS ARM64/APFS | 247 passed, 2 platform skips, 249 total | Captured-input/directory/wire/transform/origin and native Unix storage/workflow regressions, including new recursive tests |
| Ubuntu x64/ext4 | 246 passed, 3 platform skips, 249 total | Same broad filter and new native recursive tests |
| Windows VM, medium-integrity common tests | 130 passed, 118 Unix-only skips, 248 total | Common capture/plan/wire/transform/origin plus explicit unsupported-Unix cases; repeated in v2/v3 |
| Windows VM, actual standard account, v2 | 220 passed, 1 platform/elevation guard skip, 221 total | `WindowsArchiveStorageTests` plus LPAC recursive workflows; includes existing native crash/TxF regressions |
| Windows VM, actual standard account, final v3 | 10 passed, zero skips | `Recursive_` cases, including real junction exclusion, changed-file/replaced-ancestor refusal, 1,000-child enumeration and four LPAC creation workflows |
| Mac-hosted Linux ARM64 VM | Eight fresh successful reaped helpers | Dedicated self-contained guest transform and fresh verifier for each of TAR, TAR.GZ, TAR.BZ2 and TAR.ZST; native APFS source capture/publication |

The Windows account is `fcarchive42829297`, SID `S-1-5-21-1278333194-2778939646-3566894036-1003`, with its actual
profile loaded and no Administrators group membership. Source-shaped complete runtime transfers matched all 314
files by count and SHA-256. VSTest used the installed .NET 10.0.14 runtime; builds/private probes used the existing
private .NET 10.0.101 SDK/runtime 10.0.1 setup. Mac used runtime 10.0.12 and Ubuntu 10.0.1. The ARM64 guest was
rebuilt self-contained for `linux-musl-arm64` at runtime 10.0.12, with the previously pinned kernel/module/helper.

All four formats were created from two files and three folders, including a nested Unicode filename and an empty
folder. Unselected outside content was absent. Unix source links were excluded; the final Windows test independently
checks a real junction. Parent-owned origin evidence covers all five accepted objects; Windows verifies folder-zone
aggregation. Expected manifests matched the fresh verifier before native publication. No source was removed.

Independent Python TAR parsing and native `/usr/bin/tar` extraction verified the exact five-member order/types and
both binary payloads for all four outputs from Mac, Ubuntu, the actual VM workflow and Windows. Zstandard used the
existing independently pinned native library. Windows outputs were independently checked on the Mac. These are
interoperability tests of bounded generated fixtures, not the format fuzz campaign.

Compiler PDB checksums matched the actual private source trees after the runs: native Mac and Ubuntu each contain
840 production documents / 7,936,277 bytes; the dedicated ARM64 guest contains 819 / 7,652,301; Windows's five
production assemblies contain 926 / 9,314,650. Test DLL/PDBs and complete Windows runtime inventories are retained
separately; generated test-framework sources are not misrepresented as production-source coverage.

## Frozen evidence and exact source

All copied archives below were locally checked for archive SHA-256, path/link safety, exact manifest file count,
every payload size and every payload SHA-256. Native VM fixture symlinks were recorded as text and not dereferenced
or included as archive links. No frozen payload/manifest was subsequently edited.

| Evidence archive | Bytes | Verified file rows | SHA-256 |
|---|---:|---:|---|
| `mac-recursive-selection-evidence-v1.tar.gz` | 5,753,567 | 883 | `91B315D0C4EA9286B2BF472BB2AFFDC4FCE640030C6718B6E40AF01155AC0300` |
| `ubuntu-recursive-selection-evidence-v1.tar.gz` | 5,700,444 | 880 | `FCA0E6857418900E4AEFADB38ABD14B638F2469DC8AE6EDE00651EE7F26B6E5C` |
| `macos-vm-recursive-selection-evidence-v1.tar.gz` | 96,479,677 | 1,110 | `CF4076CC33D4C4802151FF54FE0859CB79B57F0FA0D37CB19B148CE2D1F2D906` |
| `recursive-selection-windows-evidence-v1.tar.gz` | 108,984,429 | 1,522 | `DD95AFAAEDD6EEF5318961CCF87944A3C1D7E8C964672760B0F48048A1909BAA` |
| `windows-recursive-oracle-evidence-v1.tar.gz` | 6,959 | 24 | `45A49D36991337B353595CF37267FCF2D6D3490CD05A61E893582A261DD5A6EE` |

Private source packets retained in `.tmp` and evidence provenance:

- Native Mac/Ubuntu and Windows v2: `recursive-selection-source-v2.tar.gz`, 22,329 bytes,
  `D6BADAAB092D2E4D57EFB77892BD3F02EF3B7C4487BD528B36DCEA4539E91F7C` (16 files).
- Actual VM and final Windows v3: `recursive-selection-source-v4.tar.gz`, 32,139 bytes,
  `38DBEC8C6AD4F6C1628668C3F5B8D3EAD2CC3D1FF8221C3168040A1734EC7B19` (20 files).
  The production sources are unchanged from v2; the added Windows junction test and dedicated VM validation files
  account for the final test/validation delta. The source-packet per-file inventory matches the checkout.

Preflight deferrals are retained. Initial local compile failures were corrected (internal enum in a public theory
signature; incorrect fixture helper overload). Two host compile commands overlapped on a shared output; serial
compilation passed afterward. Host tests were never run. The first successful Mac native test runner referred to an
incorrect oracle script filename; its four outputs were then independently verified in a separate guarded command.
The corrected Ubuntu runner completed test, oracle and final-census stages. These corrections are not test failures
or skipped runs converted to successful evidence.

## Remaining gates

This does not complete AW2 or enable recursive archive commands. Global source-root/parent preparation ownership,
global transient-memory admission, immutable snapshot policy, full source-filesystem qualification, worst-case native
scale/resource measurements, complete exclusion/collision review UX and production command/session wiring remain.
Refine exact unsupported-name exclusion samples before exposing them. Current native observations/reopens detect
changes within their contract; they do not promise an atomic snapshot against an equally privileged concurrent actor.

The actual ARM64 VM result does not qualify the trusted Mac host/helper aggregate 1 GiB envelope. The previously
measured over-budget host footprint remains an open gate. Guest RAM/cgroup limits are not that host limit. No claim
is made that Unix source enumeration was live exercised on Linux ARM64 or macOS x64 merely because their ABI was
reviewed or the stream-only ARM64 worker ran. All remaining RID/package/resource, crash/publication, new-format,
UI/accessibility, installed-license/SBOM and required fuzz gates stay open.

The next focused packet is the user's independent compressed RAR investigation (AW10a), with stored RAR5 and
optional installed-tool compression as the authorized fallback. That decision does not erase the above work or move
the final independent review ahead of RAR implementation/qualification.
