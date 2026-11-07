# 7z writing: licence, method coverage and integration decision

**Scope amendment, 7 November 2026:** the user subsequently requires password support for both formats, including
payload/header encryption and password-aware reading/verification/workflows. Earlier unencrypted-only statements
below describe the reviewed starting point and are superseded by plan §4.3.1. Encryption/KDF/source closure and
runtime qualification remain required work; no earlier codec/source result proves password support.


**Decision, 7 November 2026:** use a bounded FileCat 7z writing/verification layer with audited permissive encoders
and needed filters. Reuse the existing permitted codecs and suitable pinned MIT serialization primitives; extend or
replace their narrow writer orchestration. Add missing needed encoders from individually reviewed public-domain SDK
material or original code. Do not import the full 7-Zip/SharpCompress distributions. An external executable is not
needed to establish a viable primary route and is not selected as a required runtime component.

This is the user's requested follow-up to the [RAR feasibility record](RAR5_FEASIBILITY.md). It is a source-backed
implementation decision, **not a working 7z writer or runtime qualification**. The RAR prototype and existing 7z
decoders do not prove 7z encoding. Both encoders must implement the needed feasible coverage and document real
limits before being considered complete. The overall plan/goal remains open.

## Actual inspected source and licence boundaries

- SharpCompress remains pinned at `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626` (the existing 0.50.5 fork).
  **29** exact writer/common-header/interface/licence candidates, **154,627 bytes**, were downloaded outside the
  production tree. Each SHA-256 and Git blob identity was checked against the pinned Git tree. Inventory SHA-256:
  `E81ECAB1F836E1DB92F81B53E8FE0A5CC4290C79CB8E229BE3A5C2C93331E23A`.
- The physical writer-support files include `Common/SevenZip/ArchiveWriter.cs`, `SevenZipFilesInfo.cs`,
  `SevenZipHeaderStructures.cs`, `SevenZipStreamsCompressor.cs` and `SevenZipWriteExtensions.cs`. The previous
  type-only restoration list did not establish those filenames or their dependencies. That retrieval gap is now
  resolved for this candidate set; complete per-file origin/compile/reachability review still precedes import.
- The pinned root MIT licence matches `licenses/sharpcompress/LICENSE.txt` exactly: SHA-256
  `B7CA2B6174CEE11AFE41D78B48527E3D7659A4435C25019A4A5E072299A2F8ED`. The source checkout's CRLF copy has a
  different byte hash but identical LF-normalized text. This is not a licence-content discrepancy. Individual
  codec terms remain those in the existing [component inventory](../../../licenses/sharpcompress/COMPONENTS.md):
  public-domain LZMA material, Apache-2.0 bzip2, MS-PL/BSD/Zlib deflate and BSD-3-Clause/MIT Zstandard, among others.
- Public-domain SDK candidates are pinned to 26.05 at `b88c6bc09339de32f7d18bbab900686b244f42ba`.
  **21** exact PPMd7/filter/type/header files, **190,419 bytes**, have verified Git blob/SHA-256 identities and
  explicit public-domain notices. Their quoted local header includes resolve within the audited set. Inventory
  SHA-256: `9DCD892F1B41AF1FD1A9FA4FA229B5A566DAE42121BBF8CBEAE179CBBE8F4B6D`.
  This is header-source inclusion closure, not a compiled object/link/ABI/resource qualification.
- `Ppmd7Enc.c`, `Ppmd7.c`, `Ppmd7.h` and `Ppmd.h` identify Igor Pavlov's public-domain work based on Dmitry Shkarin's
  public-domain PPMdH. The header distinguishes the **7z range coder** from original PPMdH. This supplies a specific
  permitted encoder candidate; it is not permission to restore the excluded UnRAR-derived SharpCompress model.
- SDK candidates also include `Bcj2Enc.c`, branch converters, delta and swap code. No UnRAR/RAR source path was
  fetched. No candidate source has been copied into FileCat's runtime tree or added as a production dependency.

