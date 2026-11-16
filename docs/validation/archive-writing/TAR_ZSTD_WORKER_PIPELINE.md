# TAR.ZST worker pipeline, 5 November 2026

Development checkpoint over `3f67ec0b`, with production registration still disabled. This does not complete the
[archive-writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) or qualify a release profile.

## Implemented behavior

The assessor, streamed rebuild and separate fresh verifier support TAR.ZST. The parent computes the complete ordered
expected member/content/metadata manifest. Canonical features `[4, dialect, checksum]` distinguish this wrapper and
record whether an assessed source has a checksum. Rebuilt output always includes one, including updates of a valid
unchecked source. Missing source checksum is not treated as corruption, authenticity or download-origin evidence.

The fixed-size header scanner checks magic, reserved/unused bits, dictionary-field presence, all content-size widths
and the declared window before constructing a decoder. Skippable frames/dictionaries/unknown extensions refuse.
Windows above 64 MiB or declared content above the physical policy refuse before codec allocation. A framing stream
checks each block type/size before delivery to unsafe codec paths, requires a complete final block and optional
checksum field, and checks physical EOF. It cannot forward another frame or trailing payload to a tolerant reader.
The existing decoder runs with end-of-stream checking and an explicit 26-bit maximum window. Codec format failures
become controlled refusals. Produced bytes, declared content size and the 1000:1 expansion policy are checked.

Compression uses the already-vendored ZstdSharp/Zstandard implementation. Offered levels map to 1, 3 and 9, with
default/normal both 3; getters verify the effective parameters. The encoder sets an 8 MiB window, no worker threads,
no long-distance matching, no declared content size and a content checksum. Window tuning and derived frame-size
representation are regenerated compression fields, not archived file metadata. Source headers are nevertheless
pinned byte-for-byte across the worker's bounded replays. File metadata, dialect/order and duplicate scope remain
governed by the complete TAR manifest. No dictionary or skippable metadata is silently discarded.

Fixed compressed-input buffering prevents tiny framing fields from becoming individual IPC requests. The encoder
uses bounded proxy-sized output buffers and does not force tiny blocks on TAR flushes. Healthy disposal must emit the
complete frame/checksum before a receipt; abort/failure revokes further output while releasing encoder resources.
The shared parent limits stage IO, output length and source rereads. The worker reuses its validated raw TAR catalog,
with two independent forward decoded replays/four physical source passes and no full-payload spool.

