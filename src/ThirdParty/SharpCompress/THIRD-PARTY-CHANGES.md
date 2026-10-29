# FileCat's build of SharpCompress

Source: SharpCompress 0.50.5, `src/SharpCompress` of commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`
(github.com/adamhathcock/sharpcompress), from the GitHub tarball with SHA-256
`93eda5778f8c31d47dcaec8fd5a22e7292aa98dbeadb8dd598b220abf50169d0`. License: `LICENSE.txt` (MIT,
Copyright (c) Adam Hathcock); individual components keep their own notices (for example BZip2's
Apache License 2.0 header).

FileCat's changes, all for release issue I14 (no UnRAR-derived code may ship):

- Removed RAR support: `Archives/Rar`, `Common/Rar`, `Compressors/Rar` (UnpackV1, UnpackV2017, VM),
  `Readers/Rar` and `Factories/RarFactory.cs`, with the factory registration and three unused
  `using` lines in the ARJ code. Upstream ports these decoders from UnRAR (its repository keeps the
  UnRAR source under `reference/unrar`).
- Removed `Compressors/PPMd/H` (PPMd variant H after UnRAR's suballocator and model). A placeholder
  `Compressors/PPMd/H/ModelPpm.cs` refuses variant H until FileCat's own PPMd7 model (ported from
  public-domain and BSD sources) is wired in its place.
- Project file: `FileCat.SharpCompress` assembly, `net10.0` only, no strong-name key (the upstream
  `.snk`/`.pfx` are not copied), FileCat's locked restore.

FileCat reads RAR archives with its own reader (`src/FileCat.Archives/Rar`).
