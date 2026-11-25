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

## Fix

**Cause.** I616's walk already ran on macOS: `SEEK_DATA`/`SEEK_HOLE` reported the source's two 4 KiB extents, and FileCat wrote only those and set the length. APFS then filled the gaps itself. On the test Mac (macOS 27.2.1, APFS), a replay of FileCat's exact `FileStream` steps showed the destination at 8 KiB until its descriptor was closed, and at 32 MiB after.

Probes of plain system calls showed when this happens:
- **When.** APFS gives a file written with gaps zero blocks for the smaller gaps once its data is flushed, at close or `fsync`. Gaps of 8 to 16 MiB were filled; gaps of 32 MiB and more stayed holes.
- **What escapes it.** A file that is only extended keeps its hole (the all-hole case), and punched holes survive close.
- **`copyfile(3)`.** Its sparse copy, read from Apple's source, writes the data and then punches each hole with `F_PUNCHHOLE`. A probe replaying that order showed that the first punch flushes the file, so the remaining gaps were allocated for a moment: 16 to 24 MiB in a 32 MiB file.

**Change.** On macOS, `UnixSparseCopy` (`src/FileCat.Core/FileSystem/UnixSparseCopy.cs`) punches each gap it skips as soon as the next extent's first bytes are written, and punches the trailing hole after the final length is set. Punching this way kept the peak allocation at the data's size.

What is punched:
- **Whole blocks only.** It punches only blocks between copied extents, aligned to the destination's `_PC_MIN_HOLE_SIZE` (4 KiB on APFS). A block holding copied bytes is never punched, so the copy's bytes are identical whether or not a punch succeeds.
- **Best effort.** If the destination reports no holes or refuses one, punching stops and those zeros stay allocated, as in every copy before.

What does not change:
- The extent walk, its checks for changed extents and size, progress, cancellation and cleanup.
- The job's size and read-back checks.
- Linux and Windows copies.

`fcntl` is variadic, so on Apple arm64 its argument is passed after eight register slots, as `UnixArchiveNative` already does.

**Tests.**
- `SparseCopyTests.Copy_keeps_the_holes_APFS_would_fill_when_the_copy_is_closed` (Linux and macOS) covers three layouts:
  - b26's layout: 32 MiB, with 4 KiB at 0 and at 16 MiB, and a trailing hole.
  - A leading hole, with data ending inside an unaligned last block (16 MiB + 100 bytes).
  - Eight 4 KiB extents 2 MiB apart.

  It requires the same length and SHA-256, full progress, and allocation under a quarter of the length. On macOS it also requires no more than the source's allocation plus 64 KiB. The source must stay unchanged.
- **The I616 test.** `Copy_keeps_data_and_trailing_holes_without_allocating_them` now punches its source's gaps on macOS, as the harness does. Its two data variants therefore run there instead of being skipped because the source was not sparse.

None of these tests needs a fixture, so all of them run in the hosted macOS Core lane.

**Results.** On the physical M1 Mac (macOS 27.2.1, APFS start-up volume, .NET SDK 10.0.101, Release):
- **Before:** the rc.2 product source (`ec126ea94`) with these tests failed all five data cases. For b26's layout, the copy allocated 33,554,432 bytes for a source with 8,192 allocated, the result this record found. The other layouts allocated 16,781,312 and 16,777,216 bytes. The two all-hole cases passed.
- **After:** all seven passed. The focused Core run had 40 passed, 19 skipped and 0 failed; the skips are Windows- and fixture-only cases.
- **Destinations without holes.** FileCat's `CopyFile`, built from this source, copied b26's source into an attached HFS+ disk image, which keeps no holes. Every byte matched and progress reached the full length, with 33,554,432 bytes allocated, as before. The same copy to APFS had 8,192 bytes allocated. The image was detached and removed.

Hosted CI on the pull request is still to run.

**Not verified here.** The Mac GUI harness could not run a build of this source, because the console was locked (see I650). b26 was not rerun through F5.

**What the next installed candidate must check.** Run b26 on the Mac's APFS volume:
- `sparse.bin` copied with F5 must keep the same length and SHA-256, with allocation under a quarter of its length (8 KiB expected, as `copyfile` gives).
- `all-hole.bin` must stay at 0 bytes allocated.
- Each copy must be reported Completed, with only the exact targets present.

On NTFS and ext4, the I616 acceptance is unchanged.
