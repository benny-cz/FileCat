# E-I03-APPIMAGE — pinned wrapper input and original license sources

2026-10-06. Preliminary §10.3/10.4 provenance execution for the actual
`145f569` development AppImage from run 37483079672. I03/I14 remain Open.

The original official runtime download still matches the package recipe's
SHA-256 `156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074`.
It is 944,632 bytes. Independent ELF section parsing finds that the actual
package prefix differs from this original only at the 16-byte `.digest_md5`
section, offset 932,096. Restoring only those original zero bytes reproduces
every runtime input byte and its pinned SHA-256; the package stays unchanged.
The exact declared appimagetool 1.9.1 source describes writing that section.

The first observer assumes whole-file MD5 with the digest section zeroed, and
fails against the embedded value. That assumption remains retained. Retrieved
immutable `src/digest.c` uses its own chunk/section algorithm; independent MD5
semantics remain unqualified. The corrected proof seals the exact byte
transformation and explicitly does not claim checksum authenticity, complete
binary/source reproducibility or successful independent embedded-MD5 validation.

The declared runtime source resolves to
`8f39b89e2ac31e1640b3d3f7e9a5108e6ce805fa`; appimagetool tag 1.9.1 resolves to
`8c8c91f762b412a19f4e8d2c4b35afb98f2d7c81`. Twenty license/build/README/LTR blobs,
four further build/link recipes and eight checksum/patch blobs independently
match Git SHA-1, length and SHA-256. These are immutable declarations, not proof
that the downloaded binaries reproduce from them.

The runtime Makefile links squashfuse, squashfuse_ll, zstd, zlib, fuse3 and
mimalloc statically; its base uses musl. The retained dependency recipe pins
libfuse 3.15.0 and squashfuse 0.5.2 source archives by exact SHA-256 and includes
a verified libfuse patch. Both original archives pass those hashes. First
filename selection retains two root LICENSE files; v2 includes GPL/LGPL-named
bodies from the same unchanged archives, retaining four originals. This does
not establish that every retained source file ships, exact binary versions of
all linked libraries, static-link/source obligations or legal eligibility.
At that artifact identity the available runtime/license texts are absent from
the AppImage payload. The working packaging correction below addresses this;
complete composition, full notices and obligations stay Open.

The package-declared LTR source's README contains project description/link and
its Extensions project declares version 1.0.22 while the package is 1.0.23.
Those inspected files provide no complete license text; they do not resolve
the recorded metadata/full-text/binary provenance gap. No absent license
eligibility or licensing breach is inferred from that bounded inspection.

Private `FileCatReleaseEvidence/appimage-runtime-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-runtime-observation-v1.json | 78fd48a295385c12f5795810bfe941c003b3e81f78a52ad01d81ea56cd149651 |
| immutable-source-v1/independent-source-blobs-v1.json | 4c6e4966708d39befc11be8808113a63664f8820228b17f4bc5da50ccb36e561 |
| immutable-source-v2/independent-build-source-blobs-v2.json | b4f052e92c8a7f7e769b0fc07ce5be5ef794a2aa8ad4e1469c024e288e5e062d |
| immutable-source-v3/independent-build-source-blobs-v3.json | 22e6cc28414f89eef3a2c26f209ceed914ba1fce028c1033d59a2f35f9937555 |
| independent-runtime-transform-v2.json | 5da48ecb2f44232e47968416312cb984848d7ce21d4c221cc2b13974f0ed24b2 |
| declared-dependency-notices-v1/independent-declared-runtime-notices-v1.json | a2515bf2a01b8c8b25fc176f80fef95edd22155570f452bd66eeb88e03013cfd |
| declared-dependency-notices-v2/independent-declared-runtime-notices-v2.json | 01e54de485853928426b984e476a77c4e8fd0a8b6a9fc7ca690f8dc2101a821a |

Working baseline `d32edb6`: five original verified texts are frozen in
`licenses/appimage-runtime`, with README/index (seven files). This retains the
runtime LICENSE, libfuse LICENSE/GPL2/LGPL2 and squashfuse LICENSE. The index
records declared sources and the exact original x64 runtime input; it explicitly
retains unresolved musl/zstd/zlib/mimalloc versions/notices, full static composition,
source obligations and legal eligibility. All seven staged Git blobs preserve
original bytes; the scoped attribute extends only to this frozen snapshot.

The existing locked, BCL-only notice tool now has an explicit AppImage-runtime
mode. Shared source/path/hash validation runs first; the mode also checks the
original input's exact size/SHA-256 before creating output. Linux invokes it
after runtime selection and before AppImage assembly, placing the texts in
`usr/lib/filecat/licenses/appimage-runtime`. Runtime/source/notice pins must be
updated together for a different reviewed input or architecture. The default
snapshot currently covers x64. The summary corrects its prior unproved binary
reproducibility claim and incomplete static-library list.

One real pinned-input positive copies all seven original files; fifteen fresh
refusals cover changed/missing/wrong-size/already-assembled runtime input,
changed/missing/extra/duplicate/escaping/schema/runtime-pin/malformed/missing
notice source, preserved existing output and output inside the snapshot. All
fail before new output, with exact original source/runtime/artifact pins preserved.
All nineteen original App-mode controls also pass against the changed tool:
seven real SC/FDD metadata positives and twelve refusals, including two pinned
license-byte controls. C# builds without warnings; shell syntax and ordinary
whitespace checks pass. Twelve staged source files/seven exact frozen files/
23-project scope and invocation ordering independently verify. Committed actual
packaging and hosted revalidation pass below; no full audit or candidate closure.

Private `FileCatReleaseEvidence/appimage-runtime-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| snapshot-adoption-v1/independent-runtime-snapshot-adoption-v1.json | 38408b5c5ea1d58ed594cf87b0b06722fd30dc48d09843faf9df13483cafebdf |
| snapshot-adoption-v1/independent-runtime-source-adoption-v2.json | c4fb42fa1c2f15049aa37e7dadb13dfb44bec94644a7b2b3b85650c8c194c62f |
| runtime-mode-controls-v1/independent-runtime-mode-controls-v1.json | a5ad8d445ee2a07ec76c6bb9488a5c8a4499e33f61bef131c6c1b3257bc41f91 |

