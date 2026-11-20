# TAR.GZ worker pipeline, 5 November 2026

The subsequent [catalog/lifecycle follow-up](TAR_GZIP_CATALOG_REUSE.md) reduces retained catalogs and compressed
source passes, adds failure-cause diagnostics, and reproduces/fixes a controlled-child CPU observation defect.
The observations below describe this earlier checkpoint; its two historical refusals lack precise cause evidence.

Development checkpoint over `e86cfc10`. Application registration remains disabled. This extends the existing TAR
assessment/transform/fresh-verification and native preparation bridges; it does not complete the archive-writing plan.

## Implemented behavior

The read-only assessor and distinct fresh verifier now handle the neutral single-member gzip profile from plan §8.3.
They hash the full retained compressed source, validate the complete strict TAR catalog through the bounded gzip
decoder, then compare every decoded member in an independent runtime TAR pass and consume the remaining wrapper.
CRC, ISIZE, explicit DEFLATE termination and physical EOF are required. Stored names/comments/extra fields, nonzero
MTIME, reserved flags, unsupported XFL, truncation, concatenation and trailing bytes refuse. The browsing decoder does
not authorize this path. Catalog revisions and edit keys remain the physical compressed-file SHA-256.

Canonical features are `[1,dialect]` for plain TAR and `[2,dialect,OS,XFL]` for TAR.GZ; dialect is USTAR=1/PAX=2. The
parent freezes the complete expected member order, exact occurrences, content, metadata, profile and wrapper fields
before launch. Creation uses PAX (empty output is observed as USTAR), neutral MTIME/options and OS=255. Updates retain
the admitted TAR dialect and exact OS/XFL fields. Default compression preserves the source's indicated strategy;
explicit nondefault options must agree with its XFL or refuse. New compression settings map 1/2/3 to fastest/optimal/
smallest size; zero is the profile default. Existing compressed bytes need not be reproduced.

The transform request is now schema 2 and carries the explicit four-field profile; transform Hello revision is 3.
Source presence, lengths/hash, counts and output bound remain explicit. Input records retain schema 2 exact timestamps;
edit/addition and receipt records retain schema 1. Old/malformed headers and contradictory source/output profiles
refuse. Compressed receipts can be shorter than 1,024 bytes; the parent still enforces the correct format-specific
minimum, its actual stage length and complete expected manifest. The worker still receives numeric source/input/stage
grants, never native object identities, filesystem paths or the parent's expected-output digests.

For an update, the worker checks the physical revision and reads a full bounded gzip/TAR catalog before creating the
wrapper output. It then replays the same retained compressed grant using a fixed-buffer seekable adapter. That adapter
permits at most three forward decompression passes, checks the reviewed decoded length and wrapper on replay, validates
remaining CRC/trailer/EOF before a rewind and on successful completion, and poisons further use after failure. No
whole archive is buffered or spooled, and no worker scratch-file authority is added. The independent TAR decoder is
given a borrowed forward-only view: its seekable mode otherwise jumps past each payload and seeks back into it, causing
per-member decompression replays. Both plain and gzip validation now consume each payload forward through the guard.

Including the initial compressed hash/catalog, gzip transformation permits at most five compressed-source passes;
plain TAR retains three. Captured payloads are charged once per numeric reference. The parent computes the exact
allowance from actual grant lengths and enforces it on every read; the common authority has a finite five-pass ceiling.
The parent also enforces the output ceiling on every write, using the existing raw TAR estimate plus 12.5% and 64 KiB
of conservative encoder room. Logical/metadata/heap, 80-GiB physical, CPU/wall/idle and native worker limits remain.
These per-conversation controls do not complete the still outstanding global capture/heap/lease accounting gates.
In particular, the current gzip bridge retains its initial decoded catalog while the shared rewrite engine builds
another catalog. Consolidating that retention and proving the combined live heap bound remain required scale work.

Gzip encoding now has an abort/poison gate: after a failed transform/write, disposal releases encoder state while
forwarding no more stage bytes and appending no trailer. A normal finalizer failure still escapes. Sources, borrowed
stage streams and existing archive bytes remain owned by the caller; this layer grants no publication or cleanup.

The native preparation cases additionally exercise gzip create/update on held APFS/ext4/NTFS objects, including
confined Linux and actual Windows application/probe entry points. A private unmarked create continues through the
existing exclusive publication/reconciliation bridge. An update remains verified but unpublished; replacement,
retirement, promised outer metadata application, rollback and recovery resolution are still unfinished.

## Validation

| Environment | Broad affected replay | Additional replay |
|---|---|---|
| Windows 11 x64 VM / NTFS | Core: 1,189 passed, 172 skipped (1,361). UI: 219 passed | Storage: 103 passed, 30 skipped. Actual LPAC: 42 passed. Independent-input/protocol follow-up: 51 passed |
| macOS ARM64 / APFS | Core: 1,307 passed, 54 skipped (1,361). UI: 219 passed | Independent-input/protocol follow-up: 51 passed |
| Ubuntu x64 VM / ext4 | Core: 1,322 passed, 41 skipped (1,363). UI: 219 passed | Actual Linux confinement: 51 passed, one skipped. Three focused publication-refusal replays: six passed each |

