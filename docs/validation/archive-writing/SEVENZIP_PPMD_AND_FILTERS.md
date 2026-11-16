# Managed PPMd7 and streamed 7z filters checkpoint

**7 November 2026.** Seven 7z codecs and fourteen filter configurations now pass the bounded creation checkpoint
on Windows, macOS and Ubuntu. This completes another codec/container slice of the [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md),
with an actual coder-order interoperability fix. Production profiles remain unregistered. BCJ2, compressed headers,
password archive integration, strict catalogs, shared workflows and the remaining qualification gates are unfinished.

## Implementation and provenance

`Ppmd7zEncoderStream` drives the existing BSD-2-Clause rardecode model through bounded symbol-selection hooks and
a managed 7z encoding range sink. The independent public-domain SDK grammar supplies the range-format reference;
the excluded UnRAR-derived model is not restored. Model order is 2–64 and memory 2 KiB–32 MiB. Real presets select
order/memory 4/4 MiB, 6/8 MiB and 8/16 MiB. Default known-size members use five-byte range finalization without
an end marker. These bounds are the current implementation profile, not the format's theoretical maxima.

Delta now subtracts history when encoding and adds it when decoding. The encoding filter wrapper preserves
incomplete instructions across writes, does not mutate caller bytes and emits incomplete EOF tails unchanged.
ARM64 and RISC-V are new scalar ports of individually public-domain SDK `C/Bra.c`, replacing the previous files
whose origin was unrecorded. Original bounded Swap2/Swap4 implementations and reader registrations are added.
The container serializes explicit coder/binding/packed-stream/output-size graphs. Compression precedes its filter
in the coder list; filter input 1 consumes compression output 0. All tested transforms preserve total length.

The [exact source ledger](../../../eng/validation/archive-writing/sevenzip-feasibility/ppmd-filter-sources.json)
pins SDK 26.05 commit `b88c6bc09339de32f7d18bbab900686b244f42ba` and the 22 individually reviewed C/header files
used by the private independent oracle. C++ wrapper inspection established method IDs/defaults only; no wrapper
code is selected for reuse. Component notices, changes, licence hashes and the artifact-SBOM description are updated.
The official binaries have a [separate test-only inventory](../../../eng/validation/archive-writing/sevenzip-feasibility/official-test-tools.json).
They never enter FileCat's runtime or production package dependency closure.

## Actual tests and independent results

Each platform ran the same 562-case selection. Mac passed 560 with two platform skips; Ubuntu passed 559 with
three; Windows passed 441 with 121 Unix-specific skips under the actual non-administrator account
`fcarchive42829297`, with its profile loaded. No failure is counted as a skip. All 28 `SevenZipWritingTests`,
33 `SevenZipFilterEncodingTests` and 19 password-primitive cases passed on every platform. Password-primitive
results do not establish encrypted archive support.

- Seven codec fixtures cover Copy, LZMA, LZMA2, BZip2, raw Deflate, the Zstandard extension and PPMd7, with regular,
  empty and Unicode/nested members, directories, optional timestamps and a 2 MiB + 1 byte chunk boundary.
- Fourteen transform configurations cover Delta distances 1/2/7/256, x86, ARM, ARMT, ARM64, PPC, IA64, SPARC,
  RISC-V and Swap2/Swap4. Byte writes, instruction tails and reads of 1/3/8191 bytes are checked. Encoding is
  identical across input chunks 1/2/3/5/7/8191/65536 and whole input, including Flush between writes.
- Nineteen raw vectors match the independently compiled SDK encoder byte for byte: all fourteen filter cases
  and five PPMd models spanning 2 KiB–8 MiB and orders 2–64. No SDK oracle runs on the host.
- Official 7-Zip 26.05 tests 20 archives and independently streams 44 exact payloads per platform. All fourteen
  filter containers, including RISC-V and swaps, pass. This uses private binaries and no filesystem extraction.
- Supporting libarchive readers check 60 exact records per platform: names, directory/file distinctions, lengths,
  mode, mtime, optional creation/access presence and values, and content hashes. Raw UTF-16 names are separately
  observed on Unix; only Apple's native UTF-8 presentation may use NFD. Stored names are not normalized.

