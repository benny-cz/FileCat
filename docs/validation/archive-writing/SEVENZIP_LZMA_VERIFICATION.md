# Exact LZMA/LZMA2 verification component

The subsequent [7z header graph integration](SEVENZIP_HEADER_VERIFICATION.md) now uses this component for complete
encoded-header endings. Logical member graph/catalog integration remains unfinished.

**7 November 2026.** The internal `StrictLzmaReadStream` now verifies one complete, size-bounded LZMA or LZMA2
coder extent. Declared output size alone cannot establish completion. It requires real input bytes, a valid
range prefix and terminal state, exact compressed-byte consumption, no pending match, and the correct physical
end. LZMA accepts legal marked and markerless endings. LZMA2 additionally checks chunk lengths, initialization,
dictionary/state/property resets, property bounds and its final control byte; an LZMA end marker inside a chunk
refuses. Missing history cannot supply a short repeat.

The finish contract follows the pinned public-domain SDK's [LZMA interface and integrity checks](https://raw.githubusercontent.com/ip7z/7zip/b88c6bc09339de32f7d18bbab900686b244f42ba/C/LzmaDec.h)
and [LZMA2 framing decoder](https://raw.githubusercontent.com/ip7z/7zip/b88c6bc09339de32f7d18bbab900686b244f42ba/C/Lzma2Dec.c).
The separate C oracle calls those decoders with `LZMA_FINISH_END` and checks result, finish status and exact input
and output counts. Every accepted vector must also produce the expected SHA-256. Eight individually pinned
public-domain decoder/header files are private test inputs, with a [source ledger](../../../eng/validation/archive-writing/sevenzip-feasibility/lzma-end-sdk-sources.json).
No SDK decoder source or new dependency enters the production assembly.

The wrapper leaves the parent source open, distinguishes consumed bytes from buffered prefetch, caps source reads
at 65,524 bytes, honors cancellation and clears owned buffers and pooled dictionary storage on disposal/failure.
Dictionary claims are checked before dictionary allocation. A 64 MiB dictionary ceiling, optionally reduced to
16 MiB for a header consumer, is a profile policy rather than a format maximum. Small LZMA dictionary claims use
the SDK's 4 KiB minimum in the decoder's private copy; original catalog properties remain exact. Existing browsing
entrypoints keep their default behavior. The strict decoder entrypoint is synchronous.

The [checkpoint record](lzma-end-checkpoint.json) contains the three-platform outcomes, actual test runtimes,
source-packet identities, SDK binaries, complete frozen-artifact identities, compiler-input checksums and process
cleanup. Eighty-eight new cases cover empty/small/noisy/repeated streams, marked and markerless LZMA, LZMA2 compressed
and raw chunks, one-byte/short input reads, all truncated prefixes of selected streams, suffixes/concatenation,
wrong grants and output sizes, corrupt prefix/range endings, invalid chunks/resets/properties, missing history,
dictionary admission, cancellation, borrowed-source ownership and dictionary clearing. The independent SDK
comparison has 224 vectors: 63 accepted and 161 refused.

All 88 new cases pass on each platform. The resolved 1,138-case matrix has Mac **1,136 passed/two platform
skips**, Ubuntu **1,135/three**, and Windows **1,017/121**. Mac/Ubuntu totals combine the unchanged broad run
with the two fixture-only corrections described below; the checkpoint also retains the original failed outcomes.

The failed attempts remain explicit. The first Mac and Windows batches abort in a recursive test-only
`MemoryStream` override; a standalone bounded `Stream` corrects that helper. The Mac/Ubuntu broad follow-up has
two malformed positive raw-chunk fixtures: their 257-byte lengths omit the high byte. Production correctly
refuses them. Only those two fixtures are corrected/rerun; the other results are retained. Windows runs the
corrected complete matrix. Its embedded Python helper initially lacks a sibling-module import path; explicit
loading from the pinned source packet corrects that helper; a subsequent helper-only case-count assertion is
corrected from 90 to the actual 88. Only SDK/capture steps resume. These failures are never counted as passing
qualification. All application tests and native oracle execution occur on the
authorized Mac/VMs; the host performs edits, builds and static evidence checks only.

This component does **not** yet wire strict LZMA into encoded-header decoding or complete 7z member graphs.
PPMd/Deflate/BCJ2 and recursive filter/AES input/output ending verification, member CRC/SHA and complete source
rechecks, preservation admission, aggregate work/memory/pass budgets, transactions/password/UI/F6 and native
containment/fuzz/package/final-review gates remain required. The existing physical catalog still reports encoded
header endings as pending. No new write profile is registered or advertised by this checkpoint.