The source profile follows the framing fields described by [RFC 8878](https://www.rfc-editor.org/rfc/rfc8878.html)
and the [pinned Zstandard format document](https://raw.githubusercontent.com/facebook/zstd/v1.5.7/doc/zstd_compression_format.md).
[RFC 9659](https://www.rfc-editor.org/rfc/rfc9659.html) concerns HTTP window limits; it is not used to claim an 8 MiB
global file-format limit. FileCat's admitted 64 MiB maximum and output tuning are explicit application policy.

## Test scheduling defect found on Windows

The initial Windows broad Core replay had six failures, all the existing controlled ninth-waiter queue refusal:
"Eight archive tasks are already waiting." The production queue correctly enforced its one-active/eight-pending
limit. More parallel test classes now used its default shared instance; normal unrelated workflow tests accidentally
became queue-overflow tests. Failed cases crossed BZip2, Zstandard, TAR plan and transform/verifier fixtures.

Fixtures sharing that process-wide resource now use one xUnit collection, without disabling unrelated test parallelism.
Explicit concurrency, overflow and cancellation tests keep their own bounded queues. No production queue, admission,
worker resource, exit, verification or reaping check was relaxed. The follow-up changes test scheduling only; production
code is identical to the initial Mac/Ubuntu Core/UI/native replays. Initial failed Windows TRX/launch facts/fixtures
remain separate from the fresh follow-up evidence.

## Validation

| Environment | Final broad Core replay | UI | Additional native replay |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,284 passed, 181 skipped (1,465 total) | 219 passed | Storage 103 passed, 40 skipped; actual LPAC 52 passed |
| macOS ARM64 / APFS | 1,406 passed, 59 skipped (1,465 total) | 219 passed | Native retained-file preparation/private-creation cases included in Core |
| Ubuntu x64 VM / ext4 | 1,419 passed, 46 skipped (1,465 total) | 219 passed | Actual Linux confinement 56 passed, one platform skip |

All final replays have zero failures. Mac/Ubuntu UI and Linux native results come from the initial production-identical
run; their Core/oracle follow-up verifies the test-collection change. Windows repeats the full sequence with fresh
facts/output directories. The initial Windows failure remains visible and is not counted as a passing run. Native
Windows probe/application and Linux boundary cases positively reject a two-terabyte window with BudgetExceeded and
normal worker exit, and exercise streamed create/update/fresh verification. New private unmarked creation uses the
existing publication transaction; update publication remains unfinished.

Forty-nine dedicated Zstandard cases cover all content-size widths/unknown size, fragmented reads, every truncation,
header/CRC/framing mutations, unsupported dictionaries/skippables, window limits, expansion, abort/finalizer IO,
four physical passes and buffered request counts, level options/encoded-byte differences, exact duplicate edits,
remove-all and independent fixtures. A 2 MiB incompressible multi-block payload passes the real process pipeline with
bounded observed stage writes. Host focused runs pass 137 cases plus the later level-output case. The corrected broad
replays include all 49 cases and neighboring TAR/gzip/BZip2/queue/metadata tests.

SDK 10.0.101; actual test runtimes are Windows/Ubuntu 10.0.14 and Mac 10.0.12. Builds complete without warnings/errors.
Windows launch facts record integrity 12288 before / 8192 after and exit 0, retaining admin membership; this is not
ordinary filtered-user installation qualification. Mac controlled processes do not qualify a hard-memory boundary.
No GUI acceptance, production registration, complete metadata/publication/recovery, cooperative cancellation,
maximum-scale/global-budget, coverage-guided fuzz or all-RID/package qualification is inferred.

Final native process checks found no FileCat, worker, dotnet or testhost instance on any guest. Mac user-active and
idle-display/system-sleep prevention assertions remain until reboot. Shared `E:\FileCat` stays on `main`, tracked-clean.

## Independent interoperability and packaging checks

The native C libzstd oracle is separate from the managed port, with exact binary hashes/version/origin in
[the test inventory](../../../eng/validation/archive-writing/zstd-oracle-inventory.json). Hash admission occurs before
loading the library. Windows uses the official 1.5.7 release DLL; Mac and Ubuntu use their inventoried 1.5.5 libraries.
The release API provided no Windows ZIP digest: its recorded SHA pins observed TLS-origin bytes, not a publisher
signature. Test tools are not copied into shipped or worker/application asset directories.

Oracle input/output is capped at 16 MiB. Single-pass native decoding uses fixed caller-owned output, checks complete
single-frame size and checksum, and never extracts members to disk. Independently generated USTAR/PAX duplicate-member
inputs include exact mode/owner/nanosecond metadata and both checksum states. Each accepted replay checks twelve
FileCat Zstandard outputs, plus eleven BZip2, eleven gzip and eight exact-time TAR fixtures. Window/header admission,
fresh verification and the unregistered native private-creation path are exercised separately.

The Windows development payload passes DependencyNotices (125 verified files) and updated SPDX generation naming
the reachable Zstandard encoder. The SBOM validates 261 exact files, 34 reviewed NuGet packages and one runtime pack,
with 235 restored files matching their package archives. It uses the base commit plus dirty-source SHA, not a clean
release identity. No upstream codec source, package or license term was added, changed or upgraded; existing BSD/MIT
notices remain applicable. The fork change inventory records the newly reachable existing encoder.

## Evidence

Raw logs/TRX/launch facts/child facts/fixtures are retained in ignored `.tmp/test-results/archive-writing/zstd-v1`,
host smoke/focused/level directories, `zstd-artifacts-v1`, and task-owned guest roots. Subsequent report/status edits
do not change tested production code. Runtime/package/fuzz/scale and complete application workflow qualification
remain required. The RAR analysis and feasible implementation follow the main implementation loop, before the final
independent UX/UI/functional/security review.

| Transfer/artifact | SHA-256 |
|---|---|
| Initial 21-file source overlay | `5C811E7221F91431C95614F77559914242A73D9E403AC99C800FA861E3F60A5C` |
| Initial Windows 88-file managed overlay | `EAD8947BA8BCB7AF1A40C7EAA4AD54E002FEF70801E22CBE8B469117BC8FB484` |
| Independent Windows input fixtures | `2E05811BA0EC5618E8975F27D795B53738A02531283281652CDD60935B06C532` |
| Thirteen-file test-collection source follow-up | `86C894745CFC6D86ACDD15E6A54AE7D76E0EAAE3032F1D79C8EED0591F37159E` |
| Windows Core-test DLL/PDB follow-up | `BCFF5470E5A845D7C3DF7FF0C699DCB5C89034B68739DC46A7D5AF7F7227AF50` |
| Initial Mac evidence | `021738118D8AB47F49B04C524856A385AF5E1E5A44D09873D342D4EE93E09CE6` |
| Initial Ubuntu evidence | `12B9572E448D04FAC4A0FAE2853704297BCE2255E24F8AEC85FF1E8F8556B12D` |
| Initial failed Windows evidence | `D80B6831C6749AA2A9DBA9B7E56295FB13FBA42E5688C756578594C7BA470C6C` |
| Mac Core/oracle follow-up | `02481AB66E38AC5F9973709ABE54DE0FAC4AF8F7DF9FDF484F7366986DEBCDA4` |
| Ubuntu Core/oracle follow-up | `6C8BFF4F1B26F6E322868AE64B4EE4D88BA5523693FA0FF2E79D7FEBBBFDFD80` |
| Windows complete follow-up | `CBE40D2CF96463F7302E86FADF723C7130808E6921FF00AA90B9C0545DE3DA63` |
| Development application SPDX SBOM | `1F375B88E414D64021F55749A524A33E3E7AFA9F81A7196F03B191D9961EA1D5` |