All 21 final fixture archives and all 19 SDK vector outputs are byte-identical across the three platforms.
The machine-readable [checkpoint](sevenzip-ppmd-filter-checkpoint.json) records their individual hashes and results.

## Findings and concrete compatibility limits

1. The first valid graph listed the filter before its codec. Official 7-Zip accepted it, but libarchive 3.8.11
   rejected it with `Unexpected codec ID`. The implementation now uses conventional codec-first ordering and
   the correct binding; a fresh full three-platform batch passed. Earlier source packets/logs remain retained.
2. Official 7-Zip 26.05 does not register 7z method `04F71101`, despite reading standalone Zstandard. Its explicit
   unsupported-method result is retained separately and is **not** one of the 20 passing official archive tests.
   Libarchive independently verifies that fixture. The format choice must disclose this extension's compatibility
   limit; universal official 7-Zip compatibility is not claimed.
3. Windows system libarchive 3.8.9 failed PPMd with `Failed to decode PPMd`, while official 7-Zip and Mac/Ubuntu
   libarchive 3.8.11 passed the identical archive. A private 3.8.11 PPMd/Copy build also passes it in Windows.
   The [oracle inventory](../../../eng/validation/archive-writing/sevenzip-feasibility/windows-ppmd-oracle.json)
   records source, compiler, configuration, DLL/import identities and upstream reader comparison. Windows checks
   53 records with its system reader and seven PPMd records with the private current reader. The original failure
   remains in its final bundle. The system DLL is unchanged; Windows built-in-reader PPMd compatibility is limited.
4. The secondary libarchive setup lacks Swap2/Swap4 and the pinned liblzma 5.4.5 lacks RISC-V. These are excluded
   from its 60-record subset, with explicit inventory. Their actual containers pass official 7-Zip and their raw
   bytes match the SDK. This is not a skipped-as-passed secondary-reader claim.
5. Official 26.05 advertises a Deflate64 encoder. FileCat's absent Deflate64 path is an implementation gap;
   technical impossibility has not been established. Arbitrary plugins, solid output and format-maximal dictionaries
   remain separately unqualified. The tested PPMd/default-codec batch does not finish all preset/resource campaigns.

## Frozen evidence

The source packet contains 4,002 files, 30,076,254 bytes, SHA-256
`4B5C4FC23DCB5B0846E7526402B9ADFCCC6AF4A4E61E01040322A835AFA14DDD`.
Actual portable-PDB compiler documents are captured and checksum-verified: 1,295 document instances,
12,334,962 bytes on Mac/Ubuntu and 12,349,626 on Windows. These cover the tested product/test code. Subsequent
changes to reports, licence/SBOM descriptions and the Windows oracle continuation do not change that product code.

All final bundle members, sizes, hashes, path types and root manifests were verified locally after transfer completed.

| Platform | Final private bundle | Bytes / manifest rows | SHA-256 |
|---|---|---:|---|
| Mac | `mac-sevenzip-ppmd-filter-evidence-v2.tar.gz` | 67,197,665 / 1,485 | `A721CA6BDD689FBF16648E2B5A4628A741D16CE2C17E2EEF23DA1EA8B385BC6F` |
| Ubuntu | `ubuntu-sevenzip-ppmd-filter-evidence-v2.tar.gz` | 67,194,519 / 1,485 | `0BA8F9C75DAC27932DF005790F09DBCB32E4105BC4B728702549612342EF3676` |
| Windows | `windows-sevenzip-ppmd-filter-evidence-v3.tar.gz` | 74,664,748 / 1,515 | `99D73C75FC480864CCF9A1B21E546F81F5DA00285C4BA89AC81784C9687106BF` |

Runners, actual Windows continuation, source packets, native tools/notices, compiler inputs, binaries/PDBs, TRX,
SDK/official/native records and final idle census are retained. Owned test processes exited. The Mac's existing
until-reboot wake helper is retained. No host FileCat/test process or GUI/input was used.

Next codec work includes bounded four-stream BCJ2 with numeric scratch grants and compressed headers. Cipher,
encrypted-header and password-aware reader integration for **both** RAR and 7z follow at the container layer,
before freezing strict catalogs/wire versions. Parent budgets, source/verification manifests, transactions/UI,
move-into/edit/update, actual six-RID/package/fuzz qualification and the final independent review remain required.
