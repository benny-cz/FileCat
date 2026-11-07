# Streamed 7z codecs/container and password-derivation checkpoint

**7 November 2026.** The bounded 7z creation foundation now writes Copy, LZMA, LZMA2, BZip2, raw Deflate and the
Zstandard extension already read by FileCat. RAR5 and 7z password-derivation primitives also exist. Neither result
enables a production profile or completes password-protected archive support. Required PPMd7, filters, compressed
headers, actual cipher/header/member integration, password-aware reading/workflows and all remaining gates stay open.

## Implementation

`SevenZipCodecEncoder` maps actual codec IDs/properties and bounded parameters to the existing permitted encoders.
LZMA/LZMA2 use 16/32/64 fast bytes and validated power-of-two 1–16 MiB dictionaries. BZip2 uses 1/5/9 blocks;
Deflate uses framework Fastest/Optimal/SmallestSize; Zstandard uses the existing bounded frame adapter's 1/3/9 levels.
Copy rejects ineffective level settings. Default level zero aliases a real profile preset. No pinned upstream
writer's unused compression-level property is used. Preset effect/resource/interoperability coverage is not complete
merely because these mappings exist; this batch exercises default presets and rejects invalid/ineffective settings.

`SevenZipArchiveWriter` freezes/preflights names, namespace, modes, numeric grants, times and cumulative metadata/
logical sizes before stage writes. It streams independent single-coder folders and raw file metadata/header records,
then backpatches the fixed 32-byte signature through the empty owned seekable stage. CRCs cover packed streams,
unpacked contents, next header and start header; SHA-256 must match each captured input. Fixed output/header buffers
batch byte-oriented codec writes instead of issuing one worker IO call per encoded byte. It has no path, original
archive update, publication, deletion, recovery or secret authority. No complete archive/header `ToArray` buffer is used.

Empty regular files/directories have explicit distinct stream flags; no phantom packed folder is created. Names are
UTF-16LE exactly. Ordinary Unix modes are carried using the standard extended attributes representation. Mtime and
optional creation/access fields preserve presence and exact 100 ns FILETIME values; unrepresentable finer values or
pre-1601 times refuse. The common workflow must still implement its declared new-input precision policy; these
target-entry primitives must not silently round source metadata or claim arbitrary nanosecond 7z representation.

No new 7z third-party source has been imported for this writer. It uses FileCat container/CRC code and already retained
permitted codecs. The [analysis ledger](SEVENZIP_FEASIBILITY.md) still controls the missing reviewed PPMd/filter paths.
The `Ppmd7` enum currently refuses before IO with an explicit implementation-gap reason; it is not advertised support.
Zstandard is the known 7z extension, not a claim that every 7-Zip version/plugin supports it.

## Password primitive scope

- `ArchivePasswordEncoding` preserves exact Unicode/spaces with strict UTF-8 for RAR or UTF-16LE for 7z; no trimming,
  normalization, truncation or silent replacement. The current helper resource ceiling is 1,024 encoded bytes.
  Creation UI/password compatibility ranges still need separate selection/qualification; this is not a format maximum.
- RAR5 key, checksum-key, password-check and tweaked CRC processing adapts the pinned BSD-2-Clause rardecode
  `archive50.go`/`reader.go`, with exact source/blob/hash provenance in
  `eng/validation/archive-writing/rar5-independent-probe/password-reference-sources.json`. Copyright/terms are retained
  in the file and existing licence/notice inventory. No restricted UnRAR source was used.
- 7z SHA-256 derivation handles normal cost powers and the explicit legacy direct-key operation. New output must use
  a separately qualified normal KDF; the legacy operation is not a writing speed preset. Existing decoder API presence
  does not prove complete password support (its old direct-key path remains blocked by an earlier property guard).
- One shared monotonic job budget reserves rounds/hash work before derivation. Current helper ceilings are 2^26
  rounds and 8 GiB of accounted hash work, with per-operation normal powers at most 24. Invalid/over-budget inputs
  refuse; cancellation is checked during loops. These counters do not replace hard worker CPU/time/memory limits.
- Owned mutable key/password work buffers are zeroed on disposal/failure, and disposed keys refuse access. Key objects
  have no record-generated value diagnostics. This does not claim complete erasure of framework/immutable copies.