The [exact source ledger](../../../eng/validation/archive-writing/sevenzip-feasibility/reference-sources.json)
contains all URLs, file hashes, Git blob IDs and inspected existing-file identities. The [SDK publisher](https://www.7-zip.org/sdk.html)
provides a public-domain route; the [full 7-Zip distribution licence](https://www.7-zip.org/license.txt) instead
includes LGPL and additional component/UnRAR terms. They are separate source/distribution choices. Retain notices
and audit actual files/dependencies; do not relicense third-party source as MIT or assert patent clearance.

## What the pinned writer actually provides

`SevenZipWriterOptions.CompressionType` accepts only LZMA/LZMA2 and throws for other types. Its `CompressionLevel`
property is explicitly unused/reserved; the compressor receives `LzmaProperties`, not that level. A UI slider wired
only to the unused property would be misleading. Real bounded presets must drive actual codec parameters and be
validated for effect, resources and interoperability.

`SevenZipStreamsCompressor` creates a single input/output coder folder per member and invokes the existing LZMA
or LZMA2 encoder. Raw and LZMA-compressed headers are available. It depends on excluded `Crypto/Crc32Stream`;
adapt CRC handling to FileCat's audited implementation rather than restoring unknown-provenance CRC material.
Its metadata entry model carries name, mtime, nullable attributes and empty/file/directory state. The public write
path does not expose all metadata that a complete update profile may need; creation/access times and faithful
attribute handling require extension or explicit source-profile refusal.

Header writers collect entries and use memory-backed header streams/arrays. They need pre-allocation aggregate
limits and retained/transient-memory accounting. LZMA2 additionally buffers chunks and trial-decodes compressed data
to determine consumed size. Those paths need independent finalization/truncation/resource evidence; library presence
or a self-decode is insufficient. No such runtime qualification is claimed here.

## Required codec coverage and current gaps

The target covers the official core 7z compression set and the additional codecs already present in FileCat's
shipped 7z reader. This prevents an LZMA-only API from being presented as complete needed encoding support.
Defaults may be smaller than supported options; every advertised option needs actual method/property evidence.

| Codec | Current encoder basis | Implementation/qualification required |
|---|---|---|
| Copy/store | Direct bounded stream copy | Add folder/CRC/size serialization and exact empty/ordinary member tests; the candidate writer's options currently reject Copy |
| LZMA | Existing public-domain-derived managed encoder | Bound properties/allocations and real presets; add 7z container integration and independent exact readback |
| LZMA2 | Existing managed encoder/framing implementation | Qualify chunk boundaries, resets, incompressible fallback and terminal consumption independently; enforce pre-codec limits |
| PPMd7 / PPMdH with 7z range coder | Current FileCat stream explicitly refuses compression; audited public-domain SDK encoder candidate exists | Implement a bounded managed port or qualified contained native codec; match the 7z variant/properties, memory/order and finalization; do not restore restricted H code or substitute ZIP PPMd I |
| BZip2 | Existing Apache-2.0 encoder | Add 7z method mapping, properties/finalization and full independent readback; retain notices and resource bounds |
| Deflate | Existing permitted raw-deflate encoder or framework implementation | Use actual raw-deflate framing and the correct 7z method; test finalization and presets, not zlib/gzip mislabeled as raw deflate |
| Zstandard 7z method already decoded by FileCat | Existing BSD-3-Clause/MIT encoder | Qualify the precise method ID/properties and native-reader compatibility, frame/window bounds and full terminal verification; identify extension compatibility in UI/docs |
| Deflate64 | No encoder or decoder in the current trimmed 7z registry; not in the publisher's listed core set | Explicit unsupported gap, not proved impossible. Needs a separately permitted encoder/reader and complete method qualification if required; ordinary deflate cannot simply be relabeled |
| Other third-party/plugin methods | No selected source/profile/reader closure | Not advertised. Record requested interoperability needs and investigate jointly; absence from this plan is not a universal feasibility finding |

The publisher's [7z format page](https://www.7-zip.org/7z.html) distinguishes compression methods, filters, solid
compression, compressed headers and encryption. Decoder registrations in this checkout independently establish
FileCat's wider read set. Neither source establishes a working encoder for an item merely by naming it.

## Filter/coder-graph coverage

| Filter family | Inspected state | Needed work / honest limit |
|---|---|---|
| x86 BCJ, ARM, ARM-Thumb, ARM64, PPC, IA64, SPARC, RISC-V | Existing converter code and/or pinned public-domain SDK candidates | Validate encoding direction, instruction/alignment/tail boundaries, wraparound and exact native inverse conversion; constructor flags are not runtime proof |
| Delta, distances 1–256 | Existing stream's transform always adds history, including when constructed for encoding | Implement/verify the actual subtractive encoding direction and property mapping; do not treat the boolean constructor as a functioning encoder |
| BCJ2 | Existing decode paths; public-domain `Bcj2Enc.c` available | Implement four-stream encoding, graph bindings, CRC/unpacked sizes and bounded scratch/stream ownership; a single-coder folder is insufficient |
| Swap2/Swap4 | Pinned public-domain encoder candidate; not registered in current FileCat decoder | Add bounded conversion and reader/manifest support with partial-tail cases before advertising it |

These are feasible implementation candidates, not completed capabilities. The exact required filter graph must be
part of the admitted profile, including cumulative memory and scratch-space bounds. Standard filters listed by the
publisher and those already decoded by FileCat must not disappear behind a blanket “7z supported” claim. A source
archive with an unqualified graph stays visibly read-only; no dropped transform or implicit metadata conversion.

## Modes, metadata and genuine constraints

- **Raw/compressed headers:** both are relevant. Bound raw and encoded sizes, properties, nested/cyclic graphs and
  all intermediate allocations before codec setup. Compressed-header output needs independent header/payload readback.
- **Non-solid/solid:** the agreed initial update profile is non-solid. Solid is technically possible, not demonstrated
  impossible; it needs separate cross-member stream/selection/race/resource/preservation qualification. State its
  current exclusion as profile policy. Do not infer encoder support from the existing solid reader.
- **Encryption/header encryption:** technically possible with permitted cryptography; not provided by this pinned
  writer API or the agreed initial profile. A future route needs key/KDF bounds, secret handling, UI/journal/session
  policy and actual containment evidence. It is not a compression codec and must not be disguised as a licensing ban.
- **Multipart/SFX/custom methods:** excluded by current profile policy and unqualified multi-object/executable
  semantics, not proved impossible. Existing archives retain read/extract behavior and a concrete write-refusal reason.
- **Time precision:** standard 7z FILETIME fields resolve to 100 ns. An arbitrary 1 ns source time is not exactly
  representable there. Record that format limit, with explicit declared new-input precision policy or typed refusal;
  never silently claim full-nanosecond preservation. Existing representable archive values must remain exact.
- **Dictionary/model maximums:** format/vendor maxima can exceed the 1 GiB worker and still-open Mac aggregate-host
  envelope. Advertise only measured bounded settings. Refusal of an over-budget parameter is distinct from an
  unsupported codec or a legal impossibility.

## Own implementation, reuse and external fallback

The primary route is FileCat-owned bounded container/planning/verification code over the reviewed permissive
encoders, with narrowly reused MIT serialization primitives where suitable and missing codecs/filters added from
permitted source. The proprietary/restricted-source obstacle for RAR does not apply to all independent 7z writing.
Writing every existing compression algorithm again is not needed to keep FileCat-owned code MIT; applicable third-party
terms/notices still remain. Both a managed port and a contained native public-domain subset require actual safety,
licence/SBOM and six-RID artifact qualification before selection is considered complete.

A user-installed `7zz`/`7z` can be investigated if a needed route cannot be provided directly. It is not selected now:
it would require the same private immutable snapshots, executable/version identity, argument/list-file/configuration
controls, sandbox/resource/process-tree policy and fresh independent verification as the external RAR candidate.
It must never update the original pathname or become an unrestricted fallback. Full-distribution LGPL/component
obligations do not vanish at a subprocess boundary. A reduced public-domain SDK tool is a separate candidate with
its own exact closure/coverage; neither a vendor executable nor a source label establishes all methods or platforms.

## Execution and remaining evidence

This review inspected exact sources and notices locally; it did not run a 7z writer on the host or claim new live
encoder results. No source was imported into the production fork. The source-backed primary route is feasible, while
the codec/filter/preset and profile gaps above remain required implementation work. No investigated core codec has
been found legally or technically impossible under the permitted-source policy.

The next cohesive implementation/qualification candidate should cover the bounded RAR C# encoder and the needed
7z codec/container/filter paths, with exact per-method fixtures and missing coverage kept visible. Prepare source,
dependencies and runners once, then use large guarded Windows/Mac/Ubuntu sessions for builds, broad regressions,
independent method/metadata readback, relevant worker/native/resource cases, immutable evidence and owned-process
cleanup. Actual failures justify adaptive fixes and retests. Do not enable methods based on this analysis, omit a
needed method merely because the pinned writer lacks it, or call the overall feature/review complete.


The subsequent [C# codec/container/KDF checkpoint](SEVENZIP_CSHARP_CODECS_AND_KDF.md) implements and independently
checks six existing-codec paths on the three platforms. This earlier analysis remains the needed-coverage inventory;
PPMd/filter/header/password/workflow gaps must not disappear behind that narrower tested foundation.
