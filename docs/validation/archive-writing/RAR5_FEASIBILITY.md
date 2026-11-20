# Independent RAR compression: feasibility checkpoint

**Scope amendment, 7 November 2026:** the user subsequently requires password support for both formats, including
payload/header encryption and password-aware reading/verification/workflows. Earlier unencrypted-only statements
below describe the reviewed starting point and are superseded by plan §4.3.1. Encryption/KDF/source closure and
runtime qualification remain required work; no earlier codec/source result proves password support.


**Decision:** technical go for implementing an independently authored, bounded compressed RAR5 engine. The user's
preferred route has a working candidate, so the stored/external fallback is not activated by this checkpoint. This
is a feasibility result, not complete compression-method coverage or a production/release qualification. The broader
implementation goal and the [controlling plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remain open.

The experiment ran on 6 November 2026, with the result recorded across the 6/7 November boundary. The user's next
requested analysis is the equivalent 7z licensing/implementation/reuse/external-backend review. Complete that review
before broad codec/backend integration, then implement and qualify the required coverage of both encoders.

## Knowledge, licences and provenance

- Original FileCat code implements the encoder's one-candidate greedy LZ search, fixed complete canonical Huffman
  tables, bit output, framing and container assembly. No upstream encoder or vendor compression library is called.
- Wire compatibility facts were checked against the existing FileCat BSD-2-Clause rardecode port and upstream
  `nwaples/rardecode` v2.4.5 at `ad22ba91a2ab0d3ff6bca20cd978b33ee78faab1`. Its exact licence SHA-256,
  `A853785796B4BD072BBC63AFD3A510C8918198B4F778351B5A6ED71A3448F1E1`, matches the repository's retained
  licence. Source/notice obligations remain; new FileCat-owned code remains MIT. See the [pinned upstream licence](https://github.com/nwaples/rardecode/blob/ad22ba91a2ab0d3ff6bca20cd978b33ee78faab1/LICENSE).
- The vendor's [public container specification](https://www.rarlab.com/technote.htm) supplies framing, CRC, method,
  version, attribute and time-record facts. Its existence alone is not a licence for restricted source code.
- No UnRAR source, restricted decoder port, vendor binary disassembly or vendor compressor output was used to derive
  the encoder. No RAR/WinRAR/UnRAR executable was needed for this proof. The [vendor EULA](https://www.rarlab.com/license.htm)
  restrictions still prohibit the named restricted derivation route; the optional external fallback remains separately
  subject to lawful user installation/licensing and containment. This review makes no blanket patent-clearance claim.
- Existing BSD notices and authorship records support this permitted-material route. This is not a certified clean-room
  claim or a claim to have proved every historical contributor's independent authorship. A newly discovered provenance
  issue blocks the affected candidate and must be resolved or trigger the authorized fallback decision with evidence.
- Libarchive is an independent **test reader**, not an encoder dependency. Its source was inspected for reader
  behaviour and precision diagnostics. The native proof uses official release 3.8.11, with release asset SHA-256
  checked against the publisher's GitHub digest. No native library was added to FileCat's runtime/package graph.

The machine-readable [source ledger](../../../eng/validation/archive-writing/rar5-independent-probe/reference-sources.json)
records URLs, commits, sizes, SHA-256s, the existing reference files at source base `685fe564`, and the private oracle
asset. No licence label alone authorizes importing an unknown-provenance component. The release implementation must
retain exact source/notice/SBOM closure and exclude reader-only RAR1.5/UnRAR code from the writing-only worker.

## Actual proof and discovered interoperability limit

The private [prototype and readback harness](../../../eng/validation/archive-writing/rar5-independent-probe/README.md)
encode RAR5 algorithm version 0/method 1 with a 128 KiB advertised dictionary, non-solid members, fixed complete code
tables and no filters. All seven nonempty cases use the compressed path, even noise and three-byte input. Empty
input uses method 0. Source bytes and names are generated/owned, not vendor-encoder oracle inputs.

| Fixture | Input bytes | Final archive bytes | Actual output |
|---|---:|---:|---|
| Zeros | 1,048,576 | 8,169 | Compressed method 1 |
| Alphabet | 262,144 | 4,252 | Compressed method 1 |
| Text | 920,000 | 8,845 | Compressed method 1 |
| Noise | 1,048,576 | 1,081,604 | Forced compressed method 1; overhead is retained, not hidden by stored fallback |
| Tiny binary | 3 | 302 | Compressed method 1 |
| Empty | 0 | 69 | Stored method 0 |
| Block-boundary mixture | 73,730 | 67,915 | Compressed method 1 |
| Periodic binary | 1,004,000 | 16,064 | Compressed method 1 |

FileCat's existing RAR reader and independently implemented libarchive 3.8.11 both recover every byte and UTF-8 name.
The native reader also verifies ordinary mode `0644`, mtime seconds `1700000000` and **all** `123456789` nanoseconds.
FileCat's public browsing model displays time at 100 ns resolution; its readback assertion explicitly checks that
resolution, not a false claim of full-nanosecond exposure through that API.

The first precision test against the Mac's built-in libarchive 3.7.4 failed with nanoseconds reported as zero. Its
[published reader](https://github.com/libarchive/libarchive/blob/v3.7.4/libarchive/archive_read_support_format_rar5.c)
sets entry times at whole-second precision and consumes the nanosecond field without applying it. Emitting only
the high-precision record does not fix that older reader limitation. A separate source review also found that newer
readers can overwrite high precision when a redundant basic mtime is emitted; the candidate now uses one authoritative
high-precision record. Exact assertions stayed in place. The privately built current reader passes them. Preserve
these negative fixtures/diagnostics; do not qualify metadata through a reader that discards it.

All live execution occurred on the Mac. This is not Windows/Ubuntu production encoder evidence. The ordinary
unconfined feasibility reader/prototype is not a substitute for the contained write/verify roles. The Mac remained
free of other testers during these runs, and the final census contains no owned test/worker processes.

## Bounded implementation design

The prototype bounds each input to 1 MiB and output to 2 MiB. It processes 32 KiB source blocks, a 65,536-bucket
single-candidate match table and matches no longer than 4,097 bytes. Each loop emits a literal or consumes a positive
match; hash updates cover each consumed position once. The measured comparison counter is checked against five
comparisons per input byte. Fixed table sizes remove attacker-selected tree construction in the initial encoder.

For the production C# port, use compact fixed arrays and bounded source/output blocks, not Python object arrays or
whole-file buffers. The encoded payload bound is `ceil((1800 + 9*n)/8)` per nonempty block plus at most four framing
bytes, with checked aggregate counters and cancellation. The matcher uses at most 32 KiB within the advertised
128 KiB dictionary. Native/source hashes, worker wire limits, CPU caps and the shared 1 GiB worker policy remain
independent controls requiring actual validation. The current Mac trusted-host memory gate is still unqualified.

A first streaming container implementation can count compressed bytes/CRC in a bounded pass and replay the exact
numeric source grant to emit a minimal header and data. This avoids retaining an 8 GiB member or inventing an
unverified padded-varint/backpatch representation. Charge both passes to broker/resource budgets; source changes
must refuse publication. Later optimizations require their own interoperability and race evidence. No filesystem
path or source-deletion authority crosses to the codec. Native publication, origin, recovery and move-after-commit
checks remain shared.

The subsequent [C# codec/creation checkpoint](RAR5_CSHARP_ENCODER.md) implements and independently tests ordinary
stored members/directories and real bounded search presets for methods 1–5 on all three platforms. The table below
describes this earlier Python experiment; it is not the current implementation status. Password support is required
by the later scope amendment and remains work, alongside strict catalogs/worker/workflow and broader coverage gates.

## Compression coverage: explicit gaps, not impossible-by-assertion

The user's coverage instruction requires a full RAR/7z capability inventory. This candidate proves a viable primary
route; it does not implement every version, level, filter or mode. The implementation may not mark the encoder
complete while a required method is only listed as feasible. For every gap, distinguish missing work, the agreed
initial profile, a demonstrated resource/representation limit and an actual licensing/technical blocker.

| RAR item | Current evidence | Required disposition |
|---|---|---|
| RAR5 algorithm version 0, method 0 | Empty stored member verified; prototype can assemble nonempty stored data but that path is not in the eight-case suite | Implement and independently test ordinary/empty stored members and folders |
| RAR5 version 0, method 1 | Genuine compressed binary/text, boundaries and metadata verified through two implementations | Port to bounded C#, add complete source admission/manifest/wire/worker/workflow and qualification |
| RAR5 methods 2–5 / compression presets | Not implemented by this prototype; the method field shares container/compressed-stream grammar | Implement meaningful bounded effort/ratio presets and test them; changing only the header label does not establish distinct compression-level behaviour |
| RAR7 algorithm version 1 | Existing BSD reader material describes its larger distance alphabet; no encoder/independent-oracle proof here | Investigate and qualify separately if needed; do not mislabel a version-0 stream or count libarchive's hardcoded 64-distance table as version-1 coverage |
| Large/fractional RAR7 dictionaries | Not implemented or tested | Select explicit bounds within worker policy; format-advertised maximums are not a promise to allocate beyond the 1 GiB envelope |
| Standard data/executable filters | Encoder emits none; reader availability is not encoder evidence | Audit each needed filter's source and bounded encoding path; document unsupported filters and reject unqualified source features |
| Legacy RAR1.5/2/3/4 LZ/PPMd/VM encoding | Not implemented; outside the agreed initial RAR5 profile | Record read-only status and source/representation constraints. No evidence here establishes impossibility; do not cite the UnRAR restriction as a ban on every independent approach |
| Solid, encrypted, multipart, SFX/recovery/service features | No write proof; explicitly excluded from the initial profile | Preserve the stated scope until separately qualified; distinguish this product-profile decision from technical impossibility |

The [public RAR specification](https://www.rarlab.com/technote.htm) distinguishes container signature, compression
algorithm version, method, dictionary flags and compatibility bits. They are separate capability dimensions. The
7z follow-up must make the equivalent distinction for codecs, filters/coder graphs, solid mode, header compression
and encryption; a format-name checkbox cannot stand for all of them.

## Exact evidence and remaining gates

Frozen `rar5-feasibility-evidence-v1.tar.gz`: **20,698,243 bytes**, **959 verified payload rows**, SHA-256
`80A3648A3B8B2EAE22428A7B36BBD07C22127EE96075BBE143BCA93CC04B6B3A`.
The local copy passed archive identity, path/link safety, exact file count, every size and every SHA-256. It contains
the generated fixtures and source packets, negative precision variants, FileCat readback binaries and compiler-source
audit, the pinned native reader/source release, build configuration/linked-library identity and final census.
PDB checksums match **844 documents / 7,942,303 bytes** across the existing reader assemblies and private probe;
that audit is not a claim that Python encoder logic was covered by PDBs.

The native reader source release is **6,754,236 bytes**, SHA-256
`A3DC0872B1B39DA34DC18EF1B944F0F11FFD37425D70AE04BE679279F1CF43D2`.
The actual privately built dylib SHA-256 is
`D38E9A0E1091C6D991FD747879BFD73EBC7CDEC694246DB71759E951064B224E`;
the compiled RAR5 reader source SHA-256 is
`0A21CC37EF7AE0F83BBFFDAC1B08414380787DDF1CDDAE7B8539985E4B28E3AB`.
Native oracle code and baseline reader-only assemblies do not enter the encoder's production dependency closure.

No coverage-guided campaign floor, full method/preset matrix, hostile strict RAR catalog, complete time-range/mode
profile, shared worker protocol/manifest integration, native publication/update/move/session/UI flow, installed
package/RID matrix or final independent review is completed by this result. Finish the requested 7z analysis next,
then implement and validate the required encoder coverage in larger guarded machine batches. Keep all new RAR
capabilities disabled until those gates pass. The authorized fallback remains available if evidence later blocks the
independent compressed route; elapsed time or an unfinished implementation alone is not such evidence.
