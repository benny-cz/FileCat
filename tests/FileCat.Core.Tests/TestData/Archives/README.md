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

`Rar2.multi.rar` and `Rar2.multi.r00` through `.r05` are unchanged legacy-named RAR fixtures
from SharpCompress tag 0.50.4, commit `c083c6efd843a844b0c8f7878787360e815be781` (same MIT license above).
All seven downloaded files match the pinned upstream Git blobs. Independent 7-Zip 24.01
extraction from the primary `.rar` verifies all three member hashes used by the regression.
Its secondary-volume extraction can start without the primary and fail; it is not an oracle
for FileCat’s secondary-volume discovery policy. The complete primary extraction supplies the bytes.

`PureUdfRevisions.zip` contains six FileCat-owned 32 MiB pure UDF images, compressed for storage.
macOS’s native `newfs_udf` formats revisions 1.02, 1.50, 2.00, 2.01, 2.50 and 2.60; the native
filesystem writes known empty, ASCII, Unicode, long-name and binary files, plus an empty directory.
`PureUdfRevisions.json` pins every final image and every member, including macOS-created
`.fseventsd` files. A fresh read-only native inventory and independent 7-Zip 24.01 extraction
match complete names/directories and exact bytes. The initial writable-census timing failure is
retained in private release evidence. Images are detached, immutable and have no ISO 9660 descriptor.
ZIP SHA-256: `446727ee330fb8b80f9375691c79516339c7ae4e091dcc723fc90744ea4419eb`.
