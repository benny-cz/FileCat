# Confined TAR transform checkpoint, 5 November 2026

Status: partial implementation, no new writer profile registered. This builds on the parent expectation checkpoint
`ba7bef43` and remains subject to the complete [archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Implemented behavior

`ArchiveTransformWire.Prepare` converts a frozen parent TAR plan into a stream-only request. Each replacement/addition
payload reference gets a distinct consecutive positive grant, even when several references use the same captured
object. The parent retains the alias-to-captured-ID map and expected manifest; neither crosses the worker wire. Source
ID zero is reserved for the original archive and stage ID minus one is the fixed parent-owned writable stream.

The version-1 transform schema is TAR-only. It carries an optional physical source length/SHA, bounded record counts,
a parent-computed output ceiling, input facts, exact member occurrence edits and flattened additions. All operation,
reason, message and record enum values used on the wire now have explicit numeric assignments. Parsing checks exact
schema/flags/counts, UTF-8/name limits, timestamps/modes/lengths, complete record EOF, source revision/key uniqueness
and the exact once-used grant set. Containers grow with validated records rather than reserving large arrays from a
small header alone. Existing framed/fragmented-record budgets and poisoned-channel behavior remain in force.

`ArchiveTransformWorker` uses the stream-only TAR engine after native isolation. The engine checks the complete raw
source and runtime decoder, including the source namespace before edits, and charges deleted/replaced source content
plus every requested new payload reference against the logical budget. Its receipt contains the actual stage length
and semantic manifest digests. No paths, expected-output digests, publication, rename, deletion or journal authority
are supplied to the worker.

`ArchiveTransformClient` grants only the fixed source/input IDs and bounded stage IO. Source IO derives from three
original lengths plus the captured reference lengths, within the existing authority's real-grant amplification cap.
Stage writes/reads/extensions share a finite IO allowance and are checked before the actual stream is touched. The
parent verifies receipt length against its actual stage and compares all semantic digests against its expectation
only after complete terminal framing, EOF, actual exit, final diagnostics/CPU accounting and reaping. A receipt is
still unverified: the caller must retain/rereview and durably seal the native stage, then run fresh verification.

Assessment, transformation and verification now share `ArchiveWorkerSession`, preserving the queue reservation through
cleanup on success, refusal, protocol fault, cancellation and deadline/resource failure. Fresh source grants check
cancellation before opening. Valid bounded bytes/records advance progress; heartbeat frames cannot prolong a task.
Cancellation still uses immediate termination, not the planned dedicated cooperative channel/two-second grace.

The Windows application dispatches `--archive-transform-worker` before UI/single-instance startup and repeats its
actual LPAC token/job checks before input. The fixed launcher shares the existing native controls. The Linux probe
dispatches the role only after its native launcher/worker controls. Production Linux and Mac application routes remain
unavailable, and there is no ordinary-process or in-process codec fallback registered in the product.

## Bounds and traffic

The initial parent stage ceiling includes both end blocks, each member header, rounded payload, and for PAX one
extension header plus rounded UTF-8 name/semantic-metadata bodies and 4 KiB fixed field/framing room per member. An
empty archive receives only 1 KiB. This is a finite enforced ceiling under qualification; full runtime/profile bound
audit and space/snapshot admission remain required before enablement. Current
[runtime source inspection](https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Formats.Tar/src/System/Formats/Tar/TarHeader.Write.cs)
supports the PAX framing model, but is not an audit of the exact serviced binary or an independent implementation oracle.

Physical hashing now explicitly requests up to 65,524 bytes per proxy read, rather than using HashData(Stream)'s
runtime-selected small buffer. A 4 MiB fixture verifies complete physical/raw/runtime passes with fewer than 400
source reads. The finite frame limit is 8,000,000; IO commands retain independent frame/payload/authority bounds and
do not spend the 128 MiB semantic-control budget. Tests check the final allowed zero-payload frame/poisoning and an
exhausted semantic budget followed by valid fixed IO framing. These are boundary checks, not multi-million-frame or
64 GiB scale qualification.

## Observed validation

| Native environment | Full affected Core regression | Affected headless UI | Native containment |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,047 passed, 98 skipped; 1,145 total | 217 passed | 29 passed, 0 skipped |
| macOS ARM64 / APFS | 1,100 passed, 45 skipped; 1,145 total | 217 passed | Unqualified; portable child compatibility only |
| Ubuntu x64 VM / ext4 | 1,113 passed, 32 skipped; 1,145 total | 217 passed | 42 passed, 1 platform-only skip; 43 total |

No failures occurred. Skips are retained in the TRX and are not counted as passes. The broad filters were
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`. Native lanes used
the tracked Windows containment and Linux archive runners. SDK 10.0.101 builds completed with zero warnings/errors;
Windows/Ubuntu test runtime was 10.0.14. The Mac runtime version was not independently captured; see the
[later runtime inventory correction](STAGE_CREATION_RESERVATION.md). The first v1 run passed before explicit enum assignments and the maximum-name cases were
added; final v2 runs above supersede it and counts are not combined.

The new cases cover typed-wire corruption and immutable aliases, creation/update/empty/remove-all, exact duplicate
changes and metadata preservation, changed captured content, substituted roles, unsolicited source/stage IO, final
stderr/CPU failures, forged manifests, trailing bytes, cancellation and queue ownership through reaping. The maximum
Unicode name uses 32,000 CJK characters (96,000 UTF-8 bytes), crossing both control-record and stage frames. It passes
the portable worker/fresh-verifier roundtrip on every OS, both Windows LPAC probe/application routes, and Linux's
assess-transform-fresh-verify sequence. Native tests assert distinct process IDs, successful exit and reaping; Linux
also checks every owned controller was removed. Test stages are bounded MemoryStreams, not native publication tests.

Existing native memory/CPU/parent-death/descriptor/syscall cases also passed. The Linux driver's service metrics do not
describe worker memory. Windows facts record actual test-parent integrity changing from 12288 to 8192 with exit 0;
administrator membership/privileges remain, so ordinary filtered-user/installed-package qualification is outstanding.
Headless tests are not native GUI acceptance. No coverage-guided campaign, CPU-hour floor or release-scale claim is made.

23-file source overlay SHA-256: `A0CA1D4CB330A8D9119FA33E98CED9B9CA1873E9925CFA2E7835F9F1D2D77351`.
84-file Windows managed overlay SHA-256: `DA2FF385CDD04AEE5D22444192B4E7DDE0A2CE262C446F05663468BC4662C618`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `8B3F0A5EEE11BAE2CB9F84597421529987582E54A4630B5D5BBA8EA3697A3A8A` |
| Windows Core.Tests.dll | `AA94D0CC4EF3F2036D7FF70559168E56A2EC3CA6C96FB82DF4EE45962CD9D9C2` |
| Windows Platform.Windows.Tests.dll | `A609F4867EF98FE3BEACD3CF895165BA20796F450B09CA0A5162B8A66E5D0027` |
| Windows application FileCat.dll | `C46EA8E1957195F48DF819CD7EE0F78662DD29AA6DA14560795AB7F118C5CF31` |
| Windows probe FileCat.WorkerProbe.dll | `DEC5A9F48F8FAEA2439C4EEB5F475346C00078BA8B99275D94096750F4236B73` |
| Mac Core.dll | `F6AEF9C776A6A66322A4C9D7F722BE56AEDF343C1A817A8C41FE6DD01C80CFF7` |
| Mac Core.Tests.dll | `3A4B0ED3988E752EC05E53A8928BC0A3759B47CCED6F21D5B20ECB9F475AE6DB` |
| Ubuntu native probe Core.dll | `B46833CC8EBE1CC2CA963C91CE7A51D574D4620E115D593EE4E251CF10BB8CB8` |
| Ubuntu native FileCat.WorkerProbe.dll | `3ECBC5C33206CE4662600CC2284204A9F5623C0A2544551AC4310E9DC54C1402` |
| Ubuntu Core.Tests.dll | `3D4BF99AEE49A016A40694B379873E7A506F8DB728E0CD805563FD28644E952D` |

Raw source/binary overlays, build logs, TRX and launch facts remain in ignored `.tmp` and
`.tmp/test-results/archive-writing/tar-transform-v2` plus owned guest validation directories. Runs started after idle
checks; final censuses found no FileCat/test instance left by this task. The Mac's temporary until-reboot awake
assertions were retained. No other tester's instance was stopped. The shared checkout remains on `main`.

Still required: production contracts/registry and qualified native factories, cooperative cancellation, cross-parent
scheduling and global budgets, reviewed file/folder snapshots/aliases/self-inclusion, real native stage sealing and
metadata/origin rereview/application, journaled publication/reconciliation/recovery/crash integration, complete UI
workflows, all other formats and ZIP migration, complete runtime/package/RID/filesystem/scale/fuzz/oracle/GUI evidence.
The separate whole-goal independent UX/UI/functional/security review remains pending after the implementation loop,
followed by the deferred RAR licensing analysis. This checkpoint does not complete the goal.
