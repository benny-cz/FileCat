# E-I03-NATIVE — native helper compiler inputs and linked-symbol provenance

2026-10-06. Preliminary I03/I18 improvement at **573ed2cfe31e5698ee668e5aa28b2c82ccc8b942**, not issue closure. No candidate or stable GO.

The original native receipt recorded seven source/resource/compiler/output pins. Diagnostic builds now retain MSVC source-dependency JSON, original compiler/linker stdout and stderr, and the actual linked-symbol map. The receipt pins distinct C++ reported headers, linker-searched library files and reported compiler components, including the linker, after successful compilation. CI uploads these reports and maps alongside its original receipts and executable. Source, manifest, icon, generated resource, compiler and output pins remain present.

The independent diagnostic comparison preserves exact **8b4be9d9a4be824614cdc421f34cbc5981d685aa** source/resources, original version and embedded source revision. Adding the diagnostics/map yields byte-identical native executables on x64 and ARM64. This comparison controls version/revision resources; new committed output naturally embeds the new source revision. No helper runtime source or native execution behavior changes in this slice.

Four working and four clean committed SC/FDD publications pass. All **927 raw Git blobs**, their blob identities/bytes, unchanged source files and every published output verify. Each publication retains its actual native receipt/reports before another mode can overwrite the intermediate directory. Local x64 reports 229 distinct headers, 11 searched libraries and seven compiler components; ARM64 reports 220, 12 and seven. Every local declared file is independently read and its size/SHA-256 verified. Repeated include entries are retained in the original reports and are not counted as additional distinct files. ARM64 additionally searches runtimeobject.lib; no identical cross-architecture library-count assumption remains.

Actual maps identify incorporated objects from LIBCMT, libcpmt, libvcruntime and libucrt. Library search alone is not incorporation proof. MSVC documents the /MT [native runtime families](https://learn.microsoft.com/en-us/cpp/c-runtime-library/crt-library-features?view=msvc-170); its [sourceDependencies report](https://learn.microsoft.com/en-us/cpp/build/reference/sourcedependencies?view=msvc-170) supplies the C++ include graph. Microsoft's [STL license](https://github.com/microsoft/STL/blob/main/LICENSE.txt) is Apache 2.0 with the LLVM exception. This does not classify every SDK/compiler runtime input. [SignPath Foundation's terms](https://signpath.org/terms.html) permit System Libraries subject to their definition; compiler-runtime eligibility must be determined against actual components and release policy, rather than inferred from /MT alone. No legal/provider acceptance decision is fabricated.

[CI 37443418957 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37443418957) passes all four required lanes, ten downloaded artifact server digests, four build receipts and fourteen complete TRX inventories. Both downloaded Windows native executables match their compiler/source receipts. Each corresponding receipt has its original downloaded dependency report, map and diagnostics; declared input path sets match those reports and the report/map byte pins independently verify. Hosted x64 reports 231 headers/11 libraries/seven components; ARM64 reports 222/12/seven. The hosted compiler/header/library files themselves are not retrieved, so their hashes remain producer observations. ARM64 drawing/startup and installer compilation pass; tag package jobs skip as expected.

Failed private observers remain retained: raw versus distinct include-count assumption, an x64-only library-count assumption applied to ARM64, and a case-sensitive source filename assumption although MSVC reports a lowercase Windows path. They do not represent native compiler or shipped helper failures. The corrected audits independently compare each architecture's actual report sets. Full resource-compiler header dependencies, complete compiler/input read tracing, license/system-library classification, complete runtime/restore/Python/server/image inventory, owner-controlled evidence retention, release controls and final artifact/candidate qualification remain open. These pins explicitly occur after compilation and do not prove which individual bytes were read.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence`. Scripts, original reports/maps/diagnostics, source archive and full payloads remain there.

| Retained path under FileCatReleaseEvidence | SHA-256 |
|---|---|
| broker-loader-20261006/native-input-receipts-working-v3/independent-receipts-v1.json | 788217018abed9470f6010d83962a2a029025ebb8a9531a09878a6861b82ed13 |
| broker-loader-20261006/native-input-working-publish-v2/producer.json | 609927eb3553783b9130df9f3abc1eacc99d9936412c0629d3b9b5a909f02c7b |
| broker-loader-20261006/native-input-committed-v1/producer-native-bootstrap-v1.json | 7af043e6a33c533af4c9184f6432c547a83354b92a8a28354b6828e8c6bbf1e2 |
| ci-37443418957-attempt1/independent-ci.json | e0556663fce2c26291ca6808315d90b70407a6b2d96cf1df584d9ef8fea86b52 |
| ci-37443418957-attempt1/independent-ci-native-bootstrap-v1.json | 378f38c0c64d3f324647b6327eb8b4f7f71500d07b678a1b78bd4758ddbb080a |
| ci-37443418957-attempt1/independent-ci-native-inputs-v1.json | bb4116cf4032a2d204eb79142cfb324cd9dd65b4a60983e07afd2cf368f445f9 |
| broker-loader-20261006/independent-i03-native-input-seal-v1.json | aaef0ad7e0d28b183c8caa4815df4143f959591645f783d5904884fc1ea42f89 |

137/159 preliminary remediations, one Closed, 21 remaining issue remediations. All 24 campaigns require final qualification; NO-GO.
