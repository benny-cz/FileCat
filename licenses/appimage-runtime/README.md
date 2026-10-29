# AppImage wrapper notices

This frozen snapshot retains the original type2-runtime root LICENSE and the exact
libfuse, squashfuse, musl, zstd, zlib and mimalloc source license texts. `index.json`
records their original byte hashes, immutable source-archive hashes and the exact
runtime input SHA-256. FileCat keeps that runtime input in its repository
(`eng/linux/appimage-runtime/runtime-x86_64`); it was retrieved once from GitHub
asset ID 657374641 of upstream's `continuous` release, which later upstream builds
replace. The index also records the asset digest, pinned source revision, committed
signing-key blob/fingerprint, detached signature asset, official producer run/job,
base-image digest and exact package versions observed in that producer log. The
package tool verifies the runtime and snapshot before copying this directory into
the AppImage payload.

The runtime statically links libfuse 3.15.0, licensed under LGPL-2.1. Every FileCat
AppImage release therefore also publishes
`FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz`: the libfuse 3.15.0,
squashfuse 0.5.2 and type2-runtime (commit fedfad51) sources exactly as published,
the producer's package list, a README and `rebuild.sh`. The script rebuilds the
runtime in the producer's Alpine 3.21 image and relinks a FileCat AppImage with a
modified libfuse. On 2026-10-29 it rebuilt this runtime byte for byte from those
sources. `index.json` names the source archives under `CorrespondingSource`.

The runtime recipe declares static squashfuse, zstd, zlib, libfuse, mimalloc and
musl inputs. Exact declared source versions and producer package versions are now
bound to retained original license texts. This snapshot does not establish legal
eligibility, and it is not a qualified release candidate. The release audit
remains Open. Retaining GPL/LGPL source texts does not assert that every file or
tool in those source archives ships in FileCat.

Review and update the runtime bytes, source/provenance references, source bundle
and these notice texts together when changing the pinned input or selecting
another architecture. Preserve the original upstream bytes and revalidate the
final AppImage prefix and all packaged notice bytes after assembly.
