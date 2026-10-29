# E-I557 — what the archive readers decode to pass members; 7z members kept aligned; packed 7z headers (R-H5)

**Engineering for I557, I558, I559 and review finding R-H5, on `main` at `3a63099e`, `98bb2ce1` and `2872ebbd` (2026-10-29). Preliminary component evidence; not hosted CI of these commits, candidate qualification or release GO.**

## How they were found

Codex's audit of FileCat's RAR reader raised RAR-SEC-001 on 2026-10-29. To open a later member of a solid RAR, the reader decoded the earlier members without the expansion limit; Codex's `a718c777` corrects that. The release session checked the other readers that must decode members nobody asked for, in `src/FileCat.Archives/ArchiveFormats.cs`, and measured each. It found two defects there (I557, I558), the third (R-H5) in the SharpCompress build, and one in the RAR correction itself (I559).

## I557 — passing members was not bounded (High, availability)

**7z.** To open a member, FileCat moves SharpCompress's reader forward, and SharpCompress decodes every entry it moves past with no limit. FileCat's 1000:1 limit applies only to the member it opens. The reader also decodes entries in other folders, which need no decoding at all.

**Compressed TAR.** Listing checked the 1000:1 limit between members only. .NET's `TarReader` skips a member's data inside `GetNextEntry`, so one member was decompressed whole before the check ran, however large its header said it was. Opening a member skipped the earlier ones with no check at all. Cancellation was not observed inside a skip.

Measured on this host with the code before the fix (`e924db62`):

| Archive | Bytes | Inside | Result |
|---|---:|---|---|
| 7z, 2 GiB of zeros and a 6-byte member (7-Zip put them in two folders) | 458,954 | 4,679:1 | Opening the 6-byte member decoded the 2 GiB first, in 4.4 s |
| tar.xz, the same two members | 322,300 | 6,663:1 | Listing decoded the whole 2 GiB member (8.3 s), then warned |
| tar.bz2, the same two members | 1,710 | about 1,256,000:1 | Listing decoded the whole 2 GiB member (7.7 s), then warned |

The time grows linearly with the archive, and with bzip2 inside a TAR or a 7z, each kilobyte of archive is over a gigabyte of zeros. A 1 MB `.tar.bz2` therefore costs more than an hour of CPU to list, and in a 7z the same costs come when a member is opened.

**Fix (`3a63099e`).**
- **7z.** The reader opens each entry it passes itself. An entry in the member's own folder is read here, under the archive's expansion limit, and can be canceled: past 64 MiB, passing may not decode more than 1000 times the archive's size. An entry in another folder is left unread, because SharpCompress starts each folder's stream afresh. Opening a member in a folder of its own therefore decodes nothing before it: the 2 GiB example opens at once.
- **Compressed TAR.** The stream between the decompressor and `TarReader` checks the limit on every read (1000:1 against what was read of the file, after 1 GiB, as listing already did), and observes cancellation. A refusal ends the listing with the bomb warning. A cursor stopped inside a member starts over on the next open.

## I558 — a 7z member read only in part shifted the next member of its folder (High, correctness)

SharpCompress does not finish an entry FileCat opened but did not read to its end, so the next entry of the same folder starts inside it. Through the provider this happens when F3 or Quick view reads the first pages of a large member of a solid 7z and the user moves to the next member: opening another member takes the shared cursor from the first. With FileCat's member CRC-32 check (`34bc2abb`), the next member is reported as damaged. Before that check, it read back as the wrong bytes with no error.

Observed before the fix: 64 KiB read from a 40 MiB member, then the next member of the solid folder, gave "The member's CRC-32 is 0x5FDAED69, not the 0xF33D2407 its archive header records: the archive is damaged." After the fix, that member reads exactly.

**Fix (`3a63099e`).** Before moving on, the reader finishes the entry it handed out last if the next member is in the same folder, under the same limit.

## R-H5 — a packed 7z header was allocated at the size it claims (High, availability)

The [previous review](E-REVIEW-2026-10-29-codex-commits.md) found this in SharpCompress 0.50.5. It was assigned to the security plan (FCSEC-007, AC-09), whose raw-header limit does not cover it. `f134ba37` did not change it ([recheck](E-REVIEW-2026-10-29-f134ba37.md)). The code is FileCat's own SharpCompress build, which the release session maintains, so the release session fixed it.

**Fix (`98bb2ce1`).** `ArchiveReader.ReadAndDecodePackedStreams`, sync and async, used to allocate the decoded size the archive declares before decoding a byte. That method serves 7-Zip's default encoded header and additional streams. Now the buffer grows only as data actually decodes, and decoding stops beyond 256 MiB, or beyond 1000 times the packed size once past 64 MiB. A packed header that ends early is reported as damage, not an I/O error. `THIRD-PARTY-CHANGES.md` records the change.

