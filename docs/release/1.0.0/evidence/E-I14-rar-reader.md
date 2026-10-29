# E-I14 — FileCat's own RAR reader: provenance, switch-over, differential tests and stress

**Engineering for I14 / EXT-02 (owner disposition 2026-10-28), pushed to `main` as `e57ebaafd7a4fdabacc6ea4f274c5baae93a3bca` on 2026-10-29. Not a legal determination, hosted CI result, frozen candidate or release GO. REL-RAR-LICENSE stays Open: SharpCompress still ships its own UnRAR-derived RAR and PPMd var.H code until the second half of the route lands, and the notices must then match the shipped code.**

## What shipped

`ArchiveKind.Rar` now opens through `FileCat.Archives.Rar.RarReader`; SharpCompress no longer reads RAR in the product. The reader is managed code without `unsafe`:

| Part | Methods | Origin | License |
|---|---|---|---|
| `src/FileCat.Archives/Rar/` (4,534 lines) | RAR 1.5–4.x and 5.x headers (encoded Unicode names, extended times, volume numbering); RAR 2.0/2.6 LZ and audio (unpack 20, 26); RAR 2.9/3.x LZ, PPMd var.H and the six standard RAR 3 filters (unpack 29, 36); RAR 5/7 LZ and filters (unpack 50, 70); stored members; CRC32 and BLAKE2sp verification | Ported from rardecode v2.4.5 (`github.com/nwaples/rardecode`), Copyright (c) 2015 Nicholas Waples; BLAKE2sp is FileCat's own and CRC32 is Core's `Crc32` | BSD-2-Clause; `licenses/rardecode/LICENSE`, committed blob SHA-256 `a853785796b4bd072bbc63afd3a510c8918198b4f778351b5a6ed71a3448f1e1` |
| `src/FileCat.Archives.Rar15/` (836 lines), assembly `FileCat.Archives.Rar15` | RAR 1.5 method (unpack 15) | C# port of XADMaster's `XADRAR15Handle`, commit `7cb9ee0abbb163f261e4cb74501e15067032319c`, Copyright (c) 2017-present MacPaw Way Ltd. (originally Dag Ågren) | LGPL-2.1-or-later, in its own replaceable assembly; `licenses/xadmaster/LICENSE`, SHA-256 `9b872a8a070b8ad329c4bd380fb1bf0000f564c75023ec8e1e6803f15364b9e9` |

**Route as built, against the 2026-10-28 plan.** The plan named libarchive's readers for RAR 2.9–5.x, Igor Pavlov's Ppmd7 for PPMd var.H and XADMaster for RAR 1.5 and 2.0/2.6. rardecode covers RAR 2.0 through 7, PPMd var.H included, under one BSD-2-Clause source, so the LGPL part shrinks to the RAR 1.5 method. libarchive contributes only its BSD-2-Clause test corpus. The upstream sources carry no UnRAR license text, and no UnRAR source was used. That statement covers provenance only; it is not a legal opinion.

Encrypted RAR members and encrypted file lists stay unsupported, as for every other format. An encrypted member is listed as encrypted, and opening it is refused. An archive whose file list is encrypted is refused as a whole, with that reason.

## Hardening

- **Bit reader.** Lookahead past the data is zero-padded but can never be consumed, so short or hostile input cannot make a decoder loop.
- **Huffman tables.** Canonical decoding rejects over-subscribed tables.
- **Window.** It grows only with decoded output, to a 1 GiB cap (`RarLimits.MaxWindowBytes`). It never overwrites unread bytes and checks every match distance against both the bytes written and the dictionary. A soft target of about 1 MiB of decoded-but-unread output (`ShouldDecode`) keeps reading and cancellation interleaved.
- **Filters.**
  - Size limits: at most 4 MiB per filter block, 8,192 pending filters and 1–1,024 channels, plus an RGB geometry check.
  - Only the six standard RAR 3 filter programs are accepted, identified by CRC32 and code length. There is no general RarVM.
- **PPMd.** Model memory is capped at 256 MiB, and every model index is checked.
- **Header limits.** Headers are capped at 2 MiB, names at 64 KiB, and archives at 1,000,000 members.
- **Solid and multi-volume sets.**
  - A solid run that changes decoder family is refused.
  - After a decoder failure, a solid run restarts cleanly.
  - A member whose middle volume is missing (`HasGap`) is listed once and never decoded.
- **Refusals.** Every refusal is an `InvalidDataException` tagged through `Exception.Data` as damage/unsupported (`RarData.IsDamage`) or a checksum mismatch (`RarData.IsChecksum`).

