# AppImage wrapper notices

This frozen snapshot retains the original type2-runtime root LICENSE and the exact
libfuse, squashfuse, musl, zstd, zlib and mimalloc source license texts. `index.json`
records their original byte hashes, immutable source-archive hashes and the exact
runtime input SHA-256. The runtime is retrieved through an immutable GitHub asset
ID; the index also records the asset digest, pinned source revision, committed
signing-key blob/fingerprint, detached signature asset, official producer run/job,
base-image digest and exact package versions observed in that producer log. The
package tool verifies the runtime and snapshot before copying this directory into
the AppImage payload.

The runtime recipe declares static squashfuse, zstd, zlib, libfuse, mimalloc and
musl inputs. Exact declared source versions and producer package versions are now
bound to retained original license texts. This snapshot still does not establish
complete actual binary composition, static-link/source obligations, reproducible
runtime bytes or legal eligibility, and it is not a qualified release candidate.
The release audit remains Open. Retaining GPL/LGPL source texts does not assert
that every file or tool in those source archives ships in FileCat.

Review and update runtime bytes, source/provenance references and these notice
texts together when changing the pinned input or selecting another architecture.
Preserve the original upstream bytes and revalidate the final AppImage prefix and
all packaged notice bytes after assembly.
