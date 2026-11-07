# Strict RAR5 complete content catalog

**7 November 2026.** `StrictRar5Catalog` follows the physical framing observer with complete member decoding and
SHA-256 observations. It checks recorded CRC32/BLAKE2sp, including encrypted checksum protection, exact output
sizes, compressed block endings and packed extents. Both passes hash every physical byte and compare revisions;
same-length header-only changes are detected too. Every member is verified, including members a later plan might
delete. Caller-owned source streams remain open on success, refusal and cancellation.

This result is **not an `ArchiveAssessment`, writable-profile admission or publication authority**. Exact metadata
and occurrence information remain in the physical observations. A preservation implementation must still reproduce
every present field and understood source feature. The new writers remain unregistered. The next milestone is
strict 7z catalogs and shared RAR/7z transaction, password and workflow integration, before optional XZ/ISO work
and final independent review, as prioritized in plan §1.2.

Strict decoding requires a complete last block after the declared output and accounts for prefetched packed bytes.
Incomplete declared Huffman symbols and missing last-block markers refuse. Bits outside the declared final-byte
bit count are padding; they are not newly required to be zero. Encrypted streams allow at most fifteen trailing
plaintext padding bytes, within the checked block-aligned packed extent. Reserved block flags refuse. A compressed
non-solid file without a block refuses; ordinary zero-length creation uses the stored method.

The catalog observes RAR5 Delta/E8/E8E9/ARM filter use after verifying filtered output and refuses filters starting
or ending beyond the member. Filter observation does not establish filter preservation: matching RAR filter
encoding and its qualification remain missing work. Source dictionaries above **64 MiB** receive a budget refusal
before decoder allocation; this is a profile policy, not a format maximum. The current writer still emits its
bounded algorithm-version-0/128-KiB profile; this checkpoint does not add RAR7 encoding. RAR7 dictionary fractions
are now correctly observed by browsing even when the v5-compatible algorithm flag selects its smaller distance
alphabet. Wrapped KDF budget refusals retain `BudgetExceeded` instead of describing an expensive source as corrupt.

The worker writing assembly links only the selected existing BSD decoder/window/bit/Huffman/filter/BLAKE2sp and
member-crypto sources. It does not link the browsing reader, volume pathname discovery, Rar15 or DiscUtils. The
existing BSD attribution and licence remain in effect; no restricted UnRAR source or new runtime package was imported.

Owned bit-reader/window/filter scratch buffers are cleared on reset, growth or disposal as appropriate. The strict
catalog disposes them and its BLAKE2sp buffers on success and failure. Browsing retains unfinished independent
decoder runs so another open cannot erase an earlier borrowed stream; later opens reclaim completed runs, and
reader disposal clears all remaining tracked bit/window buffers. This is an owned-buffer claim, not a claim about
erasing immutable strings, framework-internal copies or all process memory.

The evidence separates the initial full matrix, corrected fixture helper and subsequent ownership correction.
The initial 964-case batch had exactly three failed interoperability tests on each platform: the independent
fixture envelopes contain unsupported main-header metadata. The other outcomes were Mac 959 passed/two skips,
Ubuntu 958/three, Windows 840/121. Those failures are retained explicitly, not relabeled as a passing matrix.

The corrected helper first requires the original-envelope refusal, then rebuilds only a minimal **test** envelope
around each fixture's unchanged independently encoded packed members and checksum data. Content hashes must match
the test-only reference reader for all three files in each fixture. It adds no production conversion route.
The focused follow-up reruns only those three cases; its source packet differs from the initial packet only in
that test file. The final affected RAR/reader/crypto batch also includes the corrected helper and a borrowed-stream
lifetime regression. Exact outcomes and frozen artifact identities are in the accompanying machine-readable record.

The final 380-case affected batch passes with zero failures: Mac and Ubuntu each pass all 380; Windows passes
378 with two Unix FIFO skips. All 46 new catalog/regression cases pass on every platform. The focused fixture
follow-up also passes all three cases everywhere. All nine frozen bundles have been verified file by file against
their manifests, and the twelve current changed C# inputs match the final compiler-source snapshots. See the
[machine-readable checkpoint](rar5-logical-catalog-checkpoint.json) for source packet hashes, each phase's actual
outcomes, dependency closure, observed executing CLR, source audit and final census. Windows evidence capture
retains its interrupted helper/audit attempts; only incomplete collection was resumed, without repeating the full
application matrix. The corrected capture preserves the compiler path's spelling and postpones while another
tester is active.

The **46 catalog/regression cases** cover all six methods and three password modes; empty files/directories,
Unicode and exact timestamps; duplicate occurrences and native owner bytes; independent CRC/BLAKE2sp vectors and
fixture content; every compressed-prefix truncation; extra packed/output bytes, incomplete symbols and missing end
markers; all four filters and out-of-member filters; missing/wrong passwords and low-budget KDF refusal; second-pass
source changes/cancellation; decoder reuse, RAR7 dictionary flags; owned-buffer cleanup and independent stream lifetime.

The host was used for edits/builds/static artifact verification only. Application tests ran on the Mac and disposable
Ubuntu/Windows guests, with Windows tests under the loaded standard account. Compiler-source snapshots and actual
dependency manifests accompany each frozen batch. The Mac wake helper is preserved and the final batch census
must show no test/FileCat instances. This is component evidence; shared native containment, full update/create/edit/F6
flows, password UI, resource/fuzz/crash/six-RID/package qualification and the final independent review remain open.