Observed: a 70-byte archive whose Copy-coded header claims 200 MiB made the old reader allocate 209,923,888 bytes on the test thread. The fixed reader allocates 1,257,296 bytes and reports "The archive's packed header ends early." A claim over 256 MiB is refused before decoding.

## I559 — the RAR correction capped a solid run at 8 GiB (High, correctness)

Codex's `a718c777` corrects RAR-SEC-001 in `RarReader.OpenDecoded`. Each earlier member of a solid run is drained under its own limits (its declared size, and 1000:1 against its packed size past 64 MiB), and the run under an aggregate one. The release session reviewed it, since the RAR reader is its code. The per-member limits and the aggregate ratio are sound, and Codex's bomb tests keep passing. But the aggregate was `MemberLimits.Of(-1, packed, MaxSpooledMember, …)`, so with no declared size it capped the run at 8 GiB, the limit for a member of unknown size. It also counted bytes the reader had already returned. A solid RAR with a member over 8 GiB could therefore not be read past that member, not even in archive order.

Observed on this host with a solid RAR 5 that WinRAR 7.01 made (`a -s -ds -m1`): an 8.5 GiB member of random bytes (9,126,805,504), then a 13-byte one.

| Opening the 13-byte member | At `a718c777` | With the fix (`2872ebbd`) |
|---|---|---|
| After reading the 8.5 GiB member whole (88–94 s) | Refused at once: "The member is larger than FileCat extracts in one piece." | 13 bytes, at once |
| Directly | Refused after 78 s of decoding | 13 bytes, after 99 s of decoding |

**Fix (`2872ebbd`).** The run is bounded by its expansion ratio alone: `long.MaxValue` in place of the 8 GiB cap. Each member is still held to its own declared size, so the cap added no protection, only the refusal. All 232 RAR tests pass, including Codex's two bomb tests (a 66 MiB predecessor; two 33 MiB ones). The 8.5 GiB case takes minutes, so it is recorded here rather than added to the suite.

## Tests and fixtures

- **`ArchivePassBudgetTests`** (6 tests). 4 fail on the old code: the partly read member, the solid 7z bomb, the TAR cursor bomb and TAR cancellation. The other 2 pin behaviour that must not regress: a 7z member in another folder opens without passing the folders before it, and the TAR listing warning.
- **`SevenZipEncodedHeaderTests`** (5 tests). Its 2 allocation tests fail on the old code: 209,923,888 bytes for the 200 MiB claim. The other 3 check that real packed headers still list.
- **Fixtures.** `FileCat.SolidPartial.7z`, `FileCat.SolidBomb.LZMA2.7z` (78,496 bytes, 512 MiB of zeros in one solid folder), `FileCat.TwoFolders.BZip2.7z` (632 bytes) and `FileCat.Bomb.tar.bz2` (1,710 bytes). They were made with 7-Zip 24.01, GNU tar 1.35 and bzip2 1.0.8, and are documented with hashes in `tests/FileCat.Core.Tests/TestData/Archives/README.md`.
- **`SharpCursorResetFailureTests`.** During the change, 6 of its 16 cases failed: a failed close of the retired cursor must surface, and the cursor must never be used or closed again. The final code keeps that contract, and all 16 pass.

## Verification

| Check | Result |
|---|---|
| Release build of `FileCat.slnx` | 0 warnings, 0 errors |
| Full Core suite, Windows x64, final tree | 4,976 tests, 0 failed, 163 skipped, with Codex's `a718c777` and `2b62355f` beneath; the archive classes (206 tests) also pass on the Ubuntu 24.04.5 VM at the pre-rebase `4688812e` |
| The same archives, after the fix | The 2 GiB 7z's small member opens at once (another folder). In `FileCat.SolidBomb.LZMA2.7z` the small member is refused after about 78 MB decoded. The tar.xz and tar.bz2 listings stop at the 1 GiB bound (4.1 s and 3.9 s), whatever the member's declared size |

## What this does not show

- **Hosted CI** of these commits on all four lanes. Since `f134ba37`, the macOS, Windows and Ubuntu lanes fail for reasons outside these files ([recheck](E-REVIEW-2026-10-29-f134ba37.md#hosted-ci-after-f134ba37)).
- **macOS.** The archive classes passed on Ubuntu, but the Mac run of them hung in an existing extraction test, on `f134ba37`'s destination-folder refusal (F-H9). The new tests were therefore not observed on macOS.
- **The provider path.** ArchiveProvider opens members with `CancellationToken.None`, so a 7z pass started from the UI cannot be canceled. It is bounded by the limit instead.
