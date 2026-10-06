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
Available runtime/license texts are not yet copied into the AppImage payload;
that executable packaging work continues. Complete composition, full notices
and obligations stay Open.

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

No source device, VM/Mac setup, upstream software execution, release tag,
publication, candidate or human GO occurs in this slice. Counts remain
139/161 preliminary Remediated, one Closed and 21 remaining issue remediations.
