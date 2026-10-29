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

`InPlaceAlpha.7z` and `InPlaceOmega.7z` are FileCat-owned same-size rewrite controls,
generated with 7-Zip 24.01 (x64) using `a -t7z -mx=0 -mtc=off -mta=off -mtm=off`.
Each holds one 4,096-byte file, `alpha.txt` (all zero bytes) or `omega.txt`
(all `0xA7` bytes). Both archives are 4,206 bytes. Their SHA-256 digests are
`7931db9acac5fd0124f8189684fa71e10cf20b767caee3c29e282d314b88e419` and
`9ba572ab801806f6cf4151fb358c24f7a7f10cfedfa3229d5115cb1cc76efc48`, respectively.

The other `Rar*` fixtures (RAR 1.5 through 5, stored, solid, multi-volume under both naming
schemes, comments, BLAKE2sp hashes, encrypted members and headers, and the damaged
`Rar.malformed_512byte.rar` and `Rar.issue1050.rar`) come unchanged from SharpCompress 0.50.5,
commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626` (same MIT license above). Each was fetched by
its Git blob ID and rehashed with `git hash-object` to that ID. They are the differential corpus of
FileCat's own RAR reader (`RarReaderTests`) against the retired SharpCompress RAR path.

`libarchive/` holds libarchive's RAR test corpus, unchanged in content: every
`libarchive/test/test_read_format_rar*.rar.uu` of libarchive v3.8.1 (tag object `7118f97c26bf0b2f426728b482f86508efc81d02`),
fetched by Git blob ID, rehashed to that ID and uudecoded (BSD 2-Clause, Copyright (c) 2003-2007 Tim Kientzle,
2011 Andres Mejia, 2018 Grzegorz Antoniak and libarchive contributors). It adds real RAR-made archives that the
SharpCompress set lacks (RAR 3 PPMd/LZSS switching, the RAR 3 filters, compression levels, RAR 5 ARM filter,
multi-volume sets) and the damaged files that once crashed real decoders (`RarLibarchiveCorpusTests`).
