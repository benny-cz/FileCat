# FileCat's build of SharpCompress

Source: SharpCompress 0.50.5, `src/SharpCompress` of commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`
(github.com/adamhathcock/sharpcompress), from the GitHub tarball with SHA-256
`93eda5778f8c31d47dcaec8fd5a22e7292aa98dbeadb8dd598b220abf50169d0`. License: `LICENSE.txt` (MIT,
Copyright (c) Adam Hathcock); individual components keep their own notices. `licenses/sharpcompress` at the
root of FileCat's repository ships that license, the license inventory of every component (`COMPONENTS.md`)
and the notices the components' licenses require.

FileCat's changes, all for release issue I14: only code under OSI-approved licenses may ship, and none
derived from UnRAR. FileCat uses this library for 7z archives and bzip2, xz and zstd streams only, so
everything else is left out.

- Password-development follow-up, 7 November 2026: original internal `SevenZipAesDecryptorFactory`/reader-option
  plumbing lets a FileCat read job own its bounded KDF/cache and mutable password bytes. The existing AES reader
  validates property/padding/length bounds before crypto setup, accepts the legacy direct-key mode with a zero IV
  when absent, and clears its private buffer on disposal. Legacy string-provider temporary key/password arrays
  are cleared; production password/UI/protocol qualification remains unfinished. No new third-party crypto code.

- Archive-writing PPMd/filter follow-up, 7 November 2026: adds bounded symbol-selection hooks to FileCat's
  existing BSD-2-Clause `PpmdModelH` and a managed PPMd7/7z encoding range sink. No excluded UnRAR model is
  restored. Range grammar and ARM64/RISC-V replacement scalar ports use the individually public-domain
  SDK C files pinned in `eng/validation/archive-writing/sevenzip-feasibility/ppmd-filter-sources.json`.
  Adds original Swap2/Swap4 and their reader registrations. Delta now encodes by subtraction and decodes
  by addition; the encoding filter wrapper preserves incomplete instructions across writes, avoids mutating
  caller buffers, and emits the incomplete EOF tail once. BCJ2 encoding was added in the later BCJ2 checkpoint.

- Dedicated archive-worker build, 6 November 2026: `FileCat.SharpCompress.csproj` grants internal access to the
  writing-only `FileCat.Archives.Writing` assembly and adds the Linux musl ARM64 restore target. Existing encoder
  sources, package versions and component licences are unchanged. The separate worker excludes the reader-only
  Rar15 and DiscUtils dependencies; its production registration and packaging remain disabled.

- Archive-writing Zstandard follow-up, 5 November 2026: the unregistered TAR.ZST worker now reaches the existing
  ZstdSharp/Zstandard encoder and decoder with a pre-codec single-frame/window/block guard, fixed bounded options
  and checksummed output. No Zstandard source was added, changed, restored or upgraded. Its BSD-3-Clause/MIT
  provenance and required notices remain those already recorded in `licenses/sharpcompress`.

- Archive-writing follow-up, 5 November 2026: `Compressors/BZip2/CBZip2InputStream.cs` and its `.Async.cs`
  retain their Apache-2.0 provenance/notices and expose an internal strict single-stream terminal observation:
  a checked combined-CRC footer, no tolerated truncation/concatenation, and zero final padding bits. Existing
  tolerant reader behavior is unchanged. The unregistered TAR.BZ2 writer adapter uses the already-present
  Apache-derived encoder, including its existing bounded block-size constructor; no upstream files were restored.

- Removed RAR support: `Archives/Rar`, `Common/Rar`, `Compressors/Rar` (UnpackV1, UnpackV2017, VM),
  `Readers/Rar` and `Factories/RarFactory.cs`. Upstream ports these decoders from UnRAR (its repository
  keeps the UnRAR source under `reference/unrar`).
- Removed `Compressors/PPMd/H` (PPMd variant H after UnRAR's suballocator and model, upstream's `ModelPpm`).
- Added `Compressors/PPMd/PpmdModelH.cs`: FileCat's PPMd variant H model, ported from rardecode v2.4.5
  (`ppm_model.go`, Copyright (c) 2015 Nicholas Waples, BSD-2-Clause, `licenses/rardecode` in FileCat's
  repository) with FileCat's bounds checks. It codes through a small range-decoder interface
  (`IPpmdRangeDecoder`: GetThreshold, Decode, DecodeBit). FileCat's RAR reader decodes RAR 3 PPMd with it
  and RAR's range coder (`src/FileCat.Archives/Rar/Rar3Ppmd.cs`). Its `Damaged` helper is not inlined: the
  hot decoding loops throw it in many places and grew larger and slower when it was.
- Added `Compressors/PPMd/PpmdModelH7z.cs`: 7-Zip's PPMd7z range decoder as the LZMA range decoder of
  `Compressors/LZMA/RangeCoder` (public-domain LZMA SDK) offers it, guarded so that no argument leaves the
  range at zero, and `PpmdModelH7z`, the members of the removed `ModelPpm` that 7z decoding uses, over the
  shared model. It refuses model orders outside 2–64, model memory under 2 KiB and, as a FileCat limit,
  over 1 GiB.
- `Compressors/PPMd`: `PpmdProperties` accepts only 7z's five property bytes and throws
  `NotSupportedException` for any other form; `PpmdVersion` keeps only `H7Z`; `PpmdStream` decodes only
  (compressing throws `NotSupportedException`) and uses `PpmdModelH7z` where it used `ModelPpm`.
- Removed the archive, reader and writer code of every format but 7z: ZIP, TAR, GZip, LZip, ARC, ARJ, ACE
  and LHA, all of `Writers`, the 7z writer in `Common/SevenZip`, and the factories of the removed formats.
  `Factories/Factory.cs` registers 7z only; `Factories/SevenZipFactory.cs` no longer offers a writer;
  `Archives/ArchiveFactory.cs` has no `CreateArchive`; `Archives/ArchiveFactory.Detection.cs` no longer
  looks for TAR inside compressed streams.
- Removed the codecs only those formats use: `Compressors/ADC`, `ArcLzw`, `Arj`, `Deflate64`, `Explode`,
  `Lzw`, `PPMd/I1`, `RLE90`, `Reduce`, `Shrink` and `Squeezed`, the GZip stream
  (`Compressors/Deflate/GZipStream*`), `Crypto/Crc32Stream.cs`, `IO/DataDescriptorStream.cs`, and the
  compression providers of the removed codecs (with their entries in
  `Providers/CompressionProviderRegistry.cs`). `licenses/sharpcompress/COMPONENTS.md` says which of them
  carried terms that are not OSI-approved or not established.
- `Compressors/Deflate/CRC32.cs`: replaced by FileCat's own table-driven CRC-32 with the three members
  `ZlibBaseStream` uses. DotNetZip's class says it is based on example code from vbAccelerator.
- `Common/ChecksumValidationStream.cs`: computes its CRC-32 itself (it used `Crc32Stream`'s table and update).
- `Compressors/Deflate/ZlibBaseStream.cs`: `DateTime.UnixEpoch` in place of the TAR header's epoch (the same
  value). Upstream's gzip-header CRC16 read (through the decompressor, and one of its two bytes) carries a
  CA2022 suppression: FileCat's build never reaches the gzip flavour.
- `Compressors/Xz/XZBlock.cs`: reads all of a block's padding, refusing padding that is cut short; a short
  read left bytes for the next field to take as its own.
- `Common/SevenZip/ArchiveReader.cs` and `ArchiveReader.Async.cs`: an unused `using` line dropped from each. A packed
  header (7-Zip's default encoded header, or additional streams) is no longer decoded into an array of the size the
  archive declares, allocated up front: the buffer grows as data actually decodes, and decoding stops beyond 256 MiB, or
  beyond 1000 times the packed size once past 64 MiB. A 100-byte archive could make the reader allocate 2 GiB. A packed
  header that ends early is reported as a damaged archive (`InvalidFormatException`), not as an I/O error.
- Project file: `FileCat.SharpCompress.csproj` (upstream's `SharpCompress.csproj`, named after the
  assembly so that the SDK lists it once in `FileCat.deps.json`), `FileCat.SharpCompress` assembly,
  `net10.0` only, no strong-name key (the upstream `.snk`/`.pfx` are not copied), FileCat's locked
  restore, and `InternalsVisibleTo` for FileCat.Archives (the shared PPMd model, the 7z member checksum) and
  FileCat.Core.Tests.

FileCat reads RAR archives with its own reader (`src/FileCat.Archives/Rar`), ZIP with its own reader, gzip
with .NET's `System.IO.Compression`, and TAR with its own reader; it checks each 7z member's CRC-32 as it
reads the member (`SevenZipMemberStream` in `src/FileCat.Archives/ArchiveFormats.cs`).

- `Compressors/LZMA/Bcj2DecoderStream.cs`: dispose the iterator and all four packed input streams, including the
  range stream. Nested cleanup also closes the remaining inputs when an earlier disposal throws; encrypted range
  streams must release their decryptor/buffer ownership with the folder.
  Construction failure also disposes all four accepted inputs, preserving the original short-range error
  if an input disposal fails.

- `Compressors/Deflate/ZlibBaseStream.cs`: retain the inflate status separately from the returned byte count in
  synchronous/asynchronous reads. A one-byte read previously treated its byte count as `Z_STREAM_END` and discarded
  compressed input still needed for BCJ2 branches (and other callers that read one byte at a time).

- `Compressors/LZMA/DecoderStream.cs`: synchronous and asynchronous graph construction dispose already-created
  decoder/packed streams if a later coder or graph construction step throws. This closes earlier AES transforms
  even when the caller never receives a root decoder to own; cleanup preserves the original exception.

- `Compressors/LZMA/LzmaStream.Strict.cs` and selected LZMA/range/window hooks: an internal, opt-in exact-extent
  verifier checks real required input, the initial range byte, terminal range state, complete compressed-byte
  consumption, legal marked/markerless LZMA finish and every LZMA2 chunk/reset/property/end rule. Short repeats
  cannot read missing dictionary history. Strict dictionary storage is cleared before pool return. Existing
  browsing entrypoints keep their default behavior. The new adapter and verification control flow are FileCat
  code; contracts and the separate native test oracle use individually pinned public-domain SDK sources listed
  in `eng/validation/archive-writing/sevenzip-feasibility/lzma-end-sdk-sources.json`. No SDK decoder source is
  imported into the production assembly, and no package or codec licence is changed.
