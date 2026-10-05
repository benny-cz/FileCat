# Archive fixtures

These sample archives come unchanged from the test suite of SharpCompress
(https://github.com/adamhathcock/sharpcompress, `tests/TestArchives/Archives`), which is licensed under the MIT
license, Copyright (c) Adam Hathcock. They exercise FileCat's read-only archive formats (ADR-07, P8): RAR 4 and 5
(solid, encrypted, multi-volume), 7z (LZMA2, solid, encrypted members), and compressed TAR (xz, zstd). FileCat creates
its other fixtures (TAR, gzip, bzip2, ISO) in the tests themselves.

`EmptyEntries*.7z` are FileCat-owned I134 regression fixtures generated with 7-Zip 24.01. They hold
`a-empty.txt` and `folder/b-empty.txt` (zero bytes), `payload.txt` (`alpha`) and `other.bin` (`beta beta`).
Solid/non-solid variants use `-ms=on`/`-ms=off`; encrypted variants use the disposable test password
`owned-test-password`, with `-mhe=off`/`-mhe=on`. Independent technical listings and extraction verify
every input byte. They test empty-stream metadata, readable empty files and retained encryption refusal.
