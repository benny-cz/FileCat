# Exact TAR timestamps, 5 November 2026

Partial implementation over `33a9c2a8`. No new profile is registered or enabled. The complete
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains incomplete.

## Contract and encoding

`ArchiveTimestamp` represents UTC as floor Unix seconds plus 0–999,999,999 nanoseconds. Its supported range is
year 1 through year 9999, including the final nanosecond. DateTimeOffset conversion is exact and refuses finer
values; it cannot silently round. Existing DateTimeOffset input constructors remain compatible. Retained Unix
input observations now carry native nanoseconds, including pre-epoch fractional instants; source rereview checks
the exact instant alongside native object/content/metadata facts. Windows FILETIME precision remains unchanged.

The transform role handshake now advertises revision 2; input records use schema 2 with signed seconds and an
explicit nanosecond field. Alias creation, input parsing, parent expectation and worker descriptors retain both
fields. Invalid ranges, negative/overflow nanoseconds and old input schemas refuse before IO grants are used.
Other request/edit/addition record schemas retain their previous version. The old transform handshake is rejected.

Canonical TAR member metadata retains schema 1 for tick-aligned timestamps. When any recorded mtime/atime/ctime
requires finer precision, schema 2 encodes every present timestamp as seconds plus nanoseconds. Ordered fields,
presence markers, UTF-8 lengths and full EOF remain checked. The parent and fresh assessor independently derive
these representations. Replacement retains original member metadata, including optional time presence; rename
changes only the name. USTAR additions keep the existing whole-second dialect rule; existing USTAR metadata still
must fit that dialect. New-file capture does not harvest filesystem atime or ctime.

The raw PAX observer admits decimal timestamps through nanoseconds and refuses finer recorded precision, malformed
decimal grammar and unsupported ranges. It compares exact PAX dictionary values with the runtime decoder, as well
as checking the runtime's tick projection. The projection is used only to compare the decoder/set its placeholder;
it does not become the semantic manifest timestamp.

## Bounded runtime adapter

.NET's writer replaces the `mtime` extended attribute with its DateTimeOffset field before writing. Its reader
projects decimal epoch seconds to ticks by truncating toward zero. These behaviors were checked against the
[pinned .NET 10.0.12 writer](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Formats.Tar/src/System/Formats/Tar/TarHeader.Write.cs)
and [timestamp conversion source](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Formats.Tar/src/System/Formats/Tar/TarHelpers.cs),
and actual 10.0.12/10.0.14 runtime tests below.

`PaxTimestampWriteStream` narrowly adapts the runtime encoder's per-member PAX header. It validates that header's
type, magic, size and checksum before allocating a bounded extension buffer, checks padding and complete PAX
record framing, replaces only mtime, then updates the extension length/checksum/padding. The ordinary member
header and payload still come from TarWriter. Its expected member byte count and final two zero blocks must match.
It has no path authority, seeking or payload buffering. Checked seekable payload views still prevent the runtime
from buffering an entire input. Failure poisons the adapter; encoder disposal cannot mask the original source or
cancellation error or append data to an incomplete entry. The parent still seals and freshly verifies the whole stage.

## Validation

| Environment | Affected Core regression | Additional native replay |
|---|---|---|
| Windows 11 x64 VM / NTFS | 1,124 passed, 142 skipped; 1,266 total | Storage: 91 passed, 21 skipped. LPAC: 33 passed |
| macOS ARM64 / APFS | 1,219 passed, 47 skipped; 1,266 total | Native nanosecond observations and journaled ordinary-child create/update included |
| Ubuntu x64 VM / ext4 | 1,232 passed, 34 skipped; 1,266 total | Confined Linux: 44 passed, one skipped; 45 total |

No final failures. SDK 10.0.101 builds/publishes passed with zero warnings/errors. Broad Core filters include
`Archive`, `EditSession`, `BackingArchiveTarget` and `PaxTimestamp`; native replay uses the tracked containment
runners. TRX `NotExecuted` results are skips, not passes. Thirty added cases cover exact boundaries/negative
instants, refusal of unsupported precision, alias/wire/worker roundtrips, independently framed PAX replacement/rename,
fragmented adapter writes, malformed/oversize headers, truncation, native capture and changed single nanoseconds.
Actual native Mac/Ubuntu journaled create/update fixtures use `utimensat` and inspect the fresh manifest's full
seconds/nanosecond fields. Existing source-change regressions caught a finalizer masking error during the first
focused run (90 passed, one failed); after fixing disposal, all 91 focused tests and the final broad replays passed.
Initial compilation corrections are not counted as passing runs.

The independent Python stdlib TAR reader checks eight generated archives per platform: four exact-time creations
and four replace/rename outputs. It inspects independently parsed PAX strings with decimal arithmetic, exact member
order/count and SHA-256 of every payload; it never extracts files. Mac Python 3.13.3 and Ubuntu's guest Python run
locally; Windows guest outputs were checked on the Windows host with Python 3.13.13. These are independent-reader
checks, not completed bidirectional GNU/libarchive, fuzz, scale or full qualification gates. Ubuntu's oracle uses
Python 3.12.3.

Test-host runtime evidence remains .NET 10.0.12 on macOS 27.0.1 and .NET 10.0.14 on Windows build 26340/Ubuntu
24.04.5. Windows parent facts record medium integrity after launch; administrator membership/privileges are retained.
The Mac worker memory boundary remains unqualified. These results do not establish ordinary filtered-user,
installed-package, all-RID/filesystem or GUI acceptance. No UI change or new GUI acceptance is claimed.

Final runner/evidence censuses found no FileCat/test process left by this task. Mac caffeinate PIDs 1339/6428 remain;
UserIsActive, PreventUserIdleDisplaySleep and PreventUserIdleSystemSleep assertions were present. Shared `E:\FileCat`
remains tracked-clean on `main`. Full metadata/security/origin application, scalable capture/folders/global budgets,
publication/recovery, production/UI integration, other formats and final independent whole-goal review remain open.

## Evidence

Raw TRX, launch facts, oracle fixtures/logs and evidence archives are retained under ignored
`.tmp/test-results/archive-writing/nanosecond-v1` and the task-owned guest roots.

| Tested transfer or result archive | SHA-256 |
|---|---|
| Twenty-file source overlay | `D7D60B6F022F0CA3162A45F4A71D9E73030B754A76B49156F2E6BBBA79DB5901` |
| Windows 84-file managed overlay | `85FA2DBDCEE19289AD8888ED7FC288859D423DB2BDFDF74BE460A2E6237EAF0B` |
| Mac results/fixtures | `AF934297F4902A8CB70A5B7650EEE92B97A5C9214C13C63E94A997A0577914DE` |
| Ubuntu results/fixtures | `B8BF6785361CD5B5E88C0E868FED1FE82A8988D0B39AA7DBDDF5589D6D36CD6A` |
| Windows results/fixtures | `775D6C6FF1BB81A26539A07FAFD096D1B8BBF56FF809EA9F728A147081164F5E` |

Only the explanatory 32/36-byte source comment and checkpoint documentation were added after these builds.
No third-party source or runtime package was added or upgraded. This checkpoint completes no whole AW packet.
