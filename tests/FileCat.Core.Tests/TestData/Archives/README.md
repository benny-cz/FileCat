# Archive fixtures

These sample archives come unchanged from the test suite of SharpCompress
(https://github.com/adamhathcock/sharpcompress, `tests/TestArchives/Archives`), which is licensed under the MIT
license, Copyright (c) Adam Hathcock. They exercise FileCat's read-only archive formats (ADR-07, P8): RAR 4 and 5
(solid, encrypted, multi-volume), 7z (LZMA2, solid, encrypted members), and compressed TAR (xz, zstd). FileCat creates
its other fixtures (TAR, gzip, bzip2, ISO) in the tests themselves.
