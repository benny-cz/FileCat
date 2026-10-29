# E-I14-SHARPCOMPRESS — FileCat's SharpCompress: no UnRAR code, cut to what FileCat uses, all OSI-licensed

**Engineering for I14 / EXT-02, the second half of the RAR route, on `main` at `ca293815c98b90e44d1f1b311b2ddd5de57eb78d` (2026-10-29). Preliminary component and package evidence; not hosted CI of this commit, a legal determination, candidate qualification or release GO.** The first half, FileCat's own RAR reader, is [E-I14-RAR](E-I14-rar-reader.md).

## What ships now

| | Before | Now |
|---|---|---|
| Library | NuGet package SharpCompress 0.50.5 | `FileCat.SharpCompress.dll`, built from `src/ThirdParty/SharpCompress` (SharpCompress 0.50.5, commit `e4193af8`, tarball SHA-256 `93eda577…69d0`); the package left every production lock and stays only as the tests' oracle (`extern alias UpstreamSharpCompress`) |
| What FileCat reads with it | 7z, bzip2, xz, zstd (RAR moved to FileCat's own reader at `e57ebaaf`) | the same |
| UnRAR-derived code | RAR (`Archives/Rar`, `Common/Rar`, `Compressors/Rar`, `Readers/Rar`) and PPMd variant H (`Compressors/PPMd/H`) | none |
| Other code | every SharpCompress format, writer and codec (743 C# files) | 7z, bzip2, xz and zstd readers only (477 files); ZIP, TAR, GZip, LZip, ARC, ARJ, ACE and LHA, all writers, and the codecs only they use are gone |
| Declared licenses | `MIT` (the package's) | `MIT AND Apache-2.0 AND MS-PL AND BSD-2-Clause AND BSD-3-Clause AND Zlib`: every part's license, all OSI-approved |

**Changes by commit:**
- **`ac25aaa1`:** one PPMd variant H model, ported from rardecode v2.4.5 (BSD-2-Clause), behind a small range-decoder interface. RAR 3 decodes through it with RAR's range coder, 7z with 7-Zip's.
- **`99bd32d5`:** 7z PPMd checked against 7-Zip and upstream SharpCompress.
- **`f8a30667`, `74d6c89f`:** the component licenses, and the project named after its assembly.
- **`e3b20fc3`:** the PPMd damage helper no longer inlined.
- **`34bc2abb`:** 7z member CRC-32 checks (below).
- **`ca293815`:** the trim and its license records.

`src/ThirdParty/SharpCompress/THIRD-PARTY-CHANGES.md` lists every change file by file.

**What the trim removed, and why:**
- **Not OSI-approved, or terms unclear.** Info-ZIP's Implode and Reduce are not OSI-approved, and Reduce may also derive from Samuel H. Smith's `unreduce.c`, which forbids commercial use. The terms of PPMd variant I, unarc-rs's ARC/squeezed/RLE90/LHA streams, and the LZW reader after Ronald Tschalar's Java code could not be established.
- **Replaced.** DotNetZip's CRC32 class says it is based on example code from vbAccelerator, so FileCat now uses a CRC-32 of its own there.
- **Inventory.** `licenses/sharpcompress/COMPONENTS.md` is the file-by-file inventory: each part's license and how it was established.
- **Shipped with it.** `NOTICE`, the 10 license texts and the inventory ship with every package. They are pinned by `SHA256SUMS` (SHA-256 `da9014fb8d4af0cab6ba4e19e1bd03bd88ea4f649e4dc1ac45cdd105c532ce8b` in `eng/publish.ps1`, `eng/package-linux.sh` and `eng/package-macos.sh`), and each script refuses to package a folder that differs.

**Two defects fixed along the way:**
- **7z members were not checked.** SharpCompress checks a 7z member's CRC-32 only when it extracts to a file itself, never on the entry stream FileCat reads. A damaged 7z could therefore read back as wrong bytes with no error. `SevenZipMemberStream` (`src/FileCat.Archives/ArchiveFormats.cs`) now refuses a member whose CRC-32 or size does not match its header. It also turns the decoders' own exception types into `InvalidDataException`, like every other refusal.
- **XZ block padding** was read with one `Read` call: a short read left padding bytes for the next field. It is now read in full, or refused when cut short.

## Verification

| Check | Result |
|---|---|
| Release build of `FileCat.slnx`, no incremental build | 0 warnings, 0 errors |
| Full Core suite, Windows x64, this tree before the three new fuzz seeds were added | 4,937 tests, 0 failed, 161 skipped |
| `SevenZipCrcTests`, `SevenZipPpmdTests`, `RarReaderTests`, `ArchiveFormatTests` at `ca293815` | 194/194 |
| 7z PPMd differential (`SevenZipPpmdTests`) | FileCat equals upstream SharpCompress and 7-Zip 24.01 on `7Zip.PPMd.7z` and five FileCat-made fixtures (orders 2–32, memory 64 KiB–8 MiB, solid, a model restarted 51 times) |
| 7z member CRC (`SevenZipCrcTests`, 300 single-bit damages per coder) | Nothing read back as if whole. Refused by the CRC-32 check: Copy 299, Deflate 262, LZMA 36, BZip2 18, PPMd 11, LZMA2 1; the decoders refused the rest. 7z Deflate64 is refused as unsupported, as it always was. |
| Archive fuzz (`ArchiveFuzzTests`, 16 seeds incl. the new BZip2, Deflate and Copy 7z × 1,000 rounds) | 16,000 rounds, 0 failures, 54 s; largest round 242 MB, slowest 153 ms; log `V:\FileCat\artifacts\rar-reader-20261029\archive-fuzz-1000x16-ca293815.log`, SHA-256 `afa66298192d33ffcc320921d550e5b161393f0e98b68db305fcc24fabdc359d` |
| RAR PPMd speed (`PpmdBenchmark`, Release, medians of six alternating runs, old RAR-only model → shared model) | Restart-heavy noise in a 1 MiB model: +1.6% (+8.6% before `e3b20fc3`). Text: +4.3% at order 6 and +2.8% at order 16, within the ±5% run-to-run noise. |
| Validators | Dependency notices 71/71; artifact SBOM 18/18 (with a control for this license expression); locked restore 33 graphs; release assets 8/8; draft release assets 32/32; draft package set 13/13; release register intact |

**Packages from a clean checkout of `ca293815`** (version `1.0.0-stepb.2`). In every package:
- the SBOMs record `Built from FileCat source commit ca293815…` and declare the expression above, with no SharpCompress package;
- the payload carries `FileCat.SharpCompress.dll`, no `SharpCompress.dll`, and the 13 files of `licenses/sharpcompress`.

| Machine | Output | Bytes | SHA-256 |
|---|---|---:|---|
| Windows 11 x64 (this host), `eng/publish.ps1` | `FileCat-1.0.0-stepb.2-win-x64-portable.zip` | 102,189,701 | `cda939ab0c71a2e4178af70bf3113281e4a45c93ce843518f7e4727204078e64` |
| | `FileCat-1.0.0-stepb.2-win-x64-fdd.zip` | 65,308,935 | `2eac54d6e175d7a3d2f116874df6611b41b274ebb7fecd2c0716eef70e437199` |
| Ubuntu 24.04.5 VM, `eng/package-linux.sh`, exit 0 in 69 s | `FileCat-1.0.0-stepb.2-linux-x64.tar.gz` | 70,162,981 | `e400379b161dc2fe1dfa47245e47108e74573c6b2e40cec65824679e1e9fec4f` |
| | `filecat_1.0.0-stepb.2_amd64.deb` | 57,049,072 | `b61b025a4ae0b80c0891701fabc03357522be21b864dff718b057ef53b99a09a` |
| | `FileCat-1.0.0-stepb.2-x86_64.AppImage` (12/12 pinned license files verify after extraction) | 64,993,784 | `d3228be4d6241de67435caefc255ae59c9b1d807cb36d10bdd80ed5210f1ef2f` |
| | `FileCat-1.0.0-stepb.2-linux-x64-appimage-runtime-source.tar.gz` (the pinned bundle) | 4,704,832 | `be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587` |
| macOS 27.0.1 arm64, `eng/package-macos.sh`, exit 0 in 41 s | `FileCat-1.0.0-stepb.2-osx-arm64.zip` (`codesign --verify --deep --strict` passes, `--version` prints `FileCat 1.0.0-stepb.2`, 12/12 license files verify) | 68,509,247 | `13d964989f0d9e18b86c122bdf797d280fc88848956c2c45a91b888e4ee2389b` |

SBOM hashes:
- Windows: setup payload `d7488e11…cda01`, portable `49ef0823…805c6`, framework-dependent `a99ce7f3…70814`.
- Linux: tar `b55760b4…f36c27`, deb `89e6ef21…ec270`, AppImage `85ac9c5f…2ea4ea`.
- macOS: `e18d9823…606674`.

The Ubuntu and Mac checkouts came from the public repository plus a git bundle of this branch.

## Not covered

- **Hosted CI** of `ca293815` has not run yet; it starts when this is pushed.
- **No legal determination.** The inventory records provenance as found.
- **Unchanged limits.** 7z Deflate64 was never readable, and that is unchanged. ZIP is read by FileCat's own reader, so dropping SharpCompress's ZIP codecs costs nothing.
- **Intermediate commits** of the branch were not each built and tested; the final tree was.