## Switch-over (`e57ebaaf`)

- `ArchiveFormats` maps `ArchiveKind.Rar` to `RarReader.Open(path, verifyOpened)`.
- The new internal `IVolumeStreams` interface hands `ArchiveProvider` the opened volume streams from either reader, so its per-volume identity checks (I136, I148, I415, I416) apply unchanged.
- One behaviour changes on purpose: SharpCompress handed out a RAR 4 Unix symbolic link as a small file holding the link target. FileCat lists it as a link, and FileCat never extracts links.

## Differential and corpus tests

- **`RarReaderTests`.** Covers SharpCompress 0.50.5's RAR fixtures (commit `e4193af8`, each fetched by Git blob ID): RAR 1.5 through 5, stored, solid, both multi-volume naming schemes, comments, BLAKE2sp, encrypted members and headers, and damaged files. It compares FileCat's members and bytes with the retired SharpCompress path. Per-decoder tests use generated streams and edge cases: PPMd escapes and model continuation, filters inside PPMd data, empty solid members, out-of-range matches, truncation, garbage, and decoder-family switches.
- **`RarLibarchiveCorpusTests`.** Uses libarchive v3.8.1's 76 RAR test files (fetched by Git blob ID, uudecoded, BSD-2-Clause) in 77 cases:
  - **63 archives or first volumes.** Wherever the retired path reads an archive completely, FileCat must give the same members and identical bytes, apart from the RAR 4 symlinks above. On every other file it may only refuse with `InvalidDataException`, within 60 s.
  - **14 real RAR-made archives that must decode completely.** They cover PPMd/LZSS switching, the RAR 3 filters, compression levels, many LZSS blocks, a four-volume set, the RAR 5 ARM filter, and RAR 5 solid and multi-volume sets.
- **Windows 11 x64**, on the tree committed as `e57ebaaf`:
  - Full Core suite: 4,846 tests, 0 failures, 152 platform skips.
  - Archive-related App tests: 20/20.
  - The two `ArchiveMultipartRarTests` failures (a member listed twice across a missing middle volume) were fixed by `HasGap` before the commit.
- **Native runs** of the five RAR/archive classes (`RarReaderTests`, `RarLibarchiveCorpusTests`, `ArchiveFormatTests`, `ArchiveMultipartRarTests`, `ArchiveLegacyRarTests`) at `e57ebaaf`:

| Platform | Build | Result | Log | SHA-256 |
|---|---|---|---|---|
| macOS 27.0.1 arm64 (M1) | framework-dependent osx-arm64 test publish, .NET 10.0.12 | 268/268, 0 skipped, 10.1 s | `native-tests-macos-arm64-e57ebaaf.log` | `fc63a9656a8896d4926764048a9f5db69526b34df3f8859047bee84da33f0e67` |
| Ubuntu 24.04.5 x64 (VM) | self-contained linux-x64 Release test publish, .NET 10.0.14 | 268/268, 0 skipped, 13.2 s | `native-tests-ubuntu2404-x64-e57ebaaf.log` | `dea928f9ac64b0ad5edea303dca6876fb88aa264fcca5f87b6ab8b6a8b7ecd3f` |