Private `FileCatReleaseEvidence/nuget-locks-20261006-v3`:

| Retained path | SHA-256 |
|---|---|
| appimage-tool-app-mode-controls-v1/independent-appimage-tool-app-mode-controls-v1.json | 7a6f5b2a7c9f2fea2c77a2430edb564171f2a13a5e663d7b5875fbe4363d69df |
| appimage-tool-byte-refusals-v1/independent-appimage-tool-byte-refusals-v1.json | e875fc7f236a4de14dddef8e2c106880819b995402d56365abfc8dc534c71c0b |

Committed wrapper source `0d61dbb2e5ea5ddc4420ee4f1807b6ac4ad32cfa` seals
original push 37492084054 and development 37492315642, attempt 1. All four
required test lanes/policy/ARM64 startup/draw/installer pass. Nineteen/twenty-five
server digests, four/six clean SDK receipts, fourteen complete inventories per
run/all App identities/picture/reference/draft/set controls and 92/138 actual
locked graphs independently verify. Actual development Linux/Mac packaging,
install/version/signature/icon checks pass; all retrieved manifest hashes match.
Draft publication skips. No tag or release is created.

Actual x64/ARM64 authoritative Windows packaging from raw Git source additionally
passes both 38-package inventory gates, four SC/FDD payloads and four ZIPs' exact
50-file App notice sets. Independent readers verify another 200 App notice files
in the four actual Unix archives, plus **all seven new wrapper files inside the
AppImage**: 207 Unix archive checks. Every package remains unchanged. Native
desktop/candidate qualification and complete legal/static/source audit remain Open.

Private `FileCatReleaseEvidence/ci-37492084054-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 71da31371be8f1a1539e810d9e1f04470b60c55999e924047fda37247131b06d |
| independent-fixture-ci-v1.json | e47b23ab1202b96eeca6b8388c760822a3df4fbb3ef055f10491c6c815702bb3 |
| independent-producer-policy-ci-v1.json | 91672007c71850f16c441a7278f4343a2b5e72481827ec930c9a03b2b3bc8525 |
| independent-draft-guard-ci-v1.json | 9d81c38f3d05efeb9d4f917905944775a6d47bf1e9d28763544b8df5127b0f37 |
| independent-separation-ci-v1.json | c9fc8fe30a9c3a80daf6e63cff23c5bd4681b6dc695eb19737d2b14a6d8e0e63 |
| independent-restore-ci-v1.json | cc7224be916471c78117d50d28d04ce97fbea04f83425af67791015d87a42755 |

Private `FileCatReleaseEvidence/ci-37492315642-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 25b9e81e5a10f8dd78d0b669be4e46832d229915be38aa81bd7ad4f195fe1587 |
| independent-fixture-ci-v1.json | 02f7d49d9160b23a348752960ab6041aea67436944eff9e9f1fbd53a425fab5a |
| independent-producer-policy-ci-v1.json | 338de5b5dccdcd872bdc2dd23be565b256924ad8936d6d11fd26a7bbfdee36c8 |
| independent-draft-guard-ci-v1.json | 3b546be28511a98db578ba1a930206a65581b59b219db526c09b0c717835d54a |
| independent-separation-ci-v1.json | 7ca98fc1564d1a627fd55a5adcc5cc501a833a005cd0f37f692fa8f57819a1ee |
| independent-restore-ci-v1.json | 04eb72ad13d9d749d2bbaf17705c4699447db85812b5a72aa56fc167cb7a7b00 |

Private `FileCatReleaseEvidence/wrapper-production-windows-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-wrapper-regression-v1.json | aa618dd1350640b186a6d968b753d5d5ff5952505c2b767b85e9aad7df7e9ddc |

Private `FileCatReleaseEvidence/wrapper-package-inspection-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-package-notice-bytes-v1.json | 9c20b16a0d40a8063d2226359defb6934228c695cd11a5759d850900a2df31ba |

No source device, VM/Mac setup, upstream software execution, release tag,
publication, candidate or human GO occurs in this slice. Counts remain
139/161 preliminary Remediated, one Closed and 21 remaining issue remediations.
