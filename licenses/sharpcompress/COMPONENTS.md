# Components of FileCat.SharpCompress.dll

`FileCat.SharpCompress.dll` is FileCat's build of SharpCompress 0.50.5: the 743 C# files of
`src/ThirdParty/SharpCompress` in FileCat's source repository, from `src/SharpCompress` of
github.com/adamhathcock/sharpcompress commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`, without the RAR
support (and PPMd variant H) that upstream ports from UnRAR. `THIRD-PARTY-CHANGES.md` beside that source
lists every change; a comparison of every file with the upstream Git blobs confirms it (FileCat changed
`PpmdStream.cs`, the ARJ reader's `using` lines and the factory list, and added the two PPMd files below).

SharpCompress is MIT-licensed (`LICENSE.txt`, its exact upstream bytes). Many of its parts come from other
projects and keep their own licenses. This inventory was made by reading the leading comment of every file,
searching every line for origin statements (copyright, license, "ported", "port of", "based on",
"converted", "derived", "from" a URL, author names), and comparing the files that carry no notice with the
code, comments and history of the projects they point to (SharpCompress's own README and pull requests,
and the origin projects at the revisions it took them from). Each part below says how it was found:

- **header**: the files themselves state the license or copyright;
- **origin, verified**: the files carry no notice, and their code or comments match the origin project's
  files line for line, or SharpCompress's history names the origin;
- **origin, inferred**: the files carry no notice and their structure and identifiers match the origin
  project, but nothing states it.

SPDX expression of the assembly:
`MIT AND Apache-2.0 AND MS-PL AND BSD-2-Clause AND BSD-3-Clause AND Zlib AND Unlicense AND Info-ZIP`.
Every license in it is OSI-approved except **Info-ZIP** (see Open questions). zstd's code is taken under
BSD-3-Clause, one of its two licenses; public-domain code needs no license.
`NOTICE` gathers the notices the licenses ask a binary distribution to carry; the full texts are listed at
the end. File paths are relative to `src/ThirdParty/SharpCompress`.

## Apache-2.0

- **BZip2**, from Apache Ant's bzip2 package (header): `Compressors/BZip2/BZip2Constants.cs`,
  `CBZip2InputStream.cs`, `CBZip2InputStream.Async.cs`, `CBZip2OutputStream.cs`,
  `CBZip2OutputStream.Async.cs`, `CRC.cs`. Four carry "Copyright 2001,2004-2005 The Apache Software
  Foundation" and "Licensed under the Apache License, Version 2.0", "based on the work done by Keiron
  Liddle, Aftex Software"; upstream split the two `.Async.cs` parts off them without the header. Apache
  Ant's NOTICE line is in `NOTICE`. `BZip2Stream.cs` and `BZip2Stream.Async.cs` are SharpCompress's own.
- **Adler-32**, from ImageSharp (header): `Algorithms/Adler32.cs`, "Copyright (c) Six Labors." and
  "Licensed under the Apache License, Version 2.0." (SharpCompress took it from ImageSharp commit
  `09b2cdb83aab0a889dbde8ec2453d605a1be1725`, Apache-2.0 then, without a NOTICE file). Its SIMD paths are
  "Based on" Chromium's `adler32_simd.c` (BSD-3-Clause, as ImageSharp's third-party notices say) and
  zlib-ng's `adler32_avx2.c` (Zlib).
- **CRC-32 and CRC-64**, Damien Guard's Crc32 and Crc64 (origin, inferred): `Crypto/Crc32Stream.cs`,
  `Compressors/Xz/Crc32.cs`, `Compressors/Xz/Crc64.cs`. No header; their table setup, polynomials
  (`DefaultPolynomial`, `Iso3309Polynomial = 0xD800000000000000`) and `Compute` overloads are his, whose
  files read "Copyright (c) Damien Guard. All rights reserved." and "Licensed under the Apache License,
  Version 2.0" (his repository now also offers MIT).
- **ARC LZW, squeezed, RLE90 and ARJ LHA streams**, after unarc-rs by Mike Krüger (origin, inferred):
  `Compressors/ArcLzw/ArcLzwStream.cs`, `ArcLzwStream.Async.cs`, `BitReader.cs`;
  `Compressors/Squeezed/BitReader.cs`, `BitReader.Async.cs`, `SqueezedStream.cs`, `SqueezedStream.Async.cs`;
  `Compressors/RLE90/RLE.cs`, `RunLength90Stream.cs`, `RunLength90Stream.Async.cs`;
  `Compressors/Arj/LHDecoderStream.cs`, `LHDecoderStream.Async.cs`. unarc-rs's LICENSE file is the
  Apache License 2.0 (see Open questions).

## MS-PL, with BSD-3-Clause and zlib notices

- **Deflate, GZip and Zlib streams**, from DotNetZip (header): all 16 files of `Compressors/Deflate`
  (`CRC32.cs`, `DeflateManager.cs`, `DeflateStream.cs`, `DeflateStream.Async.cs`, `FlushType.cs`,
  `GZipStream.cs`, `GZipStream.Async.cs`, `InfTree.cs`, `Inflate.cs`, `Tree.cs`, `Zlib.cs`,
  `ZlibBaseStream.cs`, `ZlibCodec.cs`, `ZlibConstants.cs`, `ZlibStream.cs`, `ZlibStream.Async.cs`). Twelve
  carry "Copyright (c) 2009 Dino Chiesa and Microsoft Corporation." (`CRC32.cs` 2006-2009,
  `DeflateStream.cs` "2009-2010 Dino Chiesa") and "This code is licensed under the Microsoft Public
  License." Seven (`DeflateManager`, `InfTree`, `Inflate`, `Tree`, `Zlib`, `ZlibCodec`, `ZlibConstants`)
  reproduce jzlib's notice, "Copyright (c) 2000,2001,2002,2003 ymnk, JCraft,Inc." under three-clause BSD
  terms, and credit zlib; both are in `NOTICE`.

## BSD-3-Clause (zstd), MIT (ZstdSharp)

- **Zstandard** (origin, verified): `Compressors/ZStandard/Unsafe/*` (217 files) and
  `Compressors/ZStandard/CompressionStream.cs`, `CompressionStream.Async.cs`, `Compressor.cs`,
  `Constants.cs`, `DecompressionStream.cs`, `DecompressionStream.Async.cs`, `Decompressor.cs`,
  `JobThreadPool.cs`, `SafeHandles.cs`, `SynchronizationWrapper.cs`, `ThrowHelper.cs`, `UnmanagedObject.cs`,
  `UnsafeHelper.cs`, `ZstdException.cs`. SharpCompress replaced its ZstdSharp.Port 0.8.6 dependency with
  this source (ZstdSharp: "Based on Zstandard v1.5.7", the same 217 `Unsafe` files; `SafeHandles.cs`
  still names it). ZstdSharp is MIT, "Copyright (c) 2021 Oleg Stepanischev"; zstd is "Copyright (c) Meta
  Platforms, Inc. and affiliates.", BSD-3-Clause or GPL-2.0-only, and FileCat takes BSD-3-Clause. Neither
  ZstdSharp's files nor these carry zstd's notice; `NOTICE` does. `ZStandardStream.cs`,
  `ZStandardStream.Async.cs` and `ZstandardConstants.cs` are SharpCompress's own; `BitOperations.cs` is
  the .NET Foundation's (below).

## MIT, other copyright holders

- **.NET Foundation** (header, "Licensed to the .NET Foundation under one or more agreements. The .NET
  Foundation licenses this file to you under the MIT license."): `Compressors/Deflate64/*` (12 files, 11
  with the header, copied from CoreFX), `Common/IsExternalInit.cs`, `Compressors/ZStandard/BitOperations.cs`.
  `Compressors/Deflate64/InflaterManaged.cs` also carries zlib 1.2.1's notice (Zlib, in `NOTICE`).
- **ADC**, by Natalia Portillo (header, "Copyright (c) 2016 © Claunia.com" with the MIT text, in
  `NOTICE`): `Compressors/ADC/ADCBase.cs`, `ADCBase.Async.cs`, `ADCStream.cs`, `ADCStream.Async.cs`.
- **XZ**, from Sam Bott's XZ.NET (origin, verified by SharpCompress's history): the other 20 files of
  `Compressors/Xz` (all but `Crc32.cs` and `Crc64.cs`, above) and `Compressors/Xz/Filters/BlockFilter.cs`,
  `Lzma2Filter.cs`, `Lzma2Filter.Async.cs`. XZ.NET became MIT on 2017-05-29, two days before SharpCompress
  took it; its license file still has the template's "Copyright (c) [year] [fullname]".
- **LZW (.Z)**, SharpZipLib's LzwInputStream (origin, verified): `Compressors/Lzw/LzwConstants.cs`,
  `LzwStream.cs`, `LzwStream.Async.cs`. SharpZipLib is MIT, "Copyright © 2000-2018 SharpZipLib
  Contributors"; its comment reads "Based on Java code by Ronald Tschalar, which in turn was based on the
  unlzw.c code in the gzip package" (see Open questions).
- **7z container and coders**, after Tobias Käs's managed-lzma (origin, inferred): SharpCompress's README
  says "7Zip implementation based on: https://code.google.com/p/managed-lzma/" (MIT, "Copyright (c)
  2011-2012 Tobias Käs"). The parts that follow it are in `Common/SevenZip` and, in `Compressors/LZMA`,
  `AesDecoderStream`, `Bcj2DecoderStream`, `BitVector`, `DecoderStream`, `Log`, `Registry` and
  `Utilities`; upstream records no exact file list.
- **LHA decoding**, after rust-delharc by Rafal Michalski (origin, inferred; comments copied from it):
  `Compressors/Arj/LhaStream.cs`, `LhaStream.Async.cs`, `ILhaDecoderConfig.cs`, `Lh5DecoderCfg.cs`,
  `Lh7DecoderCfg.cs`, `HistoryIterator.cs`, `IRingBuffer.cs`, `RingBuffer.cs`, `HuffmanTree.cs`,
  `HuffmanTree.Async.cs`. delharc is MIT or Apache-2.0, "Copyright 2023 Rafal Michalski"; FileCat takes MIT.
- **Branch converters** contributed "by Louis-Michel Bergeron, on behalf of aDolus Technolog Inc., 2022"
  (header; SharpCompress's MIT by contribution): `Compressors/Filters/BranchExecFilter.cs` and
  `Compressors/Xz/Filters/ArmFilter`, `ArmThumbFilter`, `IA64Filter`, `PowerPCFilter`, `SparcFilter`,
  `X86Filter` (each with its `.Async.cs`). The x86 converter follows Linux's `lib/xz/xz_dec_bcj.c`, then in
  the public domain (0BSD in later Linux versions).

## Public domain and Unlicense

- **LZMA**, from the LZMA SDK's C# code (origin, verified: "LZMA SDK is written and placed in the public
  domain by Igor Pavlov"): `Compressors/LZMA/LzmaBase.cs`, `LzmaDecoder.cs`, `LzmaDecoder.Async.cs`,
  `LzmaEncoder.cs`, `ICoder.cs`, `CRC.cs`, `Compressors/LZMA/LZ/*` (4 files) and
  `Compressors/LZMA/RangeCoder/*` (6 files), whose range decoder is the PPMd7z coder FileCat's 7z PPMd uses.
- **Branch and delta filters**, from XZ for Java 1.9 (origin, verified: "This file has been put into the
  public domain"): `Compressors/Filters/BCJFilter.cs`, `BCJFilterARM.cs`, `BCJFilterARMT.cs`,
  `BCJFilterIA64.cs`, `BCJFilterPPC.cs`, `BCJFilterSPARC.cs`, `DeltaFilter.cs`. `BCJ2Filter.cs` and
  `Filter.cs` follow the LZMA SDK (origin, inferred); `BCJFilterARM64.cs` and `BCJFilterRISCV.cs` state
  no origin.
- **Unshrink**, Hans Wennborg's hwzip (origin, verified: the comments are those of its `shrink.c`, which
  "is put in the public domain"; hwzip's LICENSE is the Unlicense, here from a mirror of hwzip 2.3):
  `Compressors/Shrink/HwUnshrink.cs`.
- **PPMd variant I** (header): `Compressors/PPMd/I1/*` (9 files), "This is a port of Dmitry Shkarin's PPMd
  Variant I Revision 1. Ported by Michael Bone". Shkarin's PPMd var.I is public domain (7-Zip's `Ppmd8.c`
  records it so); see Open questions for the port.

## BSD-2-Clause and MIT: FileCat's own files

- `Compressors/PPMd/PpmdModelH.cs`: the PPMd variant H model of FileCat's RAR 3 and 7z PPMd decoding,
  FileCat's port of rardecode v2.4.5 (`ppm_model.go`), "Copyright (c) 2015, Nicholas Waples", BSD-2-Clause
  (full text in FileCat's `THIRD-PARTY-NOTICES.md` and `licenses/rardecode/LICENSE`). No UnRAR source.
- `Compressors/PPMd/PpmdModelH7z.cs` and FileCat's other changes: FileCat's MIT license.

## Info-ZIP (not OSI-approved)

- **Implode** (header and origin, verified): `Compressors/Explode/ExplodeStream.cs`,
  `ExplodeStream.Async.cs`, `HuftTree.cs`. `HuftTree.cs` says it "has been converted to C# based on the
  original huft_tree code found in inflate.c -- by Mark Adler version c17e, 30 Mar 2007"; `ExplodeStream`
  keeps the routines, tables and comments of Info-ZIP UnZip's `explode.c` (`get_tree`, `cplen2`, `cplen3`,
  `cpdist4`, `cpdist8`, NEEDBITS/DUMPBITS). Those UnZip 6.0 files are "Copyright (c) 1990-2008 Info-ZIP"
  under the Info-ZIP license (version 2009-Jan-02, `upstream/Info-ZIP-LICENSE`); older versions of
  `inflate.c` had been put in the public domain, this one had not.
- **Reduce** (origin, verified for its bit reader): `Compressors/Reduce/ReduceStream.cs`,
  `ReduceStream.Async.cs`. Its READBITS is UnZip's macro line for line (`8*(int)(sizeof(G.bitbuf)-1)` as
  `8 * (int)(4 - 1)`).

SharpCompress reaches these two only through its ZIP compression providers (`Providers/Default`, ZIP
methods 2 to 6), which FileCat does not use: FileCat reads ZIP with its own reader. They ship in the
assembly all the same.

## Open questions

1. **Info-ZIP in a release that must be OSI-only.** Removing `Compressors/Explode` and `Compressors/Reduce`
   (with `ExplodeCompressionProvider`, the Reduce providers and their registry entries) would leave only
   OSI-approved licenses; keeping them needs an owner decision and the Info-ZIP conditions (`NOTICE`,
   `upstream/Info-ZIP-LICENSE`).
2. **Reduce and Samuel H. Smith.** Info-ZIP's separately distributed `unreduce.c` is Samuel H. Smith's code,
   whose terms (quoted in UnZip's `COPYING.OLD`) forbid commercial use. Whether `ReduceStream`'s
   follower-set code comes from it was neither confirmed nor ruled out; his source was not available.
3. **PPMd variant I port.** Michael Bone's port states no license (his page is gone); it has shipped in
   SharpCompress under SharpCompress's MIT license since 2013. FileCat reaches it only when a 7z PPMd coder
   has two property bytes instead of five (7-Zip always writes five).
4. **unarc-rs.** Its README says "MIT or Apache-2.0", its Cargo.toml "MIT", its LICENSE file holds only
   the Apache License 2.0 with no MIT copyright line; this inventory follows the LICENSE file.
5. **CRC32.cs and vbAccelerator.** `Compressors/Deflate/CRC32.cs` (DotNetZip) is "based on example source
   code published at" vbAccelerator, whose license (Apache-1.1 style, no SPDX identifier, not OSI-approved)
   asks for the acknowledgement "This product includes software developed by vbAccelerator"; whether code
   was copied is unknown.
6. **LZW and Ronald Tschalar.** SharpZipLib's LZW reader is "Based on Java code by Ronald Tschalar"; the
   license of that Java code was not established here.
7. **XZ.NET's copyright line** is the MIT template's placeholder; the MIT terms are unambiguous.

## Texts in this folder

| File | Source | SHA-256 |
|---|---|---|
| `LICENSE.txt` | SharpCompress `LICENSE.txt`, commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626` (Git blob `94e2f5bc83ac404584fd92c25cfcc305ea2f8942`) | `b7ca2b6174cee11afe41d78b48527e3d7659a4435c25019a4a5e072299a2f8ed` |
| `Apache-2.0.txt` | https://www.apache.org/licenses/LICENSE-2.0.txt | `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30` |
| `MS-PL.txt` | spdx/license-list-data v3.29.0 (`31ba1a50e5397e00a304dbadc76531740e89ee48`) `text/MS-PL.txt` | `7a162b1da10f1c22db4c68f07bec4a8355259f8d4aab5b00a2b2bbd423d833dd` |
| `upstream/Info-ZIP-LICENSE` | madler/unzip `3d61d782edc651a6a2d9c99b33cbfb247138369f` (UnZip 6.0) `LICENSE` | `7469b81d5d29ac4fd670f7c86ba0cb9fa34f137a2d4d5198437d92ddf918984b` |
| `upstream/zstd-LICENSE` | facebook/zstd `f8745da6ff1ad1e7bab384bd1f9d742439278e99` (v1.5.7) `LICENSE` | `7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8` |
| `upstream/ZstdSharp-LICENSE` | oleg-st/ZstdSharp `f475a7d4746d305094fbb09563cb0fcb167bec18` (0.8.6) `LICENSE` | `9a6a7216e532c4ee4f78881d31fb641c27148eb01b2d0e8e3235ffe9a70a662c` |
| `upstream/dotnet-LICENSE.TXT` | dotnet/runtime `8237dfd6ddd6a355dfbcac19c2d09478e1ffc88e` `LICENSE.TXT` (the same bytes as CoreFX's) | `cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310` |
| `upstream/XZ.NET-license.txt` | sambott/XZ.NET `1c60f3ffae995bd82cad11d699162dfc476a39c9` `license.txt` | `002c2696d92b5c8cf956c11072baa58eaf9f6ade995c031ea635c6a1ee342ad1` |
| `upstream/SharpZipLib-LICENSE.txt` | icsharpcode/SharpZipLib `33f64eb0f28cdd2b084cb822fcc224c7c5aba553` `LICENSE.txt` | `d7fbbc16f871bf24fa5624c7eaf945054b778985f32676dcacba41fddca61133` |
| `upstream/managed-lzma-license.txt` | weltkante/managed-lzma `e1d854e66c4218b3842c1702f6c19700f19f04aa` `license.txt` (Latin-1 bytes) | `31166505cbfec0c7c309e2ee53315edf3634a26395c0ab9c9435816c261a6ad0` |
| `upstream/delharc-LICENSE-MIT` | royaltm/rust-delharc `969e19d90ddf0a8e93598ac5cf117dfd8d1c2b7d` `LICENSE-MIT` | `78573d550901a931d792f2868ceed0b1903a82ed396636ae06b4c525650706fa` |
| `upstream/Chromium-LICENSE` | chromium/chromium `bd5fd3a6666e35fc8a3012dea3bdcbbb126dc45d` `LICENSE` | `368cca1106be99d39ecd32a38d8305585d802a475effb66380b91ffc9bcf709b` |
| `upstream/zlib-ng-LICENSE.md` | zlib-ng/zlib-ng `2bf94abf2b1760b3b1afd916c9e09c7e61bf6540` `LICENSE.md` | `6c9f0d975b41afaa34d22f55bb8986ce69e5cb7ad327cb2b28820cd425edf5ee` |
| `upstream/hwzip-LICENSE` | skissane/pdos `0fe81209e78d022b40301f86f97c7f4d3e406d0a` `hwzip/LICENSE` (a copy of hwzip 2.3) | `7e12e5df4bae12cb21581ba157ced20e1986a0508dd10d0e8a4ab9a4cf94e85c` |

`SHA256SUMS` pins every other file in this folder; the packaging scripts check the folder against it before
they copy it into a package.