**Still required:** actual AES cipher/padding/data/header framing, fresh salts/IVs, password checks during read/open,
secret grants and reader key-provider wiring, fresh password-aware verification, session/cache/recovery handling,
UI confirmation/hide-name/retry/cancel flows and independent encrypted interoperability. No real user password or
VM login credential was used as an archive key. Tests use labelled public synthetic vectors. This checkpoint must
not be presented as password-protected archive support.

## Actual consolidated machine evidence

| Platform | Passed | Platform skips | New 7z cases | Password primitive cases | Independent native record checks |
|---|---:|---:|---:|---:|---:|
| Mac | 520 | 2 | 21 | 19 | 42 |
| Ubuntu | 519 | 3 | 21 | 19 | 42 |
| Windows, loaded actual standard account | 401 | 121 Unix-only | 21 | 19 | 42 |

All new cases pass. Each codec writes seven ordered members including Unicode/nested names, ordinary and empty
files, directories, mixed boundaries and **2 MiB + 1 byte** incompressible input crossing LZMA2's chunk boundary.
Independent APIs verify payload SHA-256, size, kind, modes, exact modification times, and optional creation/access
field presence/values. Libarchive reports the 7z creation field through its `ctime` entry API; this is not a claim
that POSIX changed-time metadata is the 7z creation field. All six archive hashes match across all three platforms.

Password vectors compare RAR's phases with independent framework PBKDF2 iteration counts and 7z with the existing
separate decoder KDF implementation. Tests cover check-data corruption/wrong-password distinction, CRC transformation,
encoded-length/Unicode refusal, cumulative work, disposal/zeroing and cancellation before work. These are primitive
checks, not end-to-end encrypted archive, mid-operation cancellation or hard-resource qualification.

Mac/Ubuntu use separately built pinned libarchive 3.8.11 with required codec dependencies enabled, unlike the earlier
RAR-only oracle. Actual versions are in the machine-readable checkpoint. Windows uses its system libarchive 3.8.9
UTF-8 APIs under the standard account. No test library became a FileCat runtime dependency. Ubuntu's four missing
development packages were installed together; Mac reused existing SDK/Homebrew headers/libraries. No OS locale changed.

The Mac native UTF-16 conversion presented precomposed `é` as decomposed UTF-8. Its first name check failed and was
retained. The correction independently observes/checks start/next-header CRCs, the raw UTF-16 Name property and coder
graphs of these bounded owned fixtures; **stored code points remain exact**. Only the Mac native presentation may
be canonically decomposed, and only alongside that exact raw-name check. The generator/input is not normalized.
FileCat's reader also returns the exact original name. This fixture-only observer is not production 7z admission.
A simultaneous composed/decomposed-name fixture remains queued for the next encoding/graph qualification chunk.

Compiler checksums match **1,292 document instances**: **12,302,093 bytes** each on Mac/Ubuntu and **12,316,757** on
Windows, including actual reader/writer/test assemblies and generated test content through the explicit NuGet source
root. Counts include linked instances, not unique files. The complete source packet contains **3,988 files** and is
**30,365,048 bytes** compressed, SHA-256 `00E01C526863C8B125775A32FEB373EB2AE1389B826EDDD531320B07F32A47DB`.

Final archive identities, exact method hashes and fully verified manifest counts are in
[`sevenzip-crypto-checkpoint.json`](sevenzip-crypto-checkpoint.json). All final archive paths/counts/payload hashes
were checked locally after collection. All owned FileCat/test processes ended before freezing; a later Windows tester
was left alone and artifact transfer waited for idle status. The Mac's until-reboot wake helper is preserved.
Host work was editing, compilation and source/artifact inspection only; no host live test or UI execution occurred.

## Remaining work

Implement the required PPMd7 and filter/coder-graph inventory, compressed headers and real option coverage, then finish
RAR/7z encryption and key-provider/worker integration. Complete strict raw source analysis, semantic manifests and
independent fresh verification, shared capture/snapshot/global-resource ownership, publication/update/edit/move/UI,
fault/scale/fuzz/six-RID/package/native gates and final independent review. No source/profile gate is waived by the
successful codec/KDF batch. The explicit goal remains active.
