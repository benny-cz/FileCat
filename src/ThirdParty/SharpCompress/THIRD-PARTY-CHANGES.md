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
