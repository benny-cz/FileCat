# rardecode

FileCat's RAR reader (`src/FileCat.Archives/Rar`) contains code ported from
[rardecode](https://github.com/nwaples/rardecode) v2.4.5, Copyright (c) 2015 Nicholas Waples,
under the BSD 2-Clause license in `LICENSE` (exact upstream bytes, SHA-256
`a853785796b4bd072bbc63afd3a510c8918198b4f778351b5a6ed71a3448f1e1`). Each ported file names its
source. FileCat changed the ported code: it bounds every size, count and channel value read
from an archive, grows the window only with decoded output, refuses the general RAR 3 VM
(standard filters only) and verifies CRC32/BLAKE2sp. It contains no UnRAR source.


The RAR5 password primitives in `src/FileCat.Archives/Writing/Rar5EncryptionPrimitives.cs` and the password-aware
header/member reading in `src/FileCat.Archives/Rar/Rar5ReadPasswordSession.cs` adapt `calcKeys50`, encryption layouts
and checksum/password-check processing from `archive50.go` and `reader.go` at commit
`ad22ba91a2ab0d3ff6bca20cd978b33ee78faab1`. FileCat adds bounded cumulative KDF work, cancellation, .NET cryptographic
primitives and owned-key-buffer disposal. These parts retain BSD-2-Clause attribution/terms. Exact source/blob/hash
provenance is in `eng/validation/archive-writing/rar5-independent-probe/password-reference-sources.json`.
The new stream/container glue and CBC lease ownership are FileCat code using framework cryptography. There is no
new crypto package or restricted decoder source import. This attribution is not complete profile qualification.
The original independent RAR compressor is separate FileCat code; this port does not import restricted UnRAR source.
