# Exact PPMd7 and BCJ2 verification components

**7 November 2026.** Strict stream-only PPMd7 and BCJ2 readers now verify complete coder extents for the future 7z
logical catalog. This is a decoder/ownership checkpoint, not complete catalog, workflow or release qualification.
No new write profile is registered. The parent/worker/native transaction and application integration remain open.

`StrictPpmdReadStream` uses the existing BSD shared PPMd-H model with an opt-in known-size 7z range reader.
Initialization requires real bytes, a legal prefix and a valid range code. Decoding stops at the declared output
size, then requires the terminal code to be zero and every declared compressed byte to have been consumed.
It does not decode a speculative additional end symbol. Missing/truncated input cannot be replaced by synthetic
0xFF bytes. Wrong sizes, unfinished range state and trailing compressed data refuse. Cancellation is checked
during symbol decoding. Model order remains 2–64; the strict component's model ceiling is 32 MiB, with a 2 KiB
minimum. These are bounded verification-profile policies, not format maxima or impossibility claims.

`StrictBcj2ReadStream` verifies all four branches, call/jump alignment, the relationship between input/output
sizes, the range prefix and terminal code, complete output, and exact EOF on every branch. It follows converted
address output through completion before returning EOF. Its opt-in decoder refuses output beyond the known
extent and checks cancellation during iteration. The existing browsing constructor retains its tolerant defaults.
Strict wrappers borrow their sources, including on initialization failure and cancellation.

The shared `StrictCoderInput` counts bytes actually consumed by the coder separately from bytes fetched into its
bounded read-ahead buffer. End verification requires both counts to exhaust the exact extent, no unread buffered
bytes and EOF from the granted predecessor. Its owned buffer is cleared on disposal. LZMA/LZMA2 now reuse this
same input boundary, with the existing exact-ending regression and SDK comparison repeated after that change.

The PPMd 7z reader's previous model disposal method was empty. It now clears and releases its owned model arrays;
the encoder uses the same cleanup on success or failure, and model growth clears the superseded array. Failed
reader initialization also disposes its model. Tests retain references to the old state arrays and require them
to be cleared. This is an owned-model/buffer claim, not a claim about erasing immutable strings or all process
memory. The existing BSD attribution and source-built component notice remain; the third-party change inventory
records the opt-in verification and cleanup changes. No UnRAR source or new runtime dependency was imported.

The native test oracles use individually pinned public-domain SDK inputs at commit
`b88c6bc09339de32f7d18bbab900686b244f42ba`. The [source ledger](../../../eng/validation/archive-writing/sevenzip-feasibility/range-ending-sdk-sources.json)
records the twelve exact files used for PPMd7/BCJ2 testing. The known-size finish rules are checked against
[Ppmd7.h](https://raw.githubusercontent.com/ip7z/7zip/b88c6bc09339de32f7d18bbab900686b244f42ba/C/Ppmd7.h)
and [Bcj2.h](https://raw.githubusercontent.com/ip7z/7zip/b88c6bc09339de32f7d18bbab900686b244f42ba/C/Bcj2.h).
The C wrapper calls the actual SDK decoder, records exact consumption/finish decisions and hashes every accepted
output. SDK sources stay private test inputs and are not compiled into the application.

The [checkpoint record](sevenzip-range-verification-checkpoint.json) pins the complete source/tool packets,
frozen evidence, actual test CLR, standard Windows account, dependency closure and compiler-source checksums.
Each platform runs the combined **2,771-case** archive matrix, including **99 new cases**. Outcomes are Mac
**2,716 passed / 55 skipped**, Ubuntu **2,729 / 42**, and Windows **2,551 / 220**, with zero failures; all 99 new
cases pass everywhere. Cases cover PPMd orders/model sizes, empty/noisy/repeated data and restarts, every compressed
prefix of a fixture, trailing data, wrong range state/sizes, BCJ2 branch boundaries and malformed extents, model
cleanup, pre-allocation refusal and cancellation/borrowed ownership.

A subsequent source review corrected BCJ2's oversized-extent reason from `InvalidArchive` to `BudgetExceeded`.
Three additional cases require the budget refusal before any source read. The corrected full matrix has
**2,774 cases** per platform and **102 new cases**: Mac **2,719 passed / 55 skipped**, Ubuntu **2,732 / 42**,
Windows **2,554 / 220**, with zero failures. The checkpoint keeps the initial passing phase separately from this
corrected source and its final compiler-source snapshots.

All three SDK comparisons agree on **206 vectors**: PPMd accepts 56 and refuses 123; BCJ2 accepts 18 and refuses
nine. The repeated LZMA/LZMA2 comparison also agrees on all **224 vectors**, accepting 63 and refusing 161. Every
accepted output has the expected length and SHA-256. Frozen artifacts are checked file by file, and all ten changed
C# inputs match compiler-source snapshots. The host performs edits, builds and static evidence verification only;
native oracles and application tests execute on the authorized Mac/VMs. Final censuses have no owned FileCat/test
processes and retain the Mac's until-reboot wake helper.

The static collector's initial cross-platform comparison used the operating system's filename ordering; Windows
sorted one uppercase LZMA fixture differently. Comparing by exact relative-path strings establishes identical
hashes for all 430 packets across the three systems. This collector correction did not require application retests;
the subsequent expanded runtime batch was for the actual BCJ2 budget-reason correction.

Complete strict 7z member graph integration, raw-deflate ending/consumption verification, filter graph ownership,
all-member SHA/CRC/source revisions and preservation admission remain unfinished. These components do not authorize
an update or publication. Broader compression/profile gaps, native resource/UI/crash/fuzz/package qualification
and the final independent review remain required by the controlling plan.
