# rardecode

FileCat's RAR reader (`src/FileCat.Archives/Rar`) contains code ported from
[rardecode](https://github.com/nwaples/rardecode) v2.4.5, Copyright (c) 2015 Nicholas Waples,
under the BSD 2-Clause license in `LICENSE` (exact upstream bytes, SHA-256
`a853785796b4bd072bbc63afd3a510c8918198b4f778351b5a6ed71a3448f1e1`). Each ported file names its
source. FileCat changed the ported code: it bounds every size, count and channel value read
from an archive, grows the window only with decoded output, refuses the general RAR 3 VM
(standard filters only) and verifies CRC32/BLAKE2sp. It contains no UnRAR source.