**Hosted CI** ([run 40808191406](https://github.com/benny-cz/FileCat/actions/runs/40808191406), exact pushed `e57ebaaf`). The Core suite, with every RAR test and the default-round fuzz seeds, passed on all four lanes:

| Lane | Core tests | Failed |
|---|---|---:|
| Windows x64 | 4,846 (159 skipped) | 0 |
| Windows ARM64 | 4,846 (159 skipped) | 0 |
| Ubuntu 24.04 | 4,841 (111 skipped) | 0 |
| macOS 26 | 4,841 (130 skipped) | 0 |

This adds Windows ARM64, which no local run covered. The run failed elsewhere, on App tests unrelated to RAR: 38 state-saving and edit-session tests on each elevated Windows runner (I555), and one macOS checksum-dialog test that timed out waiting for its worker.

## Stress

`RarFuzzTests` damages a seed archive in a different way each round and reads the result in memory:

| Round mod 5 | Damage |
|---|---|
| 0 | bit flips inside packed data, with headers intact |
| 1 | a run of overwritten packed bytes |
| 2 | header fields changed, with the header checksum recomputed |
| 3 | raw damage anywhere |
| 4 | a truncated volume |

- **Rules per round.**
  - Each round depends only on its seed and round number.
  - It runs on its own thread with a 120 s limit (`FILECAT_RAR_FUZZ_SECONDS=120`).
  - It may allocate at most 1.5 GiB and reads at most 64 MiB of output.
  - Any exception other than `InvalidDataException`, a timeout or an over-budget allocation fails the campaign.
- **The 23 seeds.**
  - 16 SharpCompress fixtures: RAR 5 plain, solid, stored, CRC+BLAKE2, multi-volume, comment, encrypted names; RAR 3/4 stored, plain, solid, RAR 4; RAR 2.0; RAR 1.5; RAR 3 audio filter; RAR 3 multi-volume; RAR 2.0 multi-volume.
  - 7 libarchive real archives: PPMd/LZSS switching, RAR 3 filters, best compression, many LZSS blocks, a four-volume set, the RAR 5 ARM filter, RAR 5 solid multi-volume.

The logs are in `V:\FileCat\artifacts\rar-reader-20261029` (DEC-10 custody):

| Platform and source | Rounds | Failures | Most allocated in a round | Slowest round | Wall time | Log | SHA-256 |
|---|---:|---:|---|---|---:|---|---|
| Windows 11 x64, Release, `6e06b8306de7` (all four decoder families and the libarchive seeds, before the switch-over) | 0–29,999 × 23 = **690,000** | 0 | 493 MiB (four-volume set, round 20,120) | 474 ms (PPMd/LZSS switching, round 4,240) | 7,474 s | `fuzz-campaign-all-decoders-30000x23-6e06b830.log` | `be53d23ee224bb8e503d31c1ffa3348079bb2675c6ff8f7da8d6078830a44a87` |
| macOS 27.0.1 arm64, `e57ebaaf` | 0–9,999 × 23 = **230,000** | 0 | 369 MiB (four-volume set, round 5,280) | 230 ms (four-volume set, round 7,995) | 3,236 s | `fuzz-campaign-all-decoders-10000x23-macos-arm64-e57ebaaf.log` | `0321ded8014b79bfd521512fe117a412c3970a95d6d184cc2d0ba04004977f53` |
| Ubuntu 24.04.5 x64, `e57ebaaf` | 0–9,999 × 23 = **230,000** | 0 | 369 MiB (four-volume set, round 5,280) | 354 ms (four-volume set, round 4,420) | 2,749 s | `fuzz-campaign-all-decoders-10000x23-ubuntu2404-x64-e57ebaaf.log` | `ea6ca3c7c841c8d86aea902fd5927eb80f8d0aa75cefdc1f5ec130ad4c4658a8` |
| Windows 11 x64, Release, `e57ebaaf` | 30,000–39,999 × 23 = **230,000** | 0 | 493 MiB (four-volume set, round 31,990) | 380 ms (four-volume set, round 38,463) | 2,522 s | `fuzz-campaign-all-decoders-10000x23-from30000-e57ebaaf.log` | `e002a47c2278dbdbf0b006a3117132dd6d8b7dcdf10f073a5695c5ab595acee6` |

- **The pushed commit on all three platforms.** A further Windows run at `e57ebaaf` used new rounds (30,000–39,999). Every platform has therefore fuzzed the exact pushed source, 690,000 rounds at `e57ebaaf` plus 690,000 at `6e06b830`: **1,380,000 rounds in all, without a failure**.
- **Same verdicts on both CPUs.** macOS arm64 and Ubuntu x64 produced identical outcome counts for every seed: archive refused, intact, members refused, some members refused. Both ran the same rounds 0–9,999 at `e57ebaaf`.
- **No round came near the limits.** The largest allocation (493 MiB) is a third of the 1.5 GiB budget; it is window growth on a four-volume set whose sizes the damage inflated. The slowest round took 474 ms against a 120 s limit.
- **Earlier campaign.** A tree on top of `e1b02a1d` ran 40,000 rounds × 15 seeds (600,000 rounds), all clean. That was before the RAR 1.5 and 2.9/3.x decoders and the libarchive seeds were merged, and the campaigns above supersede it. Its log is `fuzz-campaign-40000x15-e1b02a1d-tree.log`, SHA-256 `9e993933134e4eb52a25d957463be8c24d644dd695b9dbc2a55ad5e92f8aadc0`.

## Remaining for REL-RAR-LICENSE

Update 2026-10-29: SharpCompress now ships without its UnRAR-derived code, cut down to what FileCat uses, with every part OSI-licensed and its notices, manifest and SBOM matching ([E-I14-SHARPCOMPRESS](E-I14-sharpcompress-osi.md), `ca293815`). What remains is hosted CI on the pushed source.
