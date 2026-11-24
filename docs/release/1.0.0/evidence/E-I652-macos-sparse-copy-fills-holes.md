# I652 — macOS: Copy fills a sparse file's holes on APFS

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b26, I616). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b26_sparse_copy.txt`). **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Candidate and machine

`FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed0c002f92c624719f567bd9768eae3fe99f904dbb776feffc771acc41`, on the physical M1 Mac, macOS 27.2.1, on the APFS start-up volume.

## Observation

| Source | Logical size | Allocated | System copier (`copyfile` with `COPYFILE_DATA_SPARSE`) | FileCat's F5 copy |
|---|---|---|---|---|
| `sparse.bin`: two 4 KiB data regions, the rest punched holes | 33,554,432 bytes | 8,192 bytes | 8,192 bytes allocated | **33,554,432 bytes allocated** |
| `all-hole.bin` | 33,554,432 bytes | 0 bytes | 0 bytes allocated | 0 bytes allocated |

Each copy completed with the same length and SHA-256 as its source, and both sources were left unchanged. The review read "Copy "sparse.bin" (32,0 MB)", and the job read "done (32,0 MB)". The result was checked again once.

## Why it matters

I616 (Medium) made Copy keep holes on Windows (NTFS) and Ubuntu. Its record says that macOS hole preservation was unqualified, because the Mac test file then was fully allocated. With a genuinely sparse source, FileCat's copy on macOS allocates every hole. On a large sparse file, such as a disk image or a VM disk, that can fill the target volume. The system's own copier keeps the file sparse.

## Remedy (proposed) and its cost

On macOS, copy with the system's sparse-aware path, `copyfile` with `COPYFILE_DATA_SPARSE`. Alternatively, apply the same data/hole walk that I616 uses elsewhere (`SEEK_DATA`/`SEEK_HOLE`, which APFS supports). Keep the byte checks.

This changes product source, so it reaches users only in a candidate after rc.2.
