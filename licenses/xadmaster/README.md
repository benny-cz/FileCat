# XADMaster

FileCat's RAR 1.5 decompression (`src/FileCat.Archives.Rar15`, built and shipped as the separate
assembly `FileCat.Archives.Rar15.dll`) is a C# port of code from
[XADMaster](https://github.com/MacPaw/XADMaster), The Unarchiver's archive library, at commit
`7cb9ee0abbb163f261e4cb74501e15067032319c`: Copyright (c) 2017-present MacPaw Way Ltd., originally
written by Dag Ågren. It is licensed under the GNU Lesser General Public License, version 2.1 or (at
your option) any later version, whose full text is `LICENSE` (exact upstream bytes, SHA-256
`9b872a8a070b8ad329c4bd380fb1bf0000f564c75023ec8e1e6803f15364b9e9`). The port is under the same
license.

Ported files:

- `XADRAR15Handle.m` and `XADRAR15Handle.h`: the RAR 1.5 decoder, its fixed codes and adaptive tables;
- `XADFastLZSSHandle.m`, `XADFastLZSSHandle.h`, `XADLZSSHandle.h`, `LZSS.h` and `LZSS.c`: the 64 KiB
  window semantics;
- `XADPrefixCode.m`: canonical and explicit prefix codes;
- `CSInputBuffer.m` and `CSInputBuffer.h`: most-significant-bit-first bit reading.

FileCat changed the ported code for untrusted input: a match that reaches before the first byte of
its solid run or runs past the member's size is refused instead of copying the zeroed window, every
table position, length and distance is bounded, truncated or malformed data ends in
`InvalidDataException`, and a decoder that failed refuses to continue a solid run. It contains no
UnRAR source.

The assembly's complete corresponding source is this repository's `src/FileCat.Archives.Rar15`; it
builds on its own with the .NET SDK that `global.json` selects
(`dotnet build src/FileCat.Archives.Rar15/FileCat.Archives.Rar15.csproj`). The rest of FileCat (MIT)
uses it only through its public `Rar15Decoder` class and ships it as its own DLL, neither merged
into another assembly nor strong-named, so a modified build of that source with the same assembly
name and version can replace it: the separate, replaceable library plus this public source satisfy
LGPL-2.1 section 6.
The test-only `tests/FileCat.Core.Tests/Rar15TestEncoder.cs` mirrors the decoder's state and is
under the same license.