The final listed replays have no failures. The later two independent-input cases explain Ubuntu's broad 1,363 total;
Windows/Mac exercise them in the subsequent 51-case follow-up. Production code is identical across those source
overlays; later changes add test fixtures and test-only lifecycle diagnostics. SDK 10.0.101; actual test-host runtimes
are Windows/Ubuntu 10.0.14 and Mac 10.0.12. Windows launch facts verify integrity 12288 before / 8192 after and exit 0;
admin membership remains, so this is not filtered-user qualification. Broad Core filtering includes Archive,
EditSession, BackingArchiveTarget and PaxTimestamp; UI includes the first three. Native lanes use tracked runners.

Tests cover four creation compression settings and an empty archive, USTAR/PAX exact duplicate edits and wrapper
preservation, delete-all, nine outer metadata/framing corruptions, replay/length limits, abort/disposal, complete wrapper
comparison by a fresh worker and malformed explicit profile headers. An independent Python tarfile/gzip generator
supplies USTAR/PAX inputs with duplicate occurrences and golden mode/owner/time metadata; FileCat assesses and updates
them. Python's tarfile/zlib then verifies eleven FileCat outputs, exact content/metadata, one complete wrapper, CRC/size
and exact header fields without extracting members to disk. Host focused runs pass, including these bidirectional cases.

The first update replay caught the seekable runtime TAR decoder's per-member backward reads; the forward-only decoder
view fixed the refusal while keeping the replay limit. Two earlier Ubuntu broad attempts returned WorkerUnavailable
in different ordinary TAR preparation/assessment cases. The terminal cause was not captured, so an actual nonzero
exit versus a failed lifecycle observation is unproven. Three focused six-case replays passed. A later broad run with
optional per-child terminal facts passed all 1,363 cases, followed by passing UI/native lanes. This does not establish
a fix for the two intermittent worker-monitor refusals: **their cause remains an open reliability investigation and
release gate**. Earlier failures and final diagnostics are retained; no guard or exit-code requirement was relaxed.

That successful Ubuntu run initially stopped its runner after Core because a reused timestamp-fixture directory held
both old and current update outputs. The eight files written since the accepted TRX's start time were copied to a
fresh owned directory; the unchanged exact-count oracle then passed. UI/native testing continued without rerunning
the already accepted Core run. Eleven gzip outputs and eight exact-time fixtures per platform pass independent Python
readers. Unix C metadata/xattr/capacity observers remain enabled in the broad replays.

Final runners reaped this task's processes. A later Mac GUI from `dsa-20261105-r60-chart/app` was preserved while completed
evidence was collected read-only. A Windows independent-input attempt postponed for another instance before testing;
the subsequent guarded attempt passed. Mac keep-awake assertions remain until reboot. Shared `E:\FileCat` remains on
`main`, tracked-clean. No GUI acceptance is inferred from these headless/native executions.

No new GUI acceptance, installed-package/filtered-user/RID/filesystem qualification, complete fuzz/scale campaign,
component reachability/SBOM release gate, full metadata publication or complete AW packet is claimed. No third-party
code or package was added/upgraded. The full implementation and validation loop remains active. The requested final
order is RAR licensing analysis after that loop, followed by the independent UX/UI/functional/security review.

## Evidence

Raw TRX/facts, earlier failures, actual child-terminal diagnostics, independent fixtures/readers and build/run logs are
retained in ignored `.tmp/test-results/archive-writing/tgz-v2` and the task-owned guest roots. Initial host focused
replays passed 98 cases; the later independent-input/protocol filter passed 51, with all eleven outputs independently
read back. The Linux diagnostic file output is opt-in test instrumentation, not a production logging/path capability.

| Transfer/result archive | SHA-256 |
|---|---|
| Initial production/test source overlay | `BC35AD6B71E34E88A5C41E3EE0BC336D79677CDB9A57DA4E98A5DA102B48AF53` |
| Independent-input/test-diagnostic source follow-up | `6BA7B030654C55F2D57DC2FC64411185E65A5F8C814D987A31DD687DBB6C8E8D` |
| Linux terminal-fact instrumentation follow-up | `6931AD28B74A342CC17F08459134544D0A060F788977058445152396EF59260E` |
| Windows 84-file managed overlay | `4DD154FA9E92BFCFA85E5061B38A38EA45812103CBB40AF3D484CA4315B57CC5` |
| Windows independent-input Core-test follow-up | `4F5773C6D5D97A75EB9AC8ED195EFC02FB4BEC42C710F2F3AF0863A058F65D03` |
| Mac results/fixtures/logs | `D722E46AAFBDE08FCED81108267DA3DE1B2D07FDE7CA118D64B20D0EB37EA47E` |
| Ubuntu results/fixtures/logs/child facts | `EAC0EAF2B87E76AB9E0D828C7FCB4B025CFEE83A108D172D4351B594BFB60742` |
| Windows results/fixtures/launch facts | `CA8F4D8822CE72A8D3CA61EAE7802C280F5C69CB214A2AD5A372D9CFC0F91515` |
