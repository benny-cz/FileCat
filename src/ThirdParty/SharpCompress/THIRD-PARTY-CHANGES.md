# FileCat's build of SharpCompress

Source: SharpCompress 0.50.5, `src/SharpCompress` of commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`
(github.com/adamhathcock/sharpcompress), from the GitHub tarball with SHA-256
`93eda5778f8c31d47dcaec8fd5a22e7292aa98dbeadb8dd598b220abf50169d0`. License: `LICENSE.txt` (MIT,
Copyright (c) Adam Hathcock); individual components keep their own notices. `licenses/sharpcompress` at the
root of FileCat's repository ships that license, the license inventory of every component (`COMPONENTS.md`)
and the notices the components' licenses require.

FileCat's changes, all for release issue I14 (no UnRAR-derived code may ship):

- Removed RAR support: `Archives/Rar`, `Common/Rar`, `Compressors/Rar` (UnpackV1, UnpackV2017, VM),
  `Readers/Rar` and `Factories/RarFactory.cs`, with the factory registration and three unused
  `using` lines in the ARJ code. Upstream ports these decoders from UnRAR (its repository keeps the
  UnRAR source under `reference/unrar`).
- Removed `Compressors/PPMd/H` (PPMd variant H after UnRAR's suballocator and model, upstream's `ModelPpm`).
- Added `Compressors/PPMd/PpmdModelH.cs`: FileCat's PPMd variant H model, ported from rardecode v2.4.5
  (`ppm_model.go`, Copyright (c) 2015 Nicholas Waples, BSD-2-Clause, `licenses/rardecode` in FileCat's
  repository) with FileCat's bounds checks. It codes through a small range-decoder interface
  (`IPpmdRangeDecoder`: GetThreshold, Decode, DecodeBit). FileCat's RAR reader decodes RAR 3 PPMd with it
  and RAR's range coder (`src/FileCat.Archives/Rar/Rar3Ppmd.cs`).
- Added `Compressors/PPMd/PpmdModelH7z.cs`: 7-Zip's PPMd7z range decoder as the LZMA range decoder of
  `Compressors/LZMA/RangeCoder` (public-domain LZMA SDK) offers it, guarded so that no argument leaves the
  range at zero, and `PpmdModelH7z`, which gives `PpmdStream` the members of the removed `ModelPpm` over the
  shared model. The 7z path (`PpmdVersion.H7Z`) decodes with it. It refuses model orders outside 2–64,
  model memory under 2 KiB and, as a FileCat limit, over 1 GiB. Variant H with RAR's range coder over a
  plain stream (`PpmdVersion.H`, which no `PpmdProperties` produces) throws `NotSupportedException`.
- `Compressors/PPMd/PpmdStream.cs`: uses `PpmdModelH7z` where it used `ModelPpm` (the field type, four
  constructions and one `using` line); nothing else changed.
- Project file: `FileCat.SharpCompress` assembly, `net10.0` only, no strong-name key (the upstream
  `.snk`/`.pfx` are not copied), FileCat's locked restore, and `InternalsVisibleTo` for FileCat.Archives
  (the shared PPMd model) and FileCat.Core.Tests.

FileCat reads RAR archives with its own reader (`src/FileCat.Archives/Rar`).
