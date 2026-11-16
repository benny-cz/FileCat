# Exact raw-Deflate verification component

**7 November 2026.** `StrictDeflateReadStream` adds complete raw-Deflate verification for the forthcoming 7z member
graph. It requires a real final block, the declared output length, exact compressed-byte consumption and EOF on
the granted input. A decoder returning no progress at truncated EOF refuses. Trailing bytes, concatenated streams,
reserved block/symbol codes, damaged stored-block lengths, missing history and wrong declared sizes refuse.
Legal ignored padding bits may be nonzero. The source is borrowed, cancellation is checked during reads, and the
component bounds physical and output extents before initializing the codec. Graph-wide resource accounting,
preservation admission and worker/native qualification remain separate gates. No new write profile is registered.

The existing vendored inflater's fast and slow backward-copy paths previously allowed distances preceding all
produced history, potentially reading a previous user's pooled window. Both paths now require actual produced
history or an explicitly supplied preset dictionary. Reset/synchronization establishes a new history origin even
when a cumulative output counter is retained. Window storage is cleared when acquired and before returning it
to its owner. The strict wrapper clears its own input/output arrays and ends the codec on disposal without closing
its source. Existing codec licence notices remain; no new runtime codec source or dependency was imported.

The initial component run also exposed a legacy Deflate encoder ownership bug: `leaveOpen` skipped finalization
and codec cleanup. Sync and async disposal now always finalize/release the codec; the existing base stream's
leave-open policy controls the captive stream. Disposing without any write emits a complete empty stream.
Tests cover both disposal routes, both captive-stream lifetimes and empty/nonempty data. The 7z creation adapter
uses the BCL Deflate encoder; this fix applies to the separate vendored codec exercised by these fixtures.

The [checkpoint record](sevenzip-deflate-verification-checkpoint.json) identifies source packets, frozen evidence,
actual test CLR, Windows standard account, writing dependency closure and compiler-source snapshots. Each platform
runs **2,849 archive cases**, including **75 new cases**, with zero failures:

| Platform | Passed | Skipped | New cases passed |
|---|---:|---:|---:|
| Mac | 2,794 | 55 | 75 |
| Ubuntu VM | 2,807 | 42 | 75 |
| Windows VM, standard account | 2,629 | 220 | 75 |

The new matrix covers stored/fast/best encoding, empty through 131,089-byte data, noisy/repeated input, one-byte
and larger reads, every prefix of two fixtures, malformed blocks/history/sizes/endings, legal padding, preset
dictionaries, cancellation, pre-read budget refusal, history reset, retained-owner cleanup and encoder lifetimes.
All six changed C# inputs match compiler-source checksums in the frozen platform binaries.

Python's independent native zlib agrees on **265 vectors per platform: 55 accepted and 210 refused**, with exact
accepted lengths/SHA-256, finished streams and no trailing input. It separately verifies the zlib preset-dictionary
fixture. The raw compressed packet hashes match across all three systems. The recorded native runtimes differ
by platform; their exact versions are in the checkpoint. Four controlled tests replace only the vendored codec
DLL/PDB with the preceding frozen version and reproduce the expected missing-history/reset/cleanup failures.
Those four tests pass against the current code in the full application suite.

The checkpoint preserves the initial **2,839-case** run separately: **48 failures** on each platform, all in the new
component tests. The small fixture encoder outputs were unfinished because of `leaveOpen`; direct-codec assertions
also expected a return code where this codec throws `ZlibException`. The implementation/assertions were corrected,
ten lifetime cases added and the full expanded application matrix repeated. After that passed, the Unix comparison
helper's file-copy step omitted executable permissions and failed to launch its control tests. `copy2` preserves
those permissions; a guarded resume runs just the four controls and remaining evidence collection, retaining the
failed launch artifacts. It does not repeat the passing application suites or native zlib comparisons. The frozen
Unix evidence contains the exact corrected helper/resume scripts alongside the original source packet.

Host activity is limited to edits, builds and static frozen-artifact validation. Application tests and native oracles
run on the authorized Mac/VMs. Final censuses contain no owned FileCat/test processes and preserve the Mac's
until-reboot wake helper. Complete 7z member graphs/catalog, filter ownership/properties, parent preservation planning,
role-bound workers, native publication, shared RAR/7z workflows and the remaining UI/resource/crash/fuzz/package
qualification are still open under the controlling plan.
